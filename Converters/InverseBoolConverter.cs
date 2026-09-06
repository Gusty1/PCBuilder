using System.Globalization;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>bool 反轉（true → false, false → true）。</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool b && !b;
    }
}
