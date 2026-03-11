using System;
using System.Windows;
using System.Windows.Controls;

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
            if (d is TextBox tb)
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
            if (sender is TextBox tb) AdjustHeight(tb);
        }

        private static void Tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb) AdjustHeight(tb);
        }

        private static void AdjustHeight(TextBox tb)
        {
            try
            {
                var min = GetMinHeight(tb);
                var max = GetMaxHeight(tb);
                // extent height approximates the needed height
                var desired = tb.ExtentHeight + 10;
                var h = Math.Max(min, Math.Min(max, desired));
                tb.Height = h;
            }
            catch { }
        }
    }
}
