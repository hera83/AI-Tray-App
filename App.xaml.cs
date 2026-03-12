using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TrayApp.Services;
using TrayApp.ViewModels;
using TrayApp.Infrastructure;

namespace TrayApp
{
    public partial class App : System.Windows.Application
    {
        private static readonly string[] CriticalStartupResources =
        {
            "BaseTextBoxStyle",
            "PrimaryButtonStyle",
            "SecondaryButtonStyle",
            "ChatBaseTextBoxStyle",
            "RoundIconButtonStyle",
            "ChatConversationSurfaceStyle"
        };

        private TrayService? _trayService;
        private MainWindowViewModel? _mainViewModel;
        private IDisposable? _chatServiceDisposable;
        private IAppLogger? _logger;
        private IThemeManager? _themeManager;

        private async void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var baseDir = Path.Combine(appData, "TrayApp");
                Directory.CreateDirectory(baseDir);

                _logger = new FileAppLogger(Path.Combine(baseDir, "logs", "trayapp.log"));
                RegisterGlobalExceptionHandlers();
                _logger.LogInfo($"{AppInfo.ProductName} {AppInfo.Version} starter.");
                LogStartupDiagnostics("Startup.Begin");

                var db = new AppDatabase(Path.Combine(baseDir, "trayapp.db"));
                ISettingsService settingsService = new SettingsService(db);
                _themeManager = new ThemeManager(this);
                _logger.LogInfo($"Anvender theme-mode fra settings: {settingsService.Settings.ThemeMode}");
                _themeManager.Initialize(settingsService.Settings.ThemeMode);
                LogStartupDiagnostics("Startup.AfterThemeInitialize");
                IChatRepository chatRepo = new ChatRepository(db);
                IChatService chatService = new HttpChatService(settingsService, _logger);
                var placementService = new WindowPlacementService(Path.Combine(baseDir, "window.json"));
                _chatServiceDisposable = chatService as IDisposable;

                _ = SyncAvailableModelsOnStartupAsync(settingsService, chatService);

                var main = new Views.MainWindow();
                ApplyAppIcon(main);
                MainWindow = main;

                var vm = new ViewModels.MainWindowViewModel(chatService, chatRepo, settingsService, _logger);
                _mainViewModel = vm;
                main.DataContext = vm;
                LogStartupDiagnostics("Startup.AfterMainWindowConstruct");

                placementService.Restore(main);

                main.SizeChanged += (s, __) => placementService.Save((Window)s!);
                main.LocationChanged += (_, __) => placementService.Save(main);
                main.StateChanged += (_, __) => placementService.Save(main);

                _trayService = new TrayService(main);
                _trayService.MainHidden += () => main.Dispatcher.Invoke(() => vm.ResetForNextOpen());
                _trayService.SettingsRequested += () => main.Dispatcher.Invoke(() => ShowSettingsWindow(main, settingsService, chatService, _themeManager, _logger));
                _trayService.ShowRequested += () => main.Dispatcher.Invoke(() => main.ActivateAndFocusInput());

                vm.SettingsRequested += () => main.Dispatcher.Invoke(() => ShowSettingsWindow(main, settingsService, chatService, _themeManager, _logger));
                vm.ResponseCompleted += responseText =>
                {
                    main.Dispatcher.Invoke(() =>
                    {
                        bool windowHidden = !main.IsVisible;
                        bool windowInactive = !main.IsActive;

                        if (windowHidden || windowInactive)
                            _trayService?.Show(AppInfo.ProductName, responseText);
                    });
                };

                Exit += (_, __) =>
                {
                    _logger?.LogInfo($"{AppInfo.ProductName} lukker ned.");
                    _mainViewModel?.Dispose();
                    _chatServiceDisposable?.Dispose();
                    _trayService?.Dispose();
                };

                await vm.InitializeAsync();

                if (settingsService.Settings.StartMinimizedToTray)
                {
                    main.ShowActivated = false;
                    main.Show();
                    main.Hide();
                    main.ShowActivated = true;
                }
                else
                {
                    main.Show();
                }

