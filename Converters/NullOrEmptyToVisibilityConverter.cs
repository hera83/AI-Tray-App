using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrayApp.Converters
{
    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            var isEmpty = string.IsNullOrWhiteSpace(text);
            var invert = parameter is string mode &&
                         string.Equals(mode, "Invert", StringComparison.OrdinalIgnoreCase);

            if (invert)
                return isEmpty ? Visibility.Visible : Visibility.Collapsed;

            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }
}
