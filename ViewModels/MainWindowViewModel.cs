using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TrayApp.Infrastructure;
using TrayApp.Models;
using TrayApp.Services;

namespace TrayApp.ViewModels
{
    public class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly IChatService _chatService;
        private readonly IChatRepository _repo;
        private readonly ISettingsService _settings;
        private readonly IAppLogger _logger;
        private readonly CancellationTokenSource _lifetimeCts = new();
        private bool _isInitialized;
        private bool _disposed;
        private bool _isSending;
        private int _sendGate;
        private CancellationTokenSource? _generationCts;

        public ObservableCollection<Message> Messages { get; } = new();

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
            private set
            {
                if (SetProperty(ref _activeSession, value))
                {
                    Messages.Clear();
                    if (_activeSession != null)
                    {
                        foreach (var message in _activeSession.Messages)
                            Messages.Add(message);
                    }

                    OnPropertyChanged(nameof(ShowEmptyState));
                    RaiseCommandStateChanged();
                }
            }
        }

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                    RaiseCommandStateChanged();
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
                    OnPropertyChanged(nameof(ShowEmptyState));
                    RaiseCommandStateChanged();
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
        public string EmptyStateTitle => IsEndpointConfigured
            ? "Start en samtale"
            : "Konfigurer AI-forbindelsen";
        public string EmptyStateDescription => IsEndpointConfigured
            ? "Skriv din besked nedenfor for at begynde."
            : "Åbn Indstillinger og angiv endpoint og eventuel API key for at bruge appen.";
        public bool IsEndpointConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.AiEndpoint);
        public bool CanSend => !IsLoading && !IsInitializing && ActiveSession != null && IsEndpointConfigured && !string.IsNullOrWhiteSpace(InputText);

        public ICommand SendCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand NewChatCommand { get; }
        public ICommand GoToLatestCommand { get; }
        public ICommand StopGenerationCommand { get; }

        public event Action<string>? ResponseCompleted;
        public event Action? SettingsRequested;

        public MainWindowViewModel(IChatService chatService, IChatRepository repo, ISettingsService settings, IAppLogger logger)
        {
            _chatService = chatService;
            _repo = repo;
            _settings = settings;
            _logger = logger;
            Messages.CollectionChanged += (_, __) => OnPropertyChanged(nameof(ShowEmptyState));

            SendCommand = new RelayCommand(async _ => await SendAsync(), _ => !_isSending && CanSend);
            OpenSettingsCommand = new RelayCommand(_ => SettingsRequested?.Invoke());
            NewChatCommand = new RelayCommand(async _ => await NewChatAsync());
            StopGenerationCommand = new RelayCommand(_ => CancelGeneration(), _ => _generationCts != null || IsLoading);
            GoToLatestCommand = new RelayCommand(_ => GoToLatest());
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
                var latest = await _repo.LoadLatestAsync();
                var session = latest ?? CreateSession("Hej! Jeg er din AI-assistent. Hvordan kan jeg hjælpe dig i dag?");

                if (latest == null)
                    await _repo.SaveSessionAsync(session);

                await RunOnUiThreadAsync(() =>
                {
                    ActiveSession = session;
                    OnPropertyChanged(nameof(ShowEmptyState));
                });

                if (IsEndpointConfigured)
                    SetStatus(ChatUiStatus.Ready, "Klar", "Lokal data er indlæst. Du kan sende en besked.");
                else
                    SetStatus(ChatUiStatus.Error, "Konfiguration mangler", "Angiv AI endpoint i Indstillinger for at sende beskeder.");

                _logger.LogInfo("MainWindowViewModel initialiseret.");
            }
            catch (Exception ex)
            {
                var fallback = CreateSession("Hej! Jeg er din AI-assistent. Hvordan kan jeg hjælpe dig i dag?");

                await RunOnUiThreadAsync(() =>
                {
                    ErrorMessage = $"Kunne ikke indlæse chats: {ex.Message}";
                    ErrorHint = "Appen startede med en midlertidig tom lokal session. Se logfilen for detaljer.";
                    ActiveSession = fallback;
                    OnPropertyChanged(nameof(ShowEmptyState));
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
            OnPropertyChanged(nameof(IsEndpointConfigured));
            OnPropertyChanged(nameof(EmptyStateTitle));
            OnPropertyChanged(nameof(EmptyStateDescription));

            if (IsLoading || IsInitializing)
                return;

            if (IsEndpointConfigured && (CurrentStatus == ChatUiStatus.Error || CurrentStatus == ChatUiStatus.Offline))
                SetStatus(ChatUiStatus.Ready, "Klar", "Konfiguration opdateret. Du kan sende en besked.");
            else if (!IsEndpointConfigured)
                SetStatus(ChatUiStatus.Error, "Konfiguration mangler", "Angiv AI endpoint i Indstillinger for at sende beskeder.");
        }

        private async Task NewChatAsync()
        {
            ClearError();
            var session = CreateSession("Ny chat startet.");
            ActiveSession = session;
            await SaveActiveSessionSafeAsync();
            SetStatus(ChatUiStatus.Ready, "Klar", "Ny chat er oprettet.");
        }

        private void GoToLatest()
        {
            ShowNewMessages = false;
            ScrollToBottomRequest++;
        }

        private void AddMessage(Message message)
        {
            if (ActiveSession == null)
                return;

            ActiveSession.Messages.Add(message);
            Messages.Add(message);
            OnPropertyChanged(nameof(ShowEmptyState));

            if (IsUserNearBottom)
            {
                ShowNewMessages = false;
                ScrollToBottomRequest++;
            }
            else
            {
                ShowNewMessages = true;
            }
        }

        private async Task SendAsync()
        {
            if (_disposed || string.IsNullOrWhiteSpace(InputText) || ActiveSession == null || IsInitializing)
                return;

            if (Interlocked.Exchange(ref _sendGate, 1) == 1)
                return;

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

                var user = new Message
                {
                    Role = MessageRole.User,
                    Content = prompt,
                    CreatedAt = DateTime.UtcNow
                };
                AddMessage(user);

                var assistant = new Message
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
                    await foreach (var chunk in _chatService.StreamResponseAsync(prompt, token))
                    {
                        if (!receivedAnyChunk)
                        {
                            receivedAnyChunk = true;
                            SetStatus(ChatUiStatus.Receiving, "Modtager", "Svar modtages fra AI-serveren...");
                        }

                        await RunOnUiThreadAsync(() => assistant.Content += chunk);
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

        private ChatSession CreateSession(string initialAssistantMessage)
        {
            var session = new ChatSession
            {
                Title = "Chat " + DateTime.Now.ToLocalTime().ToString("g"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            session.Messages.Add(new Message
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
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Kunne ikke gemme chat lokalt: {ex.Message}";
                ErrorHint = "Dine seneste beskeder kan være synlige i UI men ikke gemt på disken endnu.";
                SetStatus(ChatUiStatus.Error, "Lokal gemning fejlede", "Kontrollér diskadgang og prøv igen.");
                _logger.LogError("Kunne ikke gemme aktiv session.", ex);
            }
        }

        private void HandleChatServiceException(ChatServiceException ex, Message assistant)
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

        private void ClearError()
        {
            ErrorMessage = null;
            ErrorHint = null;
        }

        private static Task RunOnUiThreadAsync(Action action)
        {
            var dispatcher = Application.Current.Dispatcher;
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
