using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TrayApp.Infrastructure;
using TrayApp.Models;
using TrayApp.Services;
using ChatMessage = TrayApp.Models.Message;

namespace TrayApp.ViewModels
{
    public class MainWindowViewModel : ObservableObject, IDisposable
    {
        private const int AiThinkingDelayMs = 360;

        private readonly IChatService _chatService;
        private readonly IChatRepository _repo;
        private readonly ISettingsService _settings;
        private readonly IAppLogger _logger;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private bool _isInitialized;
        private bool _disposed;
        private bool _isSending;
        private bool _resetToFreshChatOnIdle;
        private bool _isSyncingSelectedSessionModel;
        private int _sendGate;
        private CancellationTokenSource? _generationCts;
        private readonly Dictionary<Guid, string> _sessionModelOverrides = new();
        private string _lastKnownDefaultModel = string.Empty;

        public ObservableCollection<ChatMessage> Messages { get; } = new();
        public ObservableCollection<ChatSession> ChatSessions { get; } = new();
        public ObservableCollection<string> AvailableModels { get; } = new();

        private bool _isLoadingModels;
        public bool IsLoadingModels
        {
            get => _isLoadingModels;
            private set
            {
                if (!SetProperty(ref _isLoadingModels, value))
                    return;

                OnPropertyChanged(nameof(CanSelectModel));
            }
        }

        private bool _isUserNearBottom = true;
        public bool IsUserNearBottom
        {
            get => _isUserNearBottom;
            set => SetProperty(ref _isUserNearBottom, value);
        }

        private bool _showNewMessages;
        public bool ShowNewMessages { get => _showNewMessages; set => SetProperty(ref _showNewMessages, value); }

        private int _scrollToBottomRequest;
        public int ScrollToBottomRequest { get => _scrollToBottomRequest; set => SetProperty(ref _scrollToBottomRequest, value); }

        private ChatSession? _activeSession;
        public ChatSession? ActiveSession
        {
            get => _activeSession;
            set
            {
                if (SetProperty(ref _activeSession, value))
                {
                    Messages.Clear();
                    if (_activeSession != null)
                    {
                        foreach (var message in _activeSession.Messages.OrderBy(message => message.CreatedAt))
                            Messages.Add(message);
                    }

                    ShowNewMessages = false;
                    ScrollToBottomRequest++;

                    NotifyEmptyStateChanged();
                    RaiseCommandStateChanged();
                    EnsureSelectedSessionModel();
                }
            }
        }

        private bool _isHistoryPaneExpanded;
        public bool IsHistoryPaneExpanded
        {
            get => _isHistoryPaneExpanded;
            private set
            {
                if (SetProperty(ref _isHistoryPaneExpanded, value))
                    OnPropertyChanged(nameof(HistoryPaneToggleText));
            }
        }

