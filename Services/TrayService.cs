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
        private DateTime _lastTrayInteractionUtc;
        private DateTime _lastToggleUtc;
        private DateTime _lastAutoHideUtc;

        public event Action? ShowRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;
        public event Action? MainHidden;

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
            _notifyIcon.MouseDown += OnNotifyIconMouseDown;
            _notifyIcon.MouseClick += OnNotifyIconMouseClick;

            // clicking a balloon tip brings the window to front
            _notifyIcon.BalloonTipClicked += (_, __) =>
            {
                ShowRequested?.Invoke();
                ShowMain();
            };

            _mainWindow.Closing += MainWindow_Closing;
            _mainWindow.Deactivated += MainWindow_Deactivated;
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
            HideMain();
        }

        private async void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            if (_isExitRequested || _disposed)
                return;

            await System.Threading.Tasks.Task.Delay(180);

            if (_isExitRequested || _disposed)
                return;

            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (!_mainWindow.IsVisible)
                    return;

                if (_mainWindow.IsActive)
                    return;

                if (HasVisibleOwnedWindow())
                    return;

                if (IsTrayInteractionRecent())
                    return;

                HideMain(markAsAutoHide: true);
            });
        }

        private void OnNotifyIconMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                _lastTrayInteractionUtc = DateTime.UtcNow;
        }

        private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var now = DateTime.UtcNow;
            if ((now - _lastAutoHideUtc) < TimeSpan.FromMilliseconds(200))
                return;

            if ((now - _lastToggleUtc) < TimeSpan.FromMilliseconds(300))
                return;

            _lastTrayInteractionUtc = now;
            _lastToggleUtc = now;
            ToggleMain();
        }

        private void ToggleMain()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                var isOpen = _mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized;
                if (isOpen)
                {
                    _mainWindow.WindowState = WindowState.Minimized;
                    HideMain();
                    return;
                }

                ShowRequested?.Invoke();
                ShowMain();
            });
        }

        private void HideMain(bool markAsAutoHide = false)
        {
            void HideAction()
            {
                if (!_mainWindow.IsVisible)
                    return;

                if (markAsAutoHide)
                    _lastAutoHideUtc = DateTime.UtcNow;

                _mainWindow.Hide();
                MainHidden?.Invoke();
            }

            if (_mainWindow.Dispatcher.CheckAccess())
            {
                HideAction();
                return;
            }

            _mainWindow.Dispatcher.Invoke(HideAction);
        }

        private bool IsTrayInteractionRecent()
        {
            return (DateTime.UtcNow - _lastTrayInteractionUtc) < TimeSpan.FromMilliseconds(600);
        }

        private bool HasVisibleOwnedWindow()
        {
            foreach (Window ownedWindow in _mainWindow.OwnedWindows)
            {
                if (ownedWindow.IsVisible)
                    return true;
            }

            return false;
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
            try
            {
                var targetSize = SystemInformation.SmallIconSize;
                return new System.Drawing.Icon(path, targetSize);
            }
            catch
            {
                try { return new System.Drawing.Icon(path); }
                catch { return null; }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _mainWindow.Closing -= MainWindow_Closing;
            _mainWindow.Deactivated -= MainWindow_Deactivated;
            _notifyIcon.MouseDown -= OnNotifyIconMouseDown;
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
