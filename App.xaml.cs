using System;
using System.IO;
using System.Windows;
using TrayApp.Services;
using TrayApp.ViewModels;
using TrayApp.Infrastructure;

namespace TrayApp
{
    public partial class App : Application
    {
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

                var db = new AppDatabase(Path.Combine(baseDir, "trayapp.db"));
                ISettingsService settingsService = new SettingsService(db);
                _themeManager = new ThemeManager(this);
                _themeManager.Initialize(settingsService.Settings.ThemeMode);
                IChatRepository chatRepo = new ChatRepository(db);
                IChatService chatService = new HttpChatService(settingsService, _logger);
                var placementService = new WindowPlacementService(Path.Combine(baseDir, "window.json"));
                _chatServiceDisposable = chatService as IDisposable;

                var main = new Views.MainWindow();
                MainWindow = main;

                var vm = new ViewModels.MainWindowViewModel(chatService, chatRepo, settingsService, _logger);
                _mainViewModel = vm;
                main.DataContext = vm;

                placementService.Restore(main);

                main.SizeChanged += (s, __) => placementService.Save((Window)s!);
                main.LocationChanged += (_, __) => placementService.Save(main);
                main.StateChanged += (_, __) => placementService.Save(main);

                _trayService = new TrayService(main);

                _trayService.NewChatRequested += () => main.Dispatcher.Invoke(() => vm.NewChatCommand.Execute(null));
                _trayService.SettingsRequested += () => main.Dispatcher.Invoke(() => ShowSettingsWindow(main, settingsService, _themeManager, _logger));
                _trayService.ShowRequested += () => main.Dispatcher.Invoke(() =>
                {
                    main.Show();
                    main.WindowState = WindowState.Normal;
                    main.Activate();
                });

                vm.SettingsRequested += () => main.Dispatcher.Invoke(() => ShowSettingsWindow(main, settingsService, _themeManager, _logger));
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
                _logger?.LogError("Fatal fejl under startup.", ex);
                MessageBox.Show(
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
                MessageBox.Show(
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

        private static void ShowSettingsWindow(Window owner, ISettingsService settingsService, IThemeManager themeManager, IAppLogger logger)
        {
            var window = new Views.SettingsWindow
            {
                Owner = owner
            };

            var vm = new SettingsViewModel(settingsService, themeManager, logger);
            vm.CloseRequested += saved =>
            {
                if (saved)
                {
                    if (owner.DataContext is MainWindowViewModel mainVm)
                        mainVm.RefreshConfigurationState();
                }

                window.Close();
            };
            window.DataContext = vm;
            window.ShowDialog();
        }
    }
}
