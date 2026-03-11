using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TrayApp.Services;

namespace TrayApp.Infrastructure
{
    public sealed class ThemeManager : IThemeManager
    {
        private const string DarkThemePath = "Themes/Theme.Dark.xaml";
        private const string LightThemePath = "Themes/Theme.Light.xaml";
        private readonly Application _application;

        public ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

        public ThemeManager(Application application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
        }

        public void Initialize(ThemeMode themeMode)
        {
            ApplyTheme(themeMode);
        }

        public void ApplyTheme(ThemeMode themeMode)
        {
            var targetPath = themeMode == ThemeMode.Light ? LightThemePath : DarkThemePath;
            var targetUri = new Uri(targetPath, UriKind.Relative);
            var mergedDictionaries = _application.Resources.MergedDictionaries;

            var existingThemeEntries = mergedDictionaries
                .Select((dictionary, index) => new { Dictionary = dictionary, Index = index })
                .Where(entry => IsThemeDictionary(entry.Dictionary))
                .ToList();

            if (existingThemeEntries.Count == 1 &&
                existingThemeEntries[0].Dictionary.Source != null &&
                string.Equals(existingThemeEntries[0].Dictionary.Source.OriginalString, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                CurrentTheme = themeMode;
                return;
            }

            var insertAt = existingThemeEntries.Count > 0
                ? existingThemeEntries.Min(entry => entry.Index)
                : mergedDictionaries.Count;

            foreach (var index in existingThemeEntries.Select(entry => entry.Index).OrderByDescending(index => index))
                mergedDictionaries.RemoveAt(index);

            mergedDictionaries.Insert(insertAt, new ResourceDictionary { Source = targetUri });
            CurrentTheme = themeMode;
        }

        public ThemeMode ToggleTheme()
        {
            var nextTheme = CurrentTheme == ThemeMode.Dark
                ? ThemeMode.Light
                : ThemeMode.Dark;

            ApplyTheme(nextTheme);
            return nextTheme;
        }

        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            var source = dictionary.Source?.OriginalString;
            if (string.IsNullOrWhiteSpace(source))
                return false;

            return source.EndsWith("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase);
        }
    }
}
