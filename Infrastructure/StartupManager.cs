using System;
using System.IO;
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
        private static readonly string[] StartupShortcutNames =
        {
            "AI Assistent.lnk",
            "AIAssistent.lnk",
            "TrayAIChat.lnk"
        };

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: false);
                return key?.GetValue(AppName) != null
                    || key?.GetValue(LegacyAppName) != null
                    || HasStartupShortcut();
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

                // Keep a single source of truth to avoid duplicate startups.
                RemoveStartupShortcuts();
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

                RemoveStartupShortcuts();
            }
            catch { }
        }

        public static void Sync(bool enabled)
        {
            if (enabled) Enable();
            else Disable();
        }

        private static bool HasStartupShortcut()
        {
            try
            {
                var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (string.IsNullOrWhiteSpace(startupDir) || !Directory.Exists(startupDir))
                    return false;

                foreach (var fileName in StartupShortcutNames)
                {
                    if (File.Exists(Path.Combine(startupDir, fileName)))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveStartupShortcuts()
        {
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(startupDir) || !Directory.Exists(startupDir))
                return;

            foreach (var fileName in StartupShortcutNames)
            {
                var path = Path.Combine(startupDir, fileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
