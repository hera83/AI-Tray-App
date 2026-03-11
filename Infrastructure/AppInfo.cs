using System.Reflection;

namespace TrayApp.Infrastructure
{
    public static class AppInfo
    {
        public static string ProductName => "Tray AI Chat";

        public static string Version
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? "ukendt"
                    : $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }
    }
}
