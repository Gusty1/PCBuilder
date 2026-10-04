using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>Markers 清單含有 "hot"（不分大小寫）時顯示熱銷圖示，否則隱藏。</summary>
    public class MarkersToHotVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is List<string> markers && markers.Any(m => m.Contains("hot", StringComparison.OrdinalIgnoreCase)))
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
