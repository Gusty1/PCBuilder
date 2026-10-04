using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PCBuilder.Converters
{
    /// <summary>
    /// 依菜單 Id 給固定的代表色（6 色輪流）：ConverterParameter="Tint" 回傳標題列用的淡色，其他回傳色條用的實色。
    /// 用 Id 而不是排列順序，刪掉別的菜單時顏色不會跟著變。
    /// </summary>
    public class MenuColorConverter : IValueConverter
    {
        // 約 15% 不透明度：淺色、深色模式都看得出色系，又不影響文字閱讀
        private const byte TintAlpha = 0x26;

        private static readonly Color[] Palette =
        [
            Color.FromRgb(0x3B, 0x82, 0xF6), // 藍
            Color.FromRgb(0x22, 0xC5, 0x5E), // 綠
            Color.FromRgb(0xF9, 0x73, 0x16), // 橙
            Color.FromRgb(0xA8, 0x55, 0xF7), // 紫
            Color.FromRgb(0x14, 0xB8, 0xA6), // 青
            Color.FromRgb(0xEC, 0x48, 0x99), // 粉
        ];

        private static readonly Brush[] SolidBrushes = [.. Palette.Select(c => Frozen(c))];
        private static readonly Brush[] TintBrushes = [.. Palette.Select(c => Frozen(Color.FromArgb(TintAlpha, c.R, c.G, c.B)))];

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var index = value is int id ? Math.Abs(id % Palette.Length) : 0;
            return parameter as string == "Tint" ? TintBrushes[index] : SolidBrushes[index];
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
