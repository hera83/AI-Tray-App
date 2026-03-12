using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;

namespace TrayApp.Infrastructure
{
    /// <summary>
    /// Persists the main window placement, but always restores startup position near the bottom-right.
    /// Saved size/state can still be reused when relevant, while startup ignores the last saved x/y.
    /// </summary>
    public class WindowPlacementService
    {
        private readonly string _path;

        private record PlacementRecord(double Left, double Top, double Width, double Height, string State);

        private const double MinDimension  = 200;
        private const double DefaultRightMargin = 20;
    private const double DefaultBottomMargin = 20;

        public WindowPlacementService(string path) => _path = path;

        public void Save(Window window)
        {
            if (window.WindowState == WindowState.Minimized) return; // never persist minimized

            var rec = new PlacementRecord(
                window.Left, window.Top,
                window.Width, window.Height,
                window.WindowState.ToString());

            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(rec,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best-effort */ }
        }

        public void Restore(Window window)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            PlacementRecord? rec = null;
            try
            {
                if (File.Exists(_path))
                    rec = JsonSerializer.Deserialize<PlacementRecord>(File.ReadAllText(_path));
            }
            catch { }

            if (rec != null)
            {
                var isFixedSizeWindow = window.ResizeMode == ResizeMode.NoResize;
                if (!isFixedSizeWindow && IsSizeValid(rec))
                {
                    window.Width  = rec.Width;
                    window.Height = rec.Height;
                }

                var restoredState = Enum.TryParse<WindowState>(rec.State, out var ws) ? ws : WindowState.Normal;
                if (isFixedSizeWindow && restoredState == WindowState.Maximized)
                    restoredState = WindowState.Normal;

                window.WindowState = restoredState;
            }

            // Always open in the bottom-right corner on startup instead of reusing the last saved x/y position.
            ApplyDefaultPlacement(window);
        }

        private static bool IsSizeValid(PlacementRecord r)
        {
            return r.Width >= MinDimension && r.Height >= MinDimension;
        }

        private static void ApplyDefaultPlacement(Window window)
        {
            if (double.IsNaN(window.Width) || window.Width < MinDimension)
                window.Width = MinDimension * 2;

            if (double.IsNaN(window.Height) || window.Height < MinDimension)
                window.Height = MinDimension * 2;

            var screen = Screen.FromPoint(Control.MousePosition);
            var workArea = screen.WorkingArea;
            var x = workArea.Right - window.Width - DefaultRightMargin;
            var y = workArea.Bottom - window.Height - DefaultBottomMargin;

            x = Math.Max(workArea.Left, Math.Min(x, workArea.Right - window.Width));
            y = Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - window.Height));

            window.Left = x;
            window.Top = y;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
        }
    }
}
