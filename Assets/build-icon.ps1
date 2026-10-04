# 產生 App 用的圖示素材，修改圖形後重新執行：pwsh -STA -File Assets\build-icon.ps1
#   Assets\app.ico  — App icon，圖形在 Assets\AppIcon.xaml（標題列也用同一份）
#   Assets\grab.cur — 菜單管理頁「可以拖曳」的張開手掌游標，圖形在下面的 $hand
# 每個尺寸各自用 WPF 繪製（不是把大圖縮小），小尺寸也清楚。
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

# 把圖畫成指定大小的 BGRA 點陣圖（非預乘 alpha，ICO / CUR 都用這種）
function Render-Bitmap($image, [int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($image, (New-Object System.Windows.Rect 0, 0, $size, $size))
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($bitmap, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
}

function ConvertTo-Png($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

# 游標用 BMP 格式（DIB）：BITMAPINFOHEADER（高度記兩倍）+ 由下往上的 BGRA 像素 + 1bpp 透明遮罩
function ConvertTo-Dib($bitmap, [int]$size) {
    $stride = $size * 4
    $pixels = New-Object byte[] ($stride * $size)
    $bitmap.CopyPixels($pixels, $stride, 0)
    $maskStride = [int][Math]::Ceiling($size / 32) * 4

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]0)
    $writer.Write([uint32](($stride + $maskStride) * $size))
    $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $stride, $stride) }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $mask = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ($pixels[$y * $stride + $x * 4 + 3] -eq 0) { $mask[[Math]::Floor($x / 8)] = $mask[[Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $writer.Write($mask)
    }
    $writer.Flush()
    , $stream.ToArray()
}

# ICO / CUR 共用的容器格式：6 bytes 檔頭 + 每個尺寸 16 bytes 目錄 + 圖片資料（寬高 256 記為 0）。
# 目錄裡 ICO 記色彩平面數與位元數，CUR 改記游標熱點座標（$hotspots）
function Write-IconFile([string]$path, [int]$type, [int[]]$sizes, $images, $hotspots) {
    $file = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $file
    $writer.Write([uint16]0); $writer.Write([uint16]$type); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = [byte]($sizes[$i] % 256)
        $writer.Write($dimension); $writer.Write($dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        if ($hotspots) { $writer.Write([uint16]$hotspots[$i][0]); $writer.Write([uint16]$hotspots[$i][1]) }
        else { $writer.Write([uint16]1); $writer.Write([uint16]32) }
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $file.ToArray())
    "已產生 $path（$($sizes -join ', ') px）"
}

# ── App icon：Windows 在標題列、工作列、檔案總管、不同 DPI 下會挑最接近的尺寸 ──
$icon = [System.Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $PSScriptRoot 'AppIcon.xaml') -Raw -Encoding UTF8))['AppIconImage']
$iconSizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
Write-IconFile (Join-Path $PSScriptRoot 'app.ico') 1 $iconSizes @($iconSizes | ForEach-Object { , (ConvertTo-Png (Render-Bitmap $icon $_)) }) $null

# ── 張開手掌游標：白色手掌、黑色外框，以 32x32 座標繪製；手指之間留細縫，外框才會畫出指縫 ──
function New-Capsule([double]$x, [double]$y, [double]$width, [double]$height) {
    New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect $x, $y, $width, $height), ($width / 2), ($width / 2))
}
$thumb = New-Capsule 5.8 12.5 3.4 9
$thumb.Transform = New-Object System.Windows.Media.RotateTransform(-35, 7.5, 17)
$hand = New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect 8.6, 13, 15.4, 15), 4.5, 4.5)  # 掌心
foreach ($part in (New-Capsule 8.6 4.5 3.4 12), (New-Capsule 12.6 3 3.4 13), (New-Capsule 16.6 4 3.4 12), (New-Capsule 20.6 6.5 3.4 10), $thumb) {
    $hand = [System.Windows.Media.Geometry]::Combine($hand, $part, [System.Windows.Media.GeometryCombineMode]::Union, $null)
}
$handDrawing = New-Object System.Windows.Media.DrawingGroup
# 透明的 32x32 外框：圖的大小以圖形範圍計算，沒有它整隻手會被放大到填滿
$handDrawing.Children.Add((New-Object System.Windows.Media.GeometryDrawing([System.Windows.Media.Brushes]::Transparent, $null, [System.Windows.Media.Geometry]::Parse('M0,0 H32 V32 H0 Z'))))
$handDrawing.Children.Add((New-Object System.Windows.Media.GeometryDrawing([System.Windows.Media.Brushes]::White, (New-Object System.Windows.Media.Pen([System.Windows.Media.Brushes]::Black, 1.2)), $hand)))
$handImage = New-Object System.Windows.Media.DrawingImage $handDrawing

# 熱點在掌心（32x32 座標的 16,15），各尺寸等比例換算；Windows 依 DPI 與游標大小設定挑尺寸
$cursorSizes = 32, 48, 64
Write-IconFile (Join-Path $PSScriptRoot 'grab.cur') 2 $cursorSizes `
    @($cursorSizes | ForEach-Object { , (ConvertTo-Dib (Render-Bitmap $handImage $_) $_) }) `
    @($cursorSizes | ForEach-Object { , @([int](16 * $_ / 32), [int](15 * $_ / 32)) })
