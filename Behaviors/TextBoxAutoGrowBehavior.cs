using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrayApp.Behaviors
{
    public static class TextBoxAutoGrowBehavior
    {
        public static readonly DependencyProperty EnableAutoGrowProperty = DependencyProperty.RegisterAttached(
            "EnableAutoGrow", typeof(bool), typeof(TextBoxAutoGrowBehavior), new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnableAutoGrow(DependencyObject d, bool value) => d.SetValue(EnableAutoGrowProperty, value);
        public static bool GetEnableAutoGrow(DependencyObject d) => (bool)d.GetValue(EnableAutoGrowProperty);

        public static readonly DependencyProperty MinHeightProperty = DependencyProperty.RegisterAttached(
            "MinHeight", typeof(double), typeof(TextBoxAutoGrowBehavior), new PropertyMetadata(56.0));

        public static void SetMinHeight(DependencyObject d, double value) => d.SetValue(MinHeightProperty, value);
        public static double GetMinHeight(DependencyObject d) => (double)d.GetValue(MinHeightProperty);

        public static readonly DependencyProperty MaxHeightProperty = DependencyProperty.RegisterAttached(
            "MaxHeight", typeof(double), typeof(TextBoxAutoGrowBehavior), new PropertyMetadata(200.0));

        public static void SetMaxHeight(DependencyObject d, double value) => d.SetValue(MaxHeightProperty, value);
        public static double GetMaxHeight(DependencyObject d) => (double)d.GetValue(MaxHeightProperty);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WpfTextBox tb)
            {
                if ((bool)e.NewValue)
                {
                    tb.TextChanged += Tb_TextChanged;
                    tb.Loaded += Tb_Loaded;
                }
                else
                {
                    tb.TextChanged -= Tb_TextChanged;
                    tb.Loaded -= Tb_Loaded;
                }
            }
        }

        private static void Tb_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is WpfTextBox tb)
                tb.Dispatcher.BeginInvoke(new Action(() => AdjustHeight(tb)), DispatcherPriority.Background);
        }

        private static void Tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not WpfTextBox tb)
                return;

            tb.Dispatcher.BeginInvoke(new Action(() => AdjustHeight(tb)), DispatcherPriority.Background);
        }

        private static void AdjustHeight(WpfTextBox tb)
        {
            try
            {
                var min = GetMinHeight(tb);
                var max = GetMaxHeight(tb);

                // ExtentHeight reflects rendered wrapped content after layout.
                var desired = tb.ExtentHeight + tb.Padding.Top + tb.Padding.Bottom + 2;
                var h = Math.Max(min, Math.Min(max, desired));

                if (Math.Abs(tb.Height - h) > 0.5)
                    tb.Height = h;
            }
            catch { }
        }
    }
}
