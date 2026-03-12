using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TrayApp.Infrastructure;

namespace TrayApp.Services
{
    public enum ThemeMode
    {
        Dark,
        Light
    }

    /// <summary>
    /// All user-configurable settings.
    /// API key is stored as plain text in the user's AppData folder.
    /// For stronger protection consider encrypting with Windows DPAPI
    /// (System.Security.Cryptography.ProtectedData) before writing to disk.
    /// </summary>
    public class AppSettings
    {
        // AI gateway
        public string AiEndpoint   { get; set; } = string.Empty;
        public string ApiKey       { get; set; } = string.Empty;   // never log this field
        public string Model        { get; set; } = "gpt-4o-mini";
        public string SystemPrompt { get; set; } = "You are a helpful assistant.";
        public string UserProfile  { get; set; } = string.Empty; // legacy free-text profile
        public string UserFullName { get; set; } = string.Empty;
        public string UserPreferredName { get; set; } = string.Empty;
        public string UserOccupation { get; set; } = string.Empty;
        public string UserInterests { get; set; } = string.Empty;
        public string UserResponseStyle { get; set; } = string.Empty;
        public string UserAdditionalContext { get; set; } = string.Empty;
        public string[] CachedModels { get; set; } = Array.Empty<string>();
        public double Temperature  { get; set; } = 0.7;
        public bool   UseStreaming { get; set; } = true;

        // App behaviour
        public bool StartMinimizedToTray   { get; set; } = false;
        public bool LaunchOnWindowsStartup { get; set; } = false;
        public ThemeMode ThemeMode         { get; set; } = ThemeMode.Dark;
    }

    /// <summary>
    /// SQLite-backed settings service.
    /// Settings are stored as key/value rows in the <c>settings</c> table so that
    /// adding new fields never requires a schema migration — unknown keys are ignored
    /// and missing keys fall back to the defaults defined in <see cref="AppSettings"/>.
    /// The public interface (ISettingsService) is unchanged.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly AppDatabase _db;
        public AppSettings Settings { get; private set; }

        public SettingsService(AppDatabase db)
        {
            _db      = db;
            Settings = Load();
        }

        // ── ISettingsService ──────────────────────────────────────────────────

        public void Save() => Persist(Settings);

        public void Apply(AppSettings updated)
        {
            Settings = updated;
            Persist(updated);
        }

        // ── private ───────────────────────────────────────────────────────────

        private AppSettings Load()
        {
            var map = ReadAll();
            var s   = new AppSettings();

            if (map.TryGetValue("AiEndpoint",            out var v)) s.AiEndpoint            = v;
            if (map.TryGetValue("ApiKey",                out v))     s.ApiKey                = v;
            if (map.TryGetValue("Model",                 out v))     s.Model                 = v;
            if (map.TryGetValue("SystemPrompt",          out v))     s.SystemPrompt          = v;
            if (map.TryGetValue("UserProfile",           out v))     s.UserProfile           = v;
            if (map.TryGetValue("UserFullName",          out v))     s.UserFullName          = v;
            if (map.TryGetValue("UserPreferredName",     out v))     s.UserPreferredName     = v;
            if (map.TryGetValue("UserOccupation",        out v))     s.UserOccupation        = v;
            if (map.TryGetValue("UserInterests",         out v))     s.UserInterests         = v;
            if (map.TryGetValue("UserResponseStyle",     out v))     s.UserResponseStyle     = v;
            if (map.TryGetValue("UserAdditionalContext", out v))     s.UserAdditionalContext = v;
            else if (!string.IsNullOrWhiteSpace(s.UserProfile))       s.UserAdditionalContext = s.UserProfile;
            if (map.TryGetValue("CachedModels", out v) && !string.IsNullOrWhiteSpace(v))
            {
                try
                {
                    s.CachedModels = JsonSerializer.Deserialize<string[]>(v) ?? Array.Empty<string>();
                }
                catch
                {
                    s.CachedModels = Array.Empty<string>();
                }
            }
            if (map.TryGetValue("Temperature",           out v) &&
                double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var temp))
                                                                      s.Temperature          = temp;
            if (map.TryGetValue("UseStreaming",          out v) &&
                bool.TryParse(v, out var stream))                     s.UseStreaming          = stream;
            if (map.TryGetValue("StartMinimizedToTray",  out v) &&
                bool.TryParse(v, out var tray))                       s.StartMinimizedToTray  = tray;
            if (map.TryGetValue("LaunchOnWindowsStartup",out v) &&
                bool.TryParse(v, out var startup))                    s.LaunchOnWindowsStartup = startup;
            if (map.TryGetValue("ThemeMode", out v) &&
                Enum.TryParse<ThemeMode>(v, true, out var themeMode)) s.ThemeMode = themeMode;

            return s;
        }

        private void Persist(AppSettings s)
        {
            var pairs = new[]
            {
                ("AiEndpoint",             s.AiEndpoint),
                ("ApiKey",                 s.ApiKey),
                ("Model",                  s.Model),
                ("SystemPrompt",           s.SystemPrompt),
                ("UserProfile",            s.UserAdditionalContext),
                ("UserFullName",           s.UserFullName),
                ("UserPreferredName",      s.UserPreferredName),
                ("UserOccupation",         s.UserOccupation),
                ("UserInterests",          s.UserInterests),
                ("UserResponseStyle",      s.UserResponseStyle),
                ("UserAdditionalContext",  s.UserAdditionalContext),
                ("CachedModels",           JsonSerializer.Serialize(s.CachedModels ?? Array.Empty<string>())),
                ("Temperature",            s.Temperature.ToString(CultureInfo.InvariantCulture)),
                ("UseStreaming",            s.UseStreaming.ToString()),
                ("StartMinimizedToTray",   s.StartMinimizedToTray.ToString()),
                ("LaunchOnWindowsStartup", s.LaunchOnWindowsStartup.ToString()),
                ("ThemeMode",              s.ThemeMode.ToString())
            };

            using var conn = _db.Open();
            using var tx   = conn.BeginTransaction();

            foreach (var (key, value) in pairs)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    @"INSERT INTO settings (key, value) VALUES ($k, $v)
                      ON CONFLICT(key) DO UPDATE SET value = excluded.value";
                cmd.Parameters.AddWithValue("$k", key);
                cmd.Parameters.AddWithValue("$v", value);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private Dictionary<string, string> ReadAll()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = _db.Open();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM settings";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    map[reader.GetString(0)] = reader.GetString(1);
            }
            catch { /* first run: table exists but might be empty */ }
            return map;
        }
    }
}
