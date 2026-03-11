namespace TrayApp.Services
{
    public interface INotificationService
    {
        /// <summary>Show a desktop notification. Click action is handled by the implementation.</summary>
        void Show(string title, string body);
    }
}
