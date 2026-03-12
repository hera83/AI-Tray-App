using System.Windows;
using System.Windows.Input;

namespace TrayApp.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            try
            {
                DragMove();
            }
            catch
            {
                // no-op: DragMove can throw if mouse state changes mid-gesture
            }
        }
    }
}
