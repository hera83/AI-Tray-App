using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace TrayApp.Infrastructure
{
    /// <summary>
    /// Persists and restores the main window's position, size and maximized state.
    /// Falls back to a centered default if the saved placement is invalid or off-screen.
    /// </summary>
    public class WindowPlacementService
    {
        private readonly string _path;

        private record PlacementRecord(double Left, double Top, double Width, double Height, string State);

        private const double DefaultWidth  = 500;
        private const double DefaultHeight = 720;
        private const double MinDimension  = 200;

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
            PlacementRecord? rec = null;
            try
            {
                if (File.Exists(_path))
                    rec = JsonSerializer.Deserialize<PlacementRecord>(File.ReadAllText(_path));
            }
            catch { }

            if (rec != null && IsPlacementValid(rec))
            {
                window.Left        = rec.Left;
                window.Top         = rec.Top;
                window.Width       = rec.Width;
                window.Height      = rec.Height;
                window.WindowState = Enum.TryParse<WindowState>(rec.State, out var ws) ? ws : WindowState.Normal;
            }
            else
            {
                ApplyDefaultPlacement(window);
            }
        }

        private static bool IsPlacementValid(PlacementRecord r)
        {
            if (r.Width < MinDimension || r.Height < MinDimension) return false;

            // virtual screen spans all monitors
            double vsLeft   = SystemParameters.VirtualScreenLeft;
            double vsTop    = SystemParameters.VirtualScreenTop;
            double vsRight  = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop  + SystemParameters.VirtualScreenHeight;

            // require at least part of the title bar to be visible (40px strip)
            const double titleBarStrip = 40;
            bool leftOk   = r.Left + r.Width > vsLeft + titleBarStrip;
            bool topOk    = r.Top >= vsTop;
            bool rightOk  = r.Left < vsRight - titleBarStrip;
            bool bottomOk = r.Top  < vsBottom;

            return leftOk && topOk && rightOk && bottomOk;
        }

        private static void ApplyDefaultPlacement(Window window)
        {
            window.Width  = DefaultWidth;
            window.Height = DefaultHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.WindowState = WindowState.Normal;
        }
    }
}
