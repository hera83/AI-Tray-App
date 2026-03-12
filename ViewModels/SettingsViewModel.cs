using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TrayApp.Infrastructure;
using TrayApp.Services;

namespace TrayApp.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _service;
        private readonly IChatService _chatService;
        private readonly IThemeManager _themeManager;
        private readonly IAppLogger _logger;

        // --- AI Gateway ---
        private string _aiEndpoint;
        public string AiEndpoint { get => _aiEndpoint; set => SetProperty(ref _aiEndpoint, value); }

        private string _apiKey = string.Empty;
        public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }

        private string _model;
        public string Model
        {
            get => _model;
            set
            {
                if (!SetProperty(ref _model, value))
                    return;

                EnsureCurrentModelInList();
            }
        }

        private string _systemPrompt;
        public string SystemPrompt { get => _systemPrompt; set => SetProperty(ref _systemPrompt, value); }

        private string _userFullName = string.Empty;
        public string UserFullName { get => _userFullName; set => SetProperty(ref _userFullName, value); }

        private string _userPreferredName = string.Empty;
        public string UserPreferredName { get => _userPreferredName; set => SetProperty(ref _userPreferredName, value); }

        private string _userOccupation = string.Empty;
        public string UserOccupation { get => _userOccupation; set => SetProperty(ref _userOccupation, value); }

        private string _userInterests = string.Empty;
        public string UserInterests { get => _userInterests; set => SetProperty(ref _userInterests, value); }

        private string _userResponseStyle = string.Empty;
        public string UserResponseStyle { get => _userResponseStyle; set => SetProperty(ref _userResponseStyle, value); }

        private string _userAdditionalContext = string.Empty;
        public string UserAdditionalContext { get => _userAdditionalContext; set => SetProperty(ref _userAdditionalContext, value); }

        private double _temperature;
        public double Temperature
        {
            get => _temperature;
            set => SetProperty(ref _temperature, Math.Clamp(value, 0.0, 2.0));
        }

        private bool _useStreaming;
        public bool UseStreaming { get => _useStreaming; set => SetProperty(ref _useStreaming, value); }

        // --- App Behaviour ---
        private bool _startMinimizedToTray;
        public bool StartMinimizedToTray { get => _startMinimizedToTray; set => SetProperty(ref _startMinimizedToTray, value); }

        private bool _launchOnWindowsStartup;
        public bool LaunchOnWindowsStartup
        {
            get => _launchOnWindowsStartup;
            set => SetProperty(ref _launchOnWindowsStartup, value);
        }

        private ThemeMode _selectedThemeMode;
        public ThemeMode SelectedThemeMode
        {
            get => _selectedThemeMode;
            set => SetProperty(ref _selectedThemeMode, value);
        }

        public IReadOnlyList<ThemeMode> AvailableThemeModes { get; } = new[]
        {
            ThemeMode.Dark,
            ThemeMode.Light
        };

        public ObservableCollection<string> AvailableResponseStyles { get; } = new()
        {
            string.Empty,
            "Kort og konkret",
            "Kort i punktform",
            "Trin-for-trin",
            "Teknisk detaljeret",
            "Enkelt og ikke-teknisk",
            "Med eksempler",
            "Med kode når relevant"
        };

        public ObservableCollection<string> AvailableModels { get; } = new();

        private bool _isLoadingModels;
        public bool IsLoadingModels
        {
            get => _isLoadingModels;
            private set
            {
                if (!SetProperty(ref _isLoadingModels, value))
                    return;

                CommandManager.InvalidateRequerySuggested();
            }
        }

        // --- Status ---
        private string? _statusMessage;
        public string? StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isStatusError;
        public bool IsStatusError { get => _isStatusError; private set => SetProperty(ref _isStatusError, value); }

        public string AppVersion => AppInfo.Version;
        public string LogFilePath => _logger.LogFilePath;

        // --- Commands ---
        public ICommand SaveCommand   { get; }
        public ICommand CancelCommand { get; }
        public ICommand DeleteAllChatsCommand { get; }
        public ICommand RefreshModelsCommand { get; }

        /// <summary>Raised when the user confirms deleting all chat sessions.</summary>
        public event Action? AllChatsDeleteRequested;

        /// <summary>Raised when the window should close (save or cancel).</summary>
        public event Action<bool>? CloseRequested; // true = saved

        public SettingsViewModel(ISettingsService service, IChatService chatService, IThemeManager themeManager, IAppLogger logger)
        {
            _service = service;
            _chatService = chatService;
            _themeManager = themeManager;
            _logger = logger;

            var s = service.Settings;
            _aiEndpoint            = s.AiEndpoint;
            ApiKey                 = s.ApiKey;       // plain copy for display; PasswordBox handles masking
            _model                 = s.Model;
            _systemPrompt          = s.SystemPrompt;
            _userFullName          = s.UserFullName;
            _userPreferredName     = s.UserPreferredName;
            _userOccupation        = s.UserOccupation;
            _userInterests         = s.UserInterests;
            _userResponseStyle     = s.UserResponseStyle;
            _userAdditionalContext = s.UserAdditionalContext;
            _temperature           = s.Temperature;
            _useStreaming            = s.UseStreaming;
            _startMinimizedToTray   = s.StartMinimizedToTray;
            _launchOnWindowsStartup = StartupManager.IsEnabled(); // read live registry
            _selectedThemeMode      = s.ThemeMode;

            foreach (var modelName in s.CachedModels)
                AddModelIfMissing(modelName);

            AddResponseStyleIfMissing(_userResponseStyle);

            SaveCommand   = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(false));
            RefreshModelsCommand = new RelayCommand(_ => _ = RefreshModelsAsync(), _ => !IsLoadingModels);
            DeleteAllChatsCommand = new RelayCommand(_ =>
            {
                var result = System.Windows.MessageBox.Show(
                    "Er du sikker på, at du vil slette alle samtaler?\nDette kan ikke fortrydes.",
                    "Slet alle samtaler",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                    AllChatsDeleteRequested?.Invoke();
            });

            EnsureCurrentModelInList();
            _ = RefreshModelsAsync();
        }

        private async Task RefreshModelsAsync()
        {
            if (IsLoadingModels)
                return;

            if (string.IsNullOrWhiteSpace(AiEndpoint))
            {
                EnsureCurrentModelInList();
                IsStatusError = false;
                StatusMessage = "Angiv Gateway URL og API key for at hente modeller.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                EnsureCurrentModelInList();
                IsStatusError = false;
                StatusMessage = "Angiv Gateway URL og API key for at hente modeller.";
                return;
            }

            IsLoadingModels = true;

            try
            {
                var models = await _chatService.GetAvailableModelsAsync(CancellationToken.None);
                var preferredModel = string.IsNullOrWhiteSpace(Model)
                    ? _service.Settings.Model
                    : Model;

                AvailableModels.Clear();
                foreach (var modelName in models)
                    AddModelIfMissing(modelName);

                if (!TryRestoreModelSelection(preferredModel))
                {
                    if (!string.IsNullOrWhiteSpace(preferredModel))
                        Model = preferredModel.Trim();

                    EnsureCurrentModelInList();
                }

                if (string.IsNullOrWhiteSpace(Model) && AvailableModels.Count > 0)
                    Model = AvailableModels[0];

                IsStatusError = false;
                StatusMessage = $"Hentede {AvailableModels.Count} modeller fra gatewayen.";
            }
            catch (ChatServiceException ex)
            {
                EnsureCurrentModelInList();
                IsStatusError = true;
                StatusMessage = $"Kunne ikke hente modeller: {ex.Message}";
                _logger.LogWarning($"Model-liste kunne ikke hentes: {ex.Message}");
            }
            catch (Exception ex)
            {
                EnsureCurrentModelInList();
                IsStatusError = true;
                StatusMessage = "Uventet fejl under hentning af modeller.";
                _logger.LogError("Uventet fejl under hentning af model-liste.", ex);
            }
            finally
            {
                IsLoadingModels = false;
            }
        }

        private void AddModelIfMissing(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            var normalized = modelName.Trim();
            foreach (var existing in AvailableModels)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AvailableModels.Add(normalized);
        }

        private void AddResponseStyleIfMissing(string? style)
        {
            if (string.IsNullOrWhiteSpace(style))
                return;

            var normalized = style.Trim();
            foreach (var existing in AvailableResponseStyles)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AvailableResponseStyles.Add(normalized);
        }

        private bool TryRestoreModelSelection(string? preferredModel)
        {
            if (string.IsNullOrWhiteSpace(preferredModel))
                return false;

            var match = AvailableModels.FirstOrDefault(modelName =>
                string.Equals(modelName, preferredModel, StringComparison.OrdinalIgnoreCase));

            if (match == null)
                return false;

            Model = match;
            return true;
        }

        private void EnsureCurrentModelInList()
        {
            if (string.IsNullOrWhiteSpace(Model))
                return;

            foreach (var modelName in AvailableModels)
            {
                if (string.Equals(modelName, Model, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AvailableModels.Insert(0, Model);
        }

        private void Save()
        {
            try
            {
                _service.Apply(new AppSettings
                {
                    AiEndpoint              = AiEndpoint.Trim(),
                    ApiKey                  = ApiKey,
                    Model                   = string.IsNullOrWhiteSpace(Model) ? string.Empty : Model.Trim(),
                    SystemPrompt            = SystemPrompt,
                    UserProfile             = UserAdditionalContext.Trim(),
                    UserFullName            = UserFullName.Trim(),
                    UserPreferredName       = UserPreferredName.Trim(),
                    UserOccupation          = UserOccupation.Trim(),
                    UserInterests           = UserInterests.Trim(),
                    UserResponseStyle       = UserResponseStyle.Trim(),
                    UserAdditionalContext   = UserAdditionalContext.Trim(),
                    CachedModels            = AvailableModels
                        .Where(modelName => !string.IsNullOrWhiteSpace(modelName))
                        .Select(modelName => modelName.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Temperature             = Temperature,
                    UseStreaming            = UseStreaming,
                    StartMinimizedToTray    = StartMinimizedToTray,
                    LaunchOnWindowsStartup  = LaunchOnWindowsStartup,
                    ThemeMode               = SelectedThemeMode
                });

                _themeManager.ApplyTheme(SelectedThemeMode);

                StartupManager.Sync(LaunchOnWindowsStartup);

                IsStatusError = false;
                StatusMessage = "Indstillinger gemt.";
                _logger.LogInfo("Indstillinger gemt fra SettingsViewModel.");
                CloseRequested?.Invoke(true);
            }
            catch (Exception ex)
            {
                IsStatusError = true;
                StatusMessage = $"Kunne ikke gemme indstillinger: {ex.Message}";
                _logger.LogError("Kunne ikke gemme indstillinger.", ex);
            }
        }
    }
}
