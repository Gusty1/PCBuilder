using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PCBuilder.Converters
{
    /// <summary>集合數量 &gt; 0 時顯示，否則隱藏；也接受 int（例如 ObservableCollection.Count）。</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
        {
            int count => count > 0 ? Visibility.Visible : Visibility.Collapsed,
            ICollection collection => collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
            IEnumerable enumerable => enumerable.Cast<object>().Any() ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed,
        };

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
