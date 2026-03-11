using System;
using System.Collections.Generic;
using System.Windows.Input;
using TrayApp.Infrastructure;
using TrayApp.Services;

namespace TrayApp.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _service;
        private readonly IThemeManager _themeManager;
        private readonly IAppLogger _logger;

        // --- AI Gateway ---
        private string _aiEndpoint;
        public string AiEndpoint { get => _aiEndpoint; set => SetProperty(ref _aiEndpoint, value); }

        private string _apiKey = string.Empty;
        public string ApiKey { get => _apiKey; set => SetProperty(ref _apiKey, value); }

        private string _model;
        public string Model { get => _model; set => SetProperty(ref _model, value); }

        private string _systemPrompt;
        public string SystemPrompt { get => _systemPrompt; set => SetProperty(ref _systemPrompt, value); }

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

        /// <summary>Raised when the user confirms deleting all chat sessions.</summary>
        public event Action? AllChatsDeleteRequested;

        /// <summary>Raised when the window should close (save or cancel).</summary>
        public event Action<bool>? CloseRequested; // true = saved

        public SettingsViewModel(ISettingsService service, IThemeManager themeManager, IAppLogger logger)
        {
            _service = service;
            _themeManager = themeManager;
            _logger = logger;

            var s = service.Settings;
            _aiEndpoint            = s.AiEndpoint;
            ApiKey                 = s.ApiKey;       // plain copy for display; PasswordBox handles masking
            _model                 = s.Model;
            _systemPrompt          = s.SystemPrompt;
            _temperature           = s.Temperature;
            _useStreaming            = s.UseStreaming;
            _startMinimizedToTray   = s.StartMinimizedToTray;
            _launchOnWindowsStartup = StartupManager.IsEnabled(); // read live registry
            _selectedThemeMode      = s.ThemeMode;

            SaveCommand   = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(false));
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
        }

        private void Save()
        {
            try
            {
                _service.Apply(new AppSettings
                {
                    AiEndpoint              = AiEndpoint.Trim(),
                    ApiKey                  = ApiKey,
                    Model                   = Model.Trim(),
                    SystemPrompt            = SystemPrompt,
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
