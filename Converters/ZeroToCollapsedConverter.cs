using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>數值為 0 時隱藏，非 0 時顯示（用於 Tab badge）。</summary>
    public class ZeroToCollapsedConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
