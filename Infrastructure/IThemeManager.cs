using TrayApp.Services;

namespace TrayApp.Infrastructure
{
    public interface IThemeManager
    {
        ThemeMode CurrentTheme { get; }
        void Initialize(ThemeMode themeMode);
        void ApplyTheme(ThemeMode themeMode);
        ThemeMode ToggleTheme();
    }
}