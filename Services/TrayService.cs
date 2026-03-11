using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace TrayApp.Services
{
    public class TrayService : INotificationService, IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Window _mainWindow;
        private bool _isExitRequested;
        private bool _disposed;

        public event Action? ShowRequested;
        public event Action? NewChatRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;

        public TrayService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "Tray AI Chat";

            // load custom icon; fall back to built-in application icon
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            _notifyIcon.Icon = File.Exists(iconPath)
                ? TryLoadIcon(iconPath) ?? SystemIcons.Application
                : SystemIcons.Application;

            var menu = new ContextMenuStrip();

            var show = new ToolStripMenuItem("Åbn");
            show.Click += (_, __) => { ShowRequested?.Invoke(); ShowMain(); };
            menu.Items.Add(show);

            var newChat = new ToolStripMenuItem("Ny chat");
            newChat.Click += (_, __) => NewChatRequested?.Invoke();
            menu.Items.Add(newChat);

            var settings = new ToolStripMenuItem("Indstillinger");
            settings.Click += (_, __) => SettingsRequested?.Invoke();
            menu.Items.Add(settings);

            menu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Afslut");
            exit.Click += (_, __) =>
            {
                _isExitRequested = true;
                ExitRequested?.Invoke();
                System.Windows.Application.Current.Shutdown();
            };
            menu.Items.Add(exit);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (_, __) => ShowMain();

            // clicking a balloon tip brings the window to front
            _notifyIcon.BalloonTipClicked += (_, __) =>
            {
                ShowRequested?.Invoke();
                ShowMain();
            };

            _mainWindow.Closing += MainWindow_Closing;
            System.Windows.Application.Current.Exit += (_, __) => Dispose();
        }

        // INotificationService
        public void Show(string title, string body)
        {
            _notifyIcon.Visible = true;
            _notifyIcon.ShowBalloonTip(
                timeout: 4000,
                tipTitle: title,
                tipText: body.Length > 200 ? body[..200] + "…" : body,
                tipIcon: ToolTipIcon.None);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isExitRequested) return;
            e.Cancel = true;
            _mainWindow.Dispatcher.Invoke(() => _mainWindow.Hide());
        }

        private void ShowMain()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            });
        }

        private static System.Drawing.Icon? TryLoadIcon(string path)
        {
            try { return new System.Drawing.Icon(path); }
            catch { return null; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
