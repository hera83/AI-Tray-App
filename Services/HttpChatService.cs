using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrayApp.Infrastructure;

namespace TrayApp.Services
{
    public class HttpChatService : IChatService, IDisposable
    {
        private enum EndpointProtocol
        {
            OpenAiCompatible,
            OllamaChat
        }

        private readonly HttpClient _http;
        private readonly ISettingsService _settings;
        private readonly IAppLogger _logger;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public HttpChatService(ISettingsService settings, IAppLogger logger)
        {
            _settings = settings;
            _logger = logger;
            _http = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public async IAsyncEnumerable<string> StreamResponseAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var settings = _settings.Settings;
            if (string.IsNullOrWhiteSpace(settings.AiEndpoint))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ikke konfigureret. Åbn Indstillinger og angiv en base URL.");

            if (!Uri.TryCreate(settings.AiEndpoint, UriKind.Absolute, out var endpointUri))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ugyldigt. Kontrollér URL'en i Indstillinger.");

            if (!NetworkInterface.GetIsNetworkAvailable())
                throw new ChatServiceException(
                    ChatServiceErrorKind.Connection,
                    "Ingen netværksforbindelse fundet. Kontrollér internetforbindelsen og prøv igen.");

            var protocol = DetectEndpointProtocol(endpointUri);
            var requestUri = ResolveRequestUri(endpointUri, protocol);
            var effectiveSystemPrompt = BuildEffectiveSystemPrompt(settings);
            var messages = BuildMessages(prompt, effectiveSystemPrompt);
            var payload = protocol == EndpointProtocol.OllamaChat
                ? SerializeOllamaRequest(settings, messages)
                : SerializeOpenAiRequest(settings, messages);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
            ApplyAuthentication(request, settings.ApiKey, protocol);

            _logger.LogInfo($"Sender AI-request til {requestUri.Host}{requestUri.AbsolutePath} (streaming={settings.UseStreaming}, protocol={protocol}).");
            using var response = await SendAsync(request, settings.UseStreaming, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning($"AI endpoint returnerede {(int)response.StatusCode} {response.ReasonPhrase}.");
                throw CreateHttpFailure((int)response.StatusCode, body);
            }

            if (protocol == EndpointProtocol.OllamaChat)
            {
                await foreach (var chunk in ReadOllamaResponseAsync(response, settings.UseStreaming, cancellationToken))
                    yield return chunk;

                yield break;
            }

            await foreach (var chunk in ReadOpenAiResponseAsync(response, settings.UseStreaming, cancellationToken))
                yield return chunk;
        }

