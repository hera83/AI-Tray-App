using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrayApp.Behaviors
{
    public static class EnterKeyBehavior
    {
        public static readonly DependencyProperty SendCommandProperty = DependencyProperty.RegisterAttached(
            "SendCommand", typeof(ICommand), typeof(EnterKeyBehavior), new PropertyMetadata(null, OnSendCommandChanged));

        public static void SetSendCommand(DependencyObject d, ICommand? value) => d.SetValue(SendCommandProperty, value);
        public static ICommand? GetSendCommand(DependencyObject d) => (ICommand?)d.GetValue(SendCommandProperty);

        private static void OnSendCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WpfTextBox tb)
            {
                if (e.NewValue != null)
                {
                    tb.PreviewKeyDown += Tb_PreviewKeyDown;
                }
                else
                {
                    tb.PreviewKeyDown -= Tb_PreviewKeyDown;
                }
            }
        }

        private static void Tb_PreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is WpfTextBox tb && e.Key == Key.Enter)
            {
                var cmd = GetSendCommand(tb);
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    // allow newline
                    return;
                }
                else
                {
                    if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