                _logger.LogInfo("Startup-sekvens gennemført.");
            }
            catch (Exception ex)
            {
                LogStartupDiagnostics("Startup.FatalCatch");
                _logger?.LogError("Fatal fejl under startup.", ex);
                System.Windows.MessageBox.Show(
                    "Appen kunne ikke starte korrekt. Se logfilen i AppData/TrayApp/logs for detaljer.",
                    AppInfo.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private void RegisterGlobalExceptionHandlers()
        {
            DispatcherUnhandledException += (_, args) =>
            {
                _logger?.LogError("Ubehandlet UI-fejl.", args.Exception);
                args.Handled = true;
                System.Windows.MessageBox.Show(
                    "Der opstod en uventet fejl i brugerfladen. Fejlen er logget lokalt.",
                    AppInfo.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                _logger?.LogError("Ubehandlet AppDomain-fejl.", args.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                _logger?.LogError("Uobserveret task-fejl.", args.Exception);
                args.SetObserved();
            };
        }

        private void LogStartupDiagnostics(string stage)
        {
            if (_logger == null)
                return;

            try
            {
                _logger.LogInfo($"[StartupDiagnostics] Stage={stage}");
                _logger.LogInfo($"[StartupDiagnostics] Theme={_themeManager?.CurrentTheme.ToString() ?? "<ikke-initialiseret>"}");
                _logger.LogInfo($"[StartupDiagnostics] AppResources: direct={Resources.Count}, merged={Resources.MergedDictionaries.Count}");

                for (var index = 0; index < Resources.MergedDictionaries.Count; index++)
                {
                    var dictionary = Resources.MergedDictionaries[index];
                    var source = dictionary.Source?.OriginalString ?? "<inline>";
                    _logger.LogInfo($"[StartupDiagnostics] Merged[{index}] Source={source}, Keys={dictionary.Count}");
                }

                foreach (var resourceKey in CriticalStartupResources)
                {
                    var resourceValue = TryFindResource(resourceKey);
                    var status = resourceValue == null
                        ? "MISSING"
                        : $"FOUND ({resourceValue.GetType().Name})";

                    _logger.LogInfo($"[StartupDiagnostics] Resource '{resourceKey}': {status}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[StartupDiagnostics] Fejl under diagnostics i stage '{stage}': {ex.Message}");
            }
        }

        private static void ShowSettingsWindow(Window owner, ISettingsService settingsService, IChatService chatService, IThemeManager themeManager, IAppLogger logger)
        {
            var window = new Views.SettingsWindow
            {
                Owner = owner,
                Icon = owner.Icon
            };

            if (window.Icon == null)
                ApplyAppIcon(window);

            var vm = new SettingsViewModel(settingsService, chatService, themeManager, logger);
            vm.CloseRequested += saved =>
            {
                if (saved)
                {
                    if (owner.DataContext is MainWindowViewModel mainVm)
                        mainVm.RefreshConfigurationState();
                }

                window.Close();
            };
            vm.AllChatsDeleteRequested += async () =>
            {
                if (owner.DataContext is MainWindowViewModel mainVm)
                    await mainVm.DeleteAllSessionsAsync();
            };
            window.DataContext = vm;
            window.ShowDialog();
        }

        private static void ApplyAppIcon(Window window)
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!File.Exists(iconPath))
                return;

            try
            {
                window.Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
            catch
            {
                // best-effort icon assignment
            }
        }

        private async Task SyncAvailableModelsOnStartupAsync(ISettingsService settingsService, IChatService chatService)
        {
            try
            {
                var settings = settingsService.Settings;
                if (string.IsNullOrWhiteSpace(settings.AiEndpoint) || string.IsNullOrWhiteSpace(settings.ApiKey))
                    return;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var models = await chatService.GetAvailableModelsAsync(cts.Token).ConfigureAwait(false);
                var normalizedModels = models
                    .Where(modelName => !string.IsNullOrWhiteSpace(modelName))
                    .Select(modelName => modelName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (normalizedModels.Length == 0)
                    return;

                settings.CachedModels = normalizedModels;

                if (string.IsNullOrWhiteSpace(settings.Model) ||
                    !normalizedModels.Any(modelName => string.Equals(modelName, settings.Model, StringComparison.OrdinalIgnoreCase)))
                {
                    settings.Model = normalizedModels[0];
                }

                settingsService.Save();
                _logger?.LogInfo($"Model-liste opdateret ved startup ({normalizedModels.Length} modeller). Valgt model: {settings.Model}.");
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("Model-liste opdatering ved startup timed out.");
            }
            catch (ChatServiceException ex)
            {
                _logger?.LogWarning($"Kunne ikke opdatere model-liste ved startup: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger?.LogError("Uventet fejl under model-liste opdatering ved startup.", ex);
            }
        }
    }
}
