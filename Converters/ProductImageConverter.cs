using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCBuilder.Converters
{
    /// <summary>
    /// 商品圖片網址 → 圖片；沒有網址或網址格式不對時回傳 NO IMAGE 替代圖（不經內建 ImageSourceConverter，網址是 null 時它會擲例外）。
    /// 同一個網址共用同一個 BitmapImage：原價屋很多商品共用同一張圖，縮圖和放大預覽也是同一個網址，
    /// 各自建立時 WPF 雖然只下載一次，卻會把同一個檔案串流交給每張圖各自解碼；共用後每個網址只解碼一次，
    /// 縮圖和放大預覽一定一致（曾出現縮圖空白、滑鼠移過去的放大預覽卻有圖）。
    /// 下載或解碼失敗時從快取移除（下次重新下載），畫面上由 Image.ImageFailed 換成 NO IMAGE（見 ProductListControl）。
    /// </summary>
    public class ProductImageConverter : IValueConverter
    {
        // 弱參考：捲走、沒有畫面在用的圖片可以被回收。原價屋的圖都是 250x450、400x400 左右的小圖，用原尺寸解碼即可
        private static readonly Dictionary<string, WeakReference<BitmapImage>> Cache = [];

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string { Length: > 0 } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return NoImage;

            if (Cache.TryGetValue(url, out var weak) && weak.TryGetTarget(out var cached))
                return cached;

            var image = new BitmapImage(uri);
            image.DownloadFailed += (_, _) => Forget(url, image);
            image.DecodeFailed += (_, _) => Forget(url, image);
            Cache[url] = new WeakReference<BitmapImage>(image);
            return image;
        }

        public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();

        private static void Forget(string url, BitmapImage image)
        {
            if (Cache.TryGetValue(url, out var weak) && weak.TryGetTarget(out var cached) && cached == image)
                Cache.Remove(url);
        }

        private static ImageSource NoImage => (ImageSource)Application.Current.FindResource("NoImageBitmap");
    }
}
