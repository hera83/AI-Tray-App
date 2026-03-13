using System;
using Microsoft.Win32;

namespace TrayApp.Infrastructure
{
    /// <summary>
    /// Manages the Windows "Run at startup" registry entry for this application.
    /// Uses HKCU so no elevation is needed.
    /// </summary>
    public static class StartupManager
    {
        private const string RegistryKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName      = "AIAssistent";
        private const string LegacyAppName = "TrayAIChat";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
                return key?.GetValue(AppName) != null || key?.GetValue(LegacyAppName) != null;
            }
            catch { return false; }
        }

        public static void Enable()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
                key?.SetValue(AppName, $"\"{exePath}\"");
                if (key?.GetValue(LegacyAppName) != null)
                    key.DeleteValue(LegacyAppName);
            }
            catch { /* silently ignore in non-Windows environments */ }
        }

        public static void Disable()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
                if (key?.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
                if (key?.GetValue(LegacyAppName) != null)
                    key.DeleteValue(LegacyAppName);
            }
            catch { }
        }

        public static void Sync(bool enabled)
        {
            if (enabled) Enable();
            else Disable();
        }
    }
}
