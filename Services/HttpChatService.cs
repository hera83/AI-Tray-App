using System;
using System.Collections.Generic;
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
            var s = _settings.Settings;
            if (string.IsNullOrWhiteSpace(s.AiEndpoint))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ikke konfigureret. Åbn Indstillinger og angiv en base URL.");

            if (!Uri.TryCreate(s.AiEndpoint, UriKind.Absolute, out var endpointUri))
                throw new ChatServiceException(
                    ChatServiceErrorKind.Configuration,
                    "AI endpoint er ugyldigt. Kontrollér URL'en i Indstillinger.");

            if (!NetworkInterface.GetIsNetworkAvailable())
                throw new ChatServiceException(
                    ChatServiceErrorKind.Connection,
                    "Ingen netværksforbindelse fundet. Kontrollér internetforbindelsen og prøv igen.");

            var req = new AiChatRequest
            {
                Model       = s.Model,
                Temperature = s.Temperature,
                Stream      = s.UseStreaming,
                Messages    = new List<AiMessage>()
            };

            // prepend system prompt if set
            if (!string.IsNullOrWhiteSpace(s.SystemPrompt))
                req.Messages.Add(new AiMessage { Role = "system", Content = s.SystemPrompt });

            req.Messages.Add(new AiMessage { Role = "user", Content = prompt });

            var json = JsonSerializer.Serialize(req, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri) { Content = content };

            if (!string.IsNullOrEmpty(s.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);

            _logger.LogInfo($"Sender AI-request til {endpointUri.Host} (streaming={s.UseStreaming}).");
            using var res = await SendAsync(request, s.UseStreaming, cancellationToken).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning($"AI endpoint returnerede {(int)res.StatusCode} {res.ReasonPhrase}.");
                throw CreateHttpFailure((int)res.StatusCode, body);
            }

            if (!s.UseStreaming)
            {
                var respJson = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
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
            }
            else
            {
                using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(stream);
                var buffer = new char[1024];
                var receivedAnyData = false;

                while (!cancellationToken.IsCancellationRequested)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (read > 0)
                    {
                        receivedAnyData = true;
                        var chunk = new string(buffer, 0, read);
                        yield return chunk;
                    }
                    else
                    {
                        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                    }
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
        }

        public void Dispose()
        {
            _http.Dispose();
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
                408 => new ChatServiceException(ChatServiceErrorKind.Timeout, "AI-serveren brugte for lang tid på at svare." + suffix),
                429 => new ChatServiceException(ChatServiceErrorKind.Server, "AI-serveren afviser midlertidigt flere requests. Prøv igen om lidt." + suffix),
                >= 500 => new ChatServiceException(ChatServiceErrorKind.Server, "AI-serveren rapporterede en intern fejl." + suffix),
                _ => new ChatServiceException(ChatServiceErrorKind.Unknown, $"AI-serveren returnerede status {statusCode}." + suffix)
            };
        }
    }
}