        public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        {
            var settings = _settings.Settings;
            if (string.IsNullOrWhiteSpace(settings.AiEndpoint))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ikke konfigureret. Åbn Indstillinger og angiv en base URL.");

            if (!Uri.TryCreate(settings.AiEndpoint, UriKind.Absolute, out var endpointUri))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ugyldigt. Kontrollér URL'en i Indstillinger.");

            if (!NetworkInterface.GetIsNetworkAvailable())
                throw new ChatServiceException(
                    ChatServiceErrorKind.Connection,
                    "Ingen netværksforbindelse fundet. Kontrollér internetforbindelsen og prøv igen.");

            var protocol = DetectEndpointProtocol(endpointUri);
            var requestUri = ResolveModelsRequestUri(endpointUri, protocol);

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            ApplyAuthentication(request, settings.ApiKey, protocol);

            _logger.LogInfo($"Henter model-liste fra {requestUri.Host}{requestUri.AbsolutePath} (protocol={protocol}).");
            using var response = await SendAsync(request, useStreaming: false, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw CreateHttpFailure((int)response.StatusCode, body);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var models = ParseModelNames(json, protocol);
            if (models.Count == 0)
                throw new ChatServiceException(
                    ChatServiceErrorKind.Server,
                    "Gatewayen returnerede ingen modeller.");

            return models;
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        private static List<AiMessage> BuildMessages(string prompt, string? systemPrompt)
        {
            var messages = new List<AiMessage>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new AiMessage { Role = "system", Content = systemPrompt });

            messages.Add(new AiMessage { Role = "user", Content = prompt });
            return messages;
        }

        private static string? BuildEffectiveSystemPrompt(AppSettings settings)
        {
            var trimmedSystemPrompt = string.IsNullOrWhiteSpace(settings.SystemPrompt)
                ? null
                : settings.SystemPrompt.Trim();

            var userMetadataLines = BuildUserMetadataLines(settings);
            if (userMetadataLines.Count == 0)
                return trimmedSystemPrompt;

            var userContextBlock =
                "Brugerprofil:\n- " +
                string.Join("\n- ", userMetadataLines) +
                "\n\nBrug kun brugerprofilen, når det er relevant for brugerens forespørgsel, så svarene kan blive mere personlige uden at antage unødige detaljer.";

            if (trimmedSystemPrompt == null)
                return userContextBlock;

            return trimmedSystemPrompt + "\n\n" + userContextBlock;
        }

        private static List<string> BuildUserMetadataLines(AppSettings settings)
        {
            var lines = new List<string>();

            AddUserMetadataLine(lines, "Fulde navn", settings.UserFullName);
            AddUserMetadataLine(lines, "Foretrukket navn", settings.UserPreferredName);
            AddUserMetadataLine(lines, "Rolle/arbejde", settings.UserOccupation);
            AddUserMetadataLine(lines, "Interesser/fokus", settings.UserInterests);
            AddUserMetadataLine(lines, "Foretrukken svarstil", settings.UserResponseStyle);
            AddUserMetadataLine(lines, "Ekstra kontekst", settings.UserAdditionalContext);

            if (lines.Count == 0)
                AddUserMetadataLine(lines, "Ekstra kontekst", settings.UserProfile);

            return lines;
        }

        private static void AddUserMetadataLine(List<string> lines, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            lines.Add($"{label}: {value.Trim()}");
        }

        private string SerializeOpenAiRequest(AppSettings settings, List<AiMessage> messages)
        {
            return JsonSerializer.Serialize(new AiChatRequest
            {
                Model = settings.Model,
                Temperature = settings.Temperature,
                Stream = settings.UseStreaming,
                Messages = messages
            }, _jsonOptions);
        }

        private string SerializeOllamaRequest(AppSettings settings, List<AiMessage> messages)
        {
            return JsonSerializer.Serialize(new OllamaChatRequest
            {
                Model = settings.Model,
                Stream = settings.UseStreaming,
                Messages = messages
            }, _jsonOptions);
        }

        private static void ApplyAuthentication(HttpRequestMessage request, string? apiKey, EndpointProtocol protocol)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return;

            if (protocol == EndpointProtocol.OllamaChat)
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            else
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        private async IAsyncEnumerable<string> ReadOpenAiResponseAsync(HttpResponseMessage response, bool useStreaming, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!useStreaming)
            {
                var respJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string output;
                try
                {
                    var resp = JsonSerializer.Deserialize<AiChatResponse>(respJson, _jsonOptions);
                    var text = resp?.Choices is { Length: > 0 }
                        ? resp.Choices[0].Message?.Content
                        : null;

                    _logger.LogInfo("AI-svar modtaget (non-streaming).");
                    output = text ?? string.Empty;
                }
                catch
                {
                    _logger.LogWarning("Kunne ikke parse AI-svar som standard chat-format. Returnerer rå tekst.");
                    output = respJson;
                }

                yield return output;
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var buffer = new char[1024];
            var receivedAnyData = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read == 0)
                    break;

                receivedAnyData = true;
                yield return new string(buffer, 0, read);
            }

            if (!receivedAnyData)
            {
                _logger.LogWarning("Streaming-svar afsluttede uden data.");
                throw new ChatServiceException(
                    ChatServiceErrorKind.Server,
                    "Serveren svarede uden indhold. Prøv igen om lidt.");
            }

            _logger.LogInfo("AI-svar modtaget (streaming).");
        }

        private async IAsyncEnumerable<string> ReadOllamaResponseAsync(HttpResponseMessage response, bool useStreaming, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!useStreaming)
            {
                var respJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string output;
                try
                {
                    var resp = JsonSerializer.Deserialize<OllamaChatResponse>(respJson, _jsonOptions);
                    _logger.LogInfo("AI-svar modtaget (ollama non-streaming).");
                    output = resp?.Message?.Content ?? string.Empty;
                }
                catch
                {
                    _logger.LogWarning("Kunne ikke parse Ollama-svar som standardformat. Returnerer rå tekst.");
                    output = respJson;
                }

                yield return output;
                yield break;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var receivedAnyFrame = false;
            var receivedAnyText = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                receivedAnyFrame = true;
                string? content = null;

                try
                {
                    var resp = JsonSerializer.Deserialize<OllamaChatResponse>(line, _jsonOptions);
                    content = resp?.Message?.Content;
                }
                catch
                {
                    content = line;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    receivedAnyText = true;
                    yield return content;
                }
            }

            if (!receivedAnyFrame || !receivedAnyText)
            {
                _logger.LogWarning("Streaming-svar fra Ollama afsluttede uden indhold.");
                throw new ChatServiceException(
                    ChatServiceErrorKind.Server,
                    "Serveren svarede uden indhold. Prøv igen om lidt.");
            }

