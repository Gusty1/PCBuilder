using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>bool 為 false 時顯示，true 時隱藏（BooleanToVisibilityConverter 的反向版本）。</summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
