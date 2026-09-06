using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>集合數量 &gt; 0 時顯示，否則隱藏。</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