            _logger.LogInfo("AI-svar modtaget (ollama streaming).");
        }

        private static EndpointProtocol DetectEndpointProtocol(Uri endpointUri)
        {
            var path = endpointUri.AbsolutePath.Trim();
            if (string.IsNullOrWhiteSpace(path) || path == "/" || path.Contains("/ollama/api/chat", StringComparison.OrdinalIgnoreCase))
                return EndpointProtocol.OllamaChat;

            return EndpointProtocol.OpenAiCompatible;
        }

        private static Uri ResolveRequestUri(Uri endpointUri, EndpointProtocol protocol)
        {
            if (protocol != EndpointProtocol.OllamaChat)
                return endpointUri;

            var path = endpointUri.AbsolutePath.Trim();
            if (string.IsNullOrWhiteSpace(path) || path == "/")
                return new Uri(endpointUri, "/v1/ollama/api/chat");

            return endpointUri;
        }

        private static Uri ResolveModelsRequestUri(Uri endpointUri, EndpointProtocol protocol)
        {
            if (protocol == EndpointProtocol.OllamaChat)
                return new Uri(endpointUri, "/v1/ollama/api/tags");

            var path = endpointUri.AbsolutePath.Trim();
            if (string.IsNullOrWhiteSpace(path) || path == "/")
                return new Uri(endpointUri, "/v1/models");

            if (path.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return new Uri(endpointUri, "/v1/models");

            if (path.Contains("/models", StringComparison.OrdinalIgnoreCase))
                return endpointUri;

            return new Uri(endpointUri, "/v1/models");
        }

        private static IReadOnlyList<string> ParseModelNames(string json, EndpointProtocol protocol)
        {
            var models = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (protocol == EndpointProtocol.OllamaChat)
            {
                if (!root.TryGetProperty("models", out var ollamaModels) || ollamaModels.ValueKind != JsonValueKind.Array)
                    return models;

                foreach (var item in ollamaModels.EnumerateArray())
                {
                    AddModelCandidate(item, "model", seen, models);
                    AddModelCandidate(item, "name", seen, models);
                }

                return models;
            }

            if (root.TryGetProperty("data", out var openAiData) && openAiData.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in openAiData.EnumerateArray())
                    AddModelCandidate(item, "id", seen, models);

                return models;
            }

            if (!root.TryGetProperty("models", out var genericModels) || genericModels.ValueKind != JsonValueKind.Array)
                return models;

            foreach (var item in genericModels.EnumerateArray())
            {
                AddModelCandidate(item, "id", seen, models);
                AddModelCandidate(item, "model", seen, models);
                AddModelCandidate(item, "name", seen, models);
            }

            return models;
        }

        private static void AddModelCandidate(JsonElement item, string propertyName, HashSet<string> seen, List<string> models)
        {
            if (!item.TryGetProperty(propertyName, out var modelProperty) || modelProperty.ValueKind != JsonValueKind.String)
                return;

            var value = modelProperty.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalized = value.Trim();
            if (seen.Add(normalized))
                models.Add(normalized);
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool useStreaming, CancellationToken cancellationToken)
        {
            try
            {
                return useStreaming
                    ? await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false)
                    : await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("AI-request timed out.");
                throw new ChatServiceException(
                    ChatServiceErrorKind.Timeout,
                    "Forbindelsen til AI-serveren timed out. Prøv igen.",
                    ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("HTTP-fejl ved kald til AI endpoint.", ex);
                throw new ChatServiceException(
                    ChatServiceErrorKind.Connection,
                    "Kunne ikke oprette forbindelse til AI-serveren. Kontrollér internetforbindelse, firewall og endpoint.",
                    ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Uventet fejl ved kald til AI endpoint.", ex);
                throw new ChatServiceException(
                    ChatServiceErrorKind.Unknown,
                    "Der opstod en uventet fejl under kommunikation med AI-serveren.",
                    ex);
            }
        }

        private static ChatServiceException CreateHttpFailure(int statusCode, string body)
        {
            var trimmedBody = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
            var suffix = string.IsNullOrWhiteSpace(trimmedBody) ? string.Empty : $" Detaljer: {trimmedBody}";

            return statusCode switch
            {
                400 => new ChatServiceException(ChatServiceErrorKind.Configuration, "AI-serveren afviste forespørgslen som ugyldig." + suffix),
                401 or 403 => new ChatServiceException(ChatServiceErrorKind.Configuration, "Adgang blev afvist af AI-serveren. Kontrollér API key og endpoint." + suffix),
                404 => new ChatServiceException(ChatServiceErrorKind.Configuration, "AI endpoint blev ikke fundet. Kontrollér URL'en i Indstillinger." + suffix),
                405 => new ChatServiceException(ChatServiceErrorKind.Configuration, "AI endpoint afviste POST-kaldet. Kontrollér at URL'en peger på den rigtige chat-route." + suffix),
                408 => new ChatServiceException(ChatServiceErrorKind.Timeout, "AI-serveren brugte for lang tid på at svare." + suffix),
                429 => new ChatServiceException(ChatServiceErrorKind.Server, "AI-serveren afviser midlertidigt flere requests. Prøv igen om lidt." + suffix),
                >= 500 => new ChatServiceException(ChatServiceErrorKind.Server, "AI-serveren rapporterede en intern fejl." + suffix),
                _ => new ChatServiceException(ChatServiceErrorKind.Unknown, $"AI-serveren returnerede status {statusCode}." + suffix)
            };
        }
    }
}
