using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrayApp.Converters
{
    public class MultiplyDoubleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not double number || double.IsNaN(number) || double.IsInfinity(number))
                return DependencyProperty.UnsetValue;

            var factor = 1d;
            if (parameter != null)
            {
                var raw = parameter.ToString();
                if (!string.IsNullOrWhiteSpace(raw) &&
                    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFactor))
                {
                    factor = parsedFactor;
                }
            }

            return Math.Max(0, number * factor);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return System.Windows.Data.Binding.DoNothing;
        }
    }
}