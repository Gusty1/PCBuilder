using System.Globalization;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>值非 null 時回傳 true，供 IsEnabled 等 bool 屬性使用。</summary>
    public class NotNullToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
