namespace TrayApp.Services
{
    public interface ISettingsService
    {
        AppSettings Settings { get; }
        void Save();
        void Apply(AppSettings updated);
    }
}