        public string HistoryPaneToggleText => IsHistoryPaneExpanded ? "Skjul historik" : "Historik";

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                {
                    EnsureDraftSessionForInput();
                    RaiseCommandStateChanged();
                }
            }
        }

        private string _selectedSessionModel = string.Empty;
        public string SelectedSessionModel
        {
            get => _selectedSessionModel;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value)
                    ? ResolveDefaultModel()
                    : value.Trim();

                normalized = NormalizeModelToAvailable(normalized);

                if (!SetProperty(ref _selectedSessionModel, normalized))
                    return;

                AddModelIfMissing(_selectedSessionModel);

                if (_activeSession == null || _isSyncingSelectedSessionModel)
                    return;

                var defaultModel = ResolveDefaultModel();
                if (string.Equals(_selectedSessionModel, defaultModel, StringComparison.OrdinalIgnoreCase))
                    _sessionModelOverrides.Remove(_activeSession.Id);
                else
                    _sessionModelOverrides[_activeSession.Id] = _selectedSessionModel;
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsInputEnabled));
                    RaiseCommandStateChanged();
                    TryApplyPendingResetForNextOpen();
                }
            }
        }

        private bool _isInitializing;
        public bool IsInitializing
        {
            get => _isInitializing;
            private set
            {
                if (SetProperty(ref _isInitializing, value))
                {
                    OnPropertyChanged(nameof(IsInputEnabled));
                    NotifyEmptyStateChanged();
                    RaiseCommandStateChanged();
                    TryApplyPendingResetForNextOpen();
                }
            }
        }

        private string? _errorMessage;
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged(nameof(HasError));
            }
        }

        private string? _errorHint;
        public string? ErrorHint { get => _errorHint; private set => SetProperty(ref _errorHint, value); }

        private ChatUiStatus _currentStatus = ChatUiStatus.Initializing;
        public ChatUiStatus CurrentStatus { get => _currentStatus; private set => SetProperty(ref _currentStatus, value); }

        private string _statusText = "Starter app";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private string _statusDetail = "Indlæser lokale data...";
        public string StatusDetail { get => _statusDetail; private set => SetProperty(ref _statusDetail, value); }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
        public bool IsInputEnabled => !IsLoading && !IsInitializing;
        public bool ShowEmptyState => !IsInitializing && Messages.Count == 0;
        public bool ShowConfigurationEmptyState => ShowEmptyState && !IsEndpointConfigured;
        public string EmptyStateTitle => IsEndpointConfigured
            ? "Start en samtale"
            : "Konfigurer AI-forbindelsen";
        public string EmptyStateDescription => IsEndpointConfigured
            ? "Skriv din besked nedenfor for at begynde."
            : "Åbn Indstillinger og angiv endpoint og eventuel API key for at bruge appen.";
        public bool IsEndpointConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.AiEndpoint);
        public bool CanSend => !IsLoading && !IsInitializing && IsEndpointConfigured && !string.IsNullOrWhiteSpace(InputText);
        public bool CanSelectModel => !IsInitializing && IsEndpointConfigured && AvailableModels.Count > 0 && !IsLoadingModels;

        public ICommand SendCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand NewChatCommand { get; }
        public ICommand GoToLatestCommand { get; }
        public ICommand StopGenerationCommand { get; }
        public ICommand ToggleHistoryPaneCommand { get; }
        public ICommand DeleteSessionCommand { get; }

        public event Action<string>? ResponseCompleted;
        public event Action? SettingsRequested;

        public MainWindowViewModel(IChatService chatService, IChatRepository repo, ISettingsService settings, IAppLogger logger)
        {
            _chatService = chatService;
            _repo = repo;
            _settings = settings;
            _logger = logger;
            Messages.CollectionChanged += (_, __) => NotifyEmptyStateChanged();
            AvailableModels.CollectionChanged += (_, __) => OnPropertyChanged(nameof(CanSelectModel));

            SeedAvailableModels();
            EnsureSelectedSessionModel();
            _lastKnownDefaultModel = ResolveDefaultModel();

            SendCommand = new RelayCommand(async _ => await SendAsync(), _ => !_isSending && CanSend);
            OpenSettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke());
            NewChatCommand = new RelayCommand(async _ => await NewChatAsync());
            StopGenerationCommand = new RelayCommand(_ => CancelGeneration(), _ => _generationCts != null || IsLoading);
            GoToLatestCommand = new RelayCommand(_ => GoToLatest());
            ToggleHistoryPaneCommand = new RelayCommand(_ => IsHistoryPaneExpanded = !IsHistoryPaneExpanded);
            DeleteSessionCommand = new RelayCommand(async p => { if (p is ChatSession s) await DeleteSessionAsync(s); });
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized || _disposed)
                return;

            _isInitialized = true;
            IsInitializing = true;
            SetStatus(ChatUiStatus.Initializing, "Starter app", "Indlæser lokale chats og indstillinger...");

            try
            {
                var loadedSessions = await _repo.LoadAllAsync() ?? Array.Empty<ChatSession>();
                var orderedSessions = loadedSessions
                    .OrderByDescending(session => session.UpdatedAt)
                    .ToList();
                await EnsureStartupDraftSessionAsync(orderedSessions);

                await RunOnUiThreadAsync(() =>
                {
                    ChatSessions.Clear();
                    foreach (var session in orderedSessions)
                        ChatSessions.Add(session);

                    ActiveSession = ChatSessions.FirstOrDefault();
                    NotifyEmptyStateChanged();
                });

                if (IsEndpointConfigured)
                    SetStatus(ChatUiStatus.Ready, "Klar", "Lokal data er indlæst. Du kan sende en besked.");
                else
                    SetStatus(ChatUiStatus.Error, "Konfiguration mangler", "Angiv AI endpoint i Indstillinger for at sende beskeder.");

                _ = RefreshAvailableModelsAsync();

                _logger.LogInfo("MainWindowViewModel initialiseret.");
            }
            catch (Exception ex)
            {
                var fallback = CreateSession("Hej! Jeg er din AI-assistent. Hvordan kan jeg hjælpe dig i dag?");

                await RunOnUiThreadAsync(() =>
                {
                    ChatSessions.Clear();
                    ChatSessions.Add(fallback);

                    ErrorMessage = $"Kunne ikke indlæse chats: {ex.Message}";
                    ErrorHint = "Appen startede med en midlertidig tom lokal session. Se logfilen for detaljer.";
                    ActiveSession = fallback;
                    NotifyEmptyStateChanged();
                });

                SetStatus(ChatUiStatus.Error, "Startup-fejl", "Appen kører videre med en midlertidig lokal session.");
                _logger.LogError("Fejl under initialisering af hovedvinduet.", ex);
            }
            finally
            {
                IsInitializing = false;
            }
        }

        public void RefreshConfigurationState()
        {
            var previousDefaultModel = _lastKnownDefaultModel;

            OnPropertyChanged(nameof(IsEndpointConfigured));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateDescription));
            OnPropertyChanged(nameof(ShowConfigurationEmptyState));
            OnPropertyChanged(nameof(CanSelectModel));

            SeedAvailableModels();

            var currentDefaultModel = ResolveDefaultModel();
            if (!string.Equals(previousDefaultModel, currentDefaultModel, StringComparison.OrdinalIgnoreCase))
            {
                _sessionModelOverrides.Clear();
                EnsureSelectedSessionModel();
                _logger.LogInfo($"Standardmodel ændret i indstillinger: '{previousDefaultModel}' -> '{currentDefaultModel}'. Session-model overrides nulstillet.");
            }

            _lastKnownDefaultModel = currentDefaultModel;
            _ = RefreshAvailableModelsAsync();

            if (IsLoading || IsInitializing)
                return;

            if (IsEndpointConfigured && (CurrentStatus == ChatUiStatus.Error || CurrentStatus == ChatUiStatus.Offline))
                SetStatus(ChatUiStatus.Ready, "Klar", "Konfiguration opdateret. Du kan sende en besked.");
            else if (!IsEndpointConfigured)
                SetStatus(ChatUiStatus.Error, "Konfiguration mangler", "Angiv AI endpoint i Indstillinger for at sende beskeder.");
        }

        public void ResetForNextOpen()
        {
            if (_disposed)
                return;

            if (IsLoading || IsInitializing)
            {
                _resetToFreshChatOnIdle = true;
                return;
            }

            _resetToFreshChatOnIdle = false;
            _ = NewChatAsync();
        }

        private async Task NewChatAsync()
        {
            ClearError();
            InputText = string.Empty;

            if (ActiveSession != null && IsSessionAtWelcomeStart(ActiveSession))
            {
                SetStatus(ChatUiStatus.Ready, "Klar", "Klar til ny samtale.");
                return;
            }

            var freshSession = CreateSessionWithWelcomeMessage();
            ChatSessions.Insert(0, freshSession);
            ActiveSession = freshSession;
            SetStatus(ChatUiStatus.Ready, "Klar", "Klar til ny samtale.");
            await SaveActiveSessionSafeAsync();
        }

        private void TryApplyPendingResetForNextOpen()
        {
            if (!_resetToFreshChatOnIdle || IsLoading || IsInitializing)
                return;

            _resetToFreshChatOnIdle = false;
            _ = NewChatAsync();
        }

        private void EnsureDraftSessionForInput()
        {
            if (_disposed || IsInitializing || IsLoading)
                return;

            if (ActiveSession != null)
                return;

            if (string.IsNullOrWhiteSpace(_inputText))
                return;

            var draftSession = CreateSessionWithWelcomeMessage();
            ChatSessions.Insert(0, draftSession);
            ActiveSession = draftSession;
            _ = SaveActiveSessionSafeAsync();
        }

        private async Task DeleteSessionAsync(ChatSession session)
        {
            try
            {
                await _repo.DeleteSessionAsync(session.Id);
                _sessionModelOverrides.Remove(session.Id);
                ChatSessions.Remove(session);

                if (ActiveSession == session)
                {
                    ActiveSession = ChatSessions.Count > 0 ? ChatSessions[0] : null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Kunne ikke slette samtale.", ex);
            }
        }

        public async Task DeleteAllSessionsAsync()
        {
            try
            {
                await _repo.DeleteAllAsync();
                _sessionModelOverrides.Clear();
                ChatSessions.Clear();
                ActiveSession = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Kunne ikke slette alle samtaler.", ex);
            }
        }

        private void GoToLatest()
        {
            ShowNewMessages = false;
            ScrollToBottomRequest++;
        }

        private void AddMessage(ChatMessage message)
        {
            if (ActiveSession == null)
                return;

            ActiveSession.Messages.Add(message);
            Messages.Add(message);
            NotifyEmptyStateChanged();
            ShowNewMessages = false;
            ScrollToBottomRequest++;
        }

        private async Task SendAsync()
        {
            if (_disposed || string.IsNullOrWhiteSpace(InputText) || IsInitializing)
                return;

            if (Interlocked.Exchange(ref _sendGate, 1) == 1)
                return;

            // Lazy session creation
            if (ActiveSession == null)
            {
                var freshSession = CreateSessionWithWelcomeMessage();
                ChatSessions.Insert(0, freshSession);
                ActiveSession = freshSession;
            }

            var prompt = InputText.Trim();
            var wasCancelled = false;
            var receivedAnyChunk = false;
            _isSending = true;
            IsLoading = true;
            ClearError();

            try
            {
                if (prompt.Length > 8000)
                {
                    ErrorMessage = "Beskeden er for lang.";
                    ErrorHint = "Forkort input og prøv igen. Lange prompts kan give langsomme eller afviste requests.";
                    SetStatus(ChatUiStatus.Error, "Input for langt", "Forkort beskeden og prøv igen.");
                    return;
                }

                EnsureSessionStartsWithWelcome(ActiveSession!);

                var user = new ChatMessage
                {
                    Role = MessageRole.User,
                    Content = prompt,
                    CreatedAt = DateTime.UtcNow
                };
                AddMessage(user);

                var assistant = new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    IsStreaming = true
                };
                AddMessage(assistant);

                InputText = string.Empty;
                SetStatus(ChatUiStatus.Sending, "Sender", "Sender besked til AI-serveren...");

                ResetGenerationCts();
                var token = _generationCts!.Token;

                try
                {
                    SetStatus(ChatUiStatus.Connecting, "Forbinder", "Opretter forbindelse til AI-serveren...");
                    SetStatus(ChatUiStatus.Receiving, "Tænker", "AI forbereder et svar...");
                    await Task.Delay(AiThinkingDelayMs, token);

                    var modelForSession = ResolveModelForSession(ActiveSession);
                    await foreach (var chunk in _chatService.StreamResponseAsync(prompt, token, modelForSession))
                    {
                        if (!receivedAnyChunk)
                        {
                            receivedAnyChunk = true;
                            SetStatus(ChatUiStatus.Receiving, "Modtager", "Svar modtages fra AI-serveren...");
                        }

                        await RunOnUiThreadAsync(() =>
                        {
                            assistant.Content += chunk;
                            ShowNewMessages = false;
                            ScrollToBottomRequest++;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    wasCancelled = true;
                    await RunOnUiThreadAsync(() => assistant.Content += "\n[Cancelled]");
                    SetStatus(ChatUiStatus.Ready, "Stoppet", "Generering blev stoppet.");
                    _logger.LogInfo("AI-generering blev stoppet af brugeren.");
                }
                catch (ChatServiceException ex)
                {
                    HandleChatServiceException(ex, assistant);
                }
                catch (Exception ex)
                {
                    ErrorMessage = "Der opstod en uventet fejl under hentning af svar.";
                    ErrorHint = "Prøv igen. Hvis problemet fortsætter, kontrollér logfilen og endpoint-konfigurationen.";
                    SetStatus(ChatUiStatus.Error, "Uventet fejl", ErrorMessage);
                    _logger.LogError("Uventet fejl under beskedsending.", ex);
                    await RunOnUiThreadAsync(() => assistant.Content = "[Error receiving response]");
                }
                finally
                {
                    await RunOnUiThreadAsync(() => assistant.IsStreaming = false);
                    DisposeGenerationCts();
                }

                if (!wasCancelled && !string.IsNullOrWhiteSpace(assistant.Content))
                {
                    ResponseCompleted?.Invoke(assistant.Content);
                    if (CurrentStatus != ChatUiStatus.Error && CurrentStatus != ChatUiStatus.Offline)
                        SetStatus(ChatUiStatus.Ready, "Klar", "Svar modtaget.");
                }

                await SaveActiveSessionSafeAsync();
            }
            finally
            {
                _isSending = false;
                IsLoading = false;
                Interlocked.Exchange(ref _sendGate, 0);
            }
        }

        private void EnsureSessionStartsWithWelcome(ChatSession session)
        {
            if (session.Messages.Count > 0)
                return;

            var welcomeMessage = CreateWelcomeMessage();
            AddMessage(welcomeMessage);
        }

        private ChatSession CreateEmptySession()
        {
            return new ChatSession
            {
                Title = "Chat " + DateTime.Now.ToLocalTime().ToString("g"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private ChatSession CreateSessionWithWelcomeMessage()
        {
            var session = CreateEmptySession();
            session.Messages.Add(CreateWelcomeMessage());
            return session;
        }

        private ChatMessage CreateWelcomeMessage()
        {
            return new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = BuildWelcomeMessage(),
                CreatedAt = DateTime.UtcNow,
                IsStreaming = false
            };
        }

        private async Task EnsureStartupDraftSessionAsync(System.Collections.Generic.List<ChatSession> sessions)
        {
            if (sessions.Any(IsSessionAtWelcomeStart))
                return;

            var emptySession = sessions.FirstOrDefault(session => session.Messages.Count == 0);
            if (emptySession != null)
            {
                emptySession.Messages.Add(CreateWelcomeMessage());
                emptySession.UpdatedAt = DateTime.UtcNow;
                sessions.Remove(emptySession);
                sessions.Insert(0, emptySession);

                try
                {
                    await _repo.SaveSessionAsync(emptySession);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Kunne ikke opdatere startsession med velkomstbesked.", ex);
                }

                return;
            }

            var draftSession = CreateSessionWithWelcomeMessage();
            sessions.Insert(0, draftSession);

            try
            {
                await _repo.SaveSessionAsync(draftSession);
            }
            catch (Exception ex)
            {
                _logger.LogError("Kunne ikke gemme ny startsession.", ex);
            }
        }

        private ChatSession CreateSession(string initialAssistantMessage)
        {
            var session = CreateEmptySession();

            session.Messages.Add(new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = initialAssistantMessage,
                CreatedAt = DateTime.UtcNow,
                IsStreaming = false
            });

            return session;
        }

        private async Task SaveActiveSessionSafeAsync()
        {
            if (ActiveSession == null)
                return;

            try
            {
                ActiveSession.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveSessionAsync(ActiveSession);
                MoveSessionToTop(ActiveSession);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Kunne ikke gemme chat lokalt: {ex.Message}";
                ErrorHint = "Dine seneste beskeder kan være synlige i UI men ikke gemt på disken endnu.";
                SetStatus(ChatUiStatus.Error, "Lokal gemning fejlede", "Kontrollér diskadgang og prøv igen.");
                _logger.LogError("Kunne ikke gemme aktiv session.", ex);
            }
        }

        private void HandleChatServiceException(ChatServiceException ex, ChatMessage assistant)
        {
            switch (ex.Kind)
            {
                case ChatServiceErrorKind.Configuration:
                    ErrorMessage = ex.Message;
                    ErrorHint = "Åbn Indstillinger og kontrollér endpoint, model og API key.";
                    SetStatus(ChatUiStatus.Error, "Konfigurationsfejl", ex.Message);
                    break;
                case ChatServiceErrorKind.Connection:
                    ErrorMessage = ex.Message;
                    ErrorHint = "Appen forbliver brugbar offline, men kan ikke hente nye AI-svar før forbindelsen virker igen.";
                    SetStatus(ChatUiStatus.Offline, "Offline", "Kan ikke nå AI-serveren lige nu.");
                    break;
                case ChatServiceErrorKind.Timeout:
                    ErrorMessage = ex.Message;
                    ErrorHint = "Serveren kan være langsom eller utilgængelig. Prøv igen om lidt.";
                    SetStatus(ChatUiStatus.Error, "Timeout", ex.Message);
                    break;
                case ChatServiceErrorKind.Server:
                    ErrorMessage = ex.Message;
                    ErrorHint = "Fejlen kommer fra server-siden. Prøv igen senere eller kontroller gatewayen.";
                    SetStatus(ChatUiStatus.Error, "Serverfejl", ex.Message);
                    break;
                default:
                    ErrorMessage = ex.Message;
                    ErrorHint = "Se logfilen for tekniske detaljer.";
                    SetStatus(ChatUiStatus.Error, "Fejl", ex.Message);
                    break;
            }

            _logger.LogError("ChatServiceException under beskedsending.", ex);
            assistant.Content = "[Kunne ikke hente svar]";
        }

        private void SetStatus(ChatUiStatus status, string text, string detail)
        {
            CurrentStatus = status;
            StatusText = text;
            StatusDetail = detail;
        }

        private bool IsSessionAtWelcomeStart(ChatSession session)
        {
            if (session.Messages.Count != 1)
                return false;

            return session.Messages[0].Role == MessageRole.Assistant;
        }

        private string BuildWelcomeMessage()
        {
            var preferredName = _settings.Settings.UserPreferredName?.Trim();
            var fullName = _settings.Settings.UserFullName?.Trim();
            var name = !string.IsNullOrWhiteSpace(preferredName)
                ? preferredName
                : fullName;

            return string.IsNullOrWhiteSpace(name)
                ? "Hej, hvad kan jeg hjælpe dig med i dag?"
                : $"Hej {name}, hvad kan jeg hjælpe dig med i dag?";
        }

        private void ClearError()
        {
            ErrorMessage = null;
            ErrorHint = null;
        }

        private void SeedAvailableModels()
        {
            var settings = _settings.Settings;

            AddModelIfMissing(settings.Model);
            foreach (var modelName in settings.CachedModels)
                AddModelIfMissing(modelName);
        }

        private void AddModelIfMissing(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            var normalized = modelName.Trim();
            if (AvailableModels.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                return;

            AvailableModels.Add(normalized);
        }

        private string ResolveDefaultModel()
        {
            var settingsModel = _settings.Settings.Model?.Trim();
            if (!string.IsNullOrWhiteSpace(settingsModel))
                return NormalizeModelToAvailable(settingsModel);

            if (AvailableModels.Count > 0)
                return AvailableModels[0];

            return "gpt-4o-mini";
        }

        private string NormalizeModelToAvailable(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return string.Empty;

            var normalized = modelName.Trim();
            var match = AvailableModels.FirstOrDefault(existing =>
                string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));

            return match ?? normalized;
        }

        private string ResolveModelForSession(ChatSession? session)
        {
            if (session == null)
                return ResolveDefaultModel();

            if (_sessionModelOverrides.TryGetValue(session.Id, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                var normalized = existing.Trim();
                if (AvailableModels.Count == 0 || AvailableModels.Any(model => string.Equals(model, normalized, StringComparison.OrdinalIgnoreCase)))
                    return NormalizeModelToAvailable(normalized);

                _logger.LogWarning($"Session-model '{normalized}' findes ikke længere i model-listen. Falder tilbage til standard-modellen.");
                _sessionModelOverrides.Remove(session.Id);
            }

            return ResolveDefaultModel();
        }

        private void EnsureSelectedSessionModel()
        {
            var model = ResolveModelForSession(_activeSession);

            if (string.IsNullOrWhiteSpace(model) && AvailableModels.Count > 0)
                model = AvailableModels[0];

            if (string.IsNullOrWhiteSpace(model))
                model = "gpt-4o-mini";

            AddModelIfMissing(model);
            _isSyncingSelectedSessionModel = true;
            try
            {
                SelectedSessionModel = model;
            }
            finally
            {
                _isSyncingSelectedSessionModel = false;
            }

            OnPropertyChanged(nameof(SelectedSessionModel));
            _lastKnownDefaultModel = ResolveDefaultModel();
            OnPropertyChanged(nameof(CanSelectModel));
        }

        private async Task RefreshAvailableModelsAsync()
        {
            if (_disposed || IsLoadingModels)
                return;

            if (!IsEndpointConfigured)
            {
                EnsureSelectedSessionModel();
                return;
            }

            IsLoadingModels = true;

            try
            {
                var models = await _chatService.GetAvailableModelsAsync(_lifetimeCts.Token).ConfigureAwait(false);
                await RunOnUiThreadAsync(() =>
                {
                    AvailableModels.Clear();
                    foreach (var modelName in models)
                        AddModelIfMissing(modelName);

                    AddModelIfMissing(_settings.Settings.Model);
                    EnsureSelectedSessionModel();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (ChatServiceException ex)
            {
                _logger.LogWarning($"Kunne ikke hente model-liste i hovedvinduet: {ex.Message}");
                await RunOnUiThreadAsync(EnsureSelectedSessionModel);
            }
            catch (Exception ex)
            {
                _logger.LogError("Uventet fejl under hentning af model-liste i hovedvinduet.", ex);
                await RunOnUiThreadAsync(EnsureSelectedSessionModel);
            }
            finally
            {
                IsLoadingModels = false;
            }
        }

        private void NotifyEmptyStateChanged()
        {
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowConfigurationEmptyState));
        }

        private void MoveSessionToTop(ChatSession session)
        {
            var currentIndex = ChatSessions.IndexOf(session);
            if (currentIndex > 0)
            {
                ChatSessions.Move(currentIndex, 0);
                return;
            }

            if (currentIndex < 0)
                ChatSessions.Insert(0, session);
        }

        private static Task RunOnUiThreadAsync(Action action)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
        }

        private void ResetGenerationCts()
        {
            DisposeGenerationCts();
            _generationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            RaiseCommandStateChanged();
        }

        private void CancelGeneration()
        {
            try
            {
                _generationCts?.Cancel();
            }
            catch
            {
                // best-effort cancellation only
            }
        }

        private void DisposeGenerationCts()
        {
            _generationCts?.Dispose();
            _generationCts = null;
            RaiseCommandStateChanged();
        }

        private void RaiseCommandStateChanged()
        {
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanSelectModel));
            OnPropertyChanged(nameof(IsInputEnabled));
            OnPropertyChanged(nameof(IsEndpointConfigured));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateDescription));
            CommandManager.InvalidateRequerySuggested();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelGeneration();
            DisposeGenerationCts();
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
        }
    }
}
