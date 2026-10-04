using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace PCBuilder.Views.Controls
{
    public partial class ProductListControl : UserControl
    {
        public static readonly DependencyProperty ProductsProperty = DependencyProperty.Register(
            nameof(Products), typeof(List<MyProductDTO>), typeof(ProductListControl),
            new PropertyMetadata(null, OnProductsChanged));

        public List<MyProductDTO>? Products
        {
            get => (List<MyProductDTO>?)GetValue(ProductsProperty);
            set => SetValue(ProductsProperty, value);
        }

        public static readonly DependencyProperty CurrentMenuCategoryProperty = DependencyProperty.Register(
            nameof(CurrentMenuCategory), typeof(MenuCategory), typeof(ProductListControl));

        /// <summary>目前選中的菜單；為 null 時數量輸入框停用（對應原 Blazor Disabled="@(CurrentMenuCategory==null)"）。</summary>
        public MenuCategory? CurrentMenuCategory
        {
            get => (MenuCategory?)GetValue(CurrentMenuCategoryProperty);
            set => SetValue(CurrentMenuCategoryProperty, value);
        }

        public static readonly RoutedEvent AddMenuProductRequestedEvent = EventManager.RegisterRoutedEvent(
            nameof(AddMenuProductRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ProductListControl));

        /// <summary>使用者變更商品數量後觸發，攜帶 (MyProductDTO Product, int NewQty)。</summary>
        public event RoutedEventHandler AddMenuProductRequested
        {
            add => AddHandler(AddMenuProductRequestedEvent, value);
            remove => RemoveHandler(AddMenuProductRequestedEvent, value);
        }

        private ICollectionView? _view;

        public ProductListControl()
        {
            InitializeComponent();
        }

        private static void OnProductsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProductListControl)d;
            control._view = CollectionViewSource.GetDefaultView(e.NewValue ?? new List<MyProductDTO>());
            control.ProductListView.ItemsSource = control._view;
            // 切換子分類時清掉上一頁的排序、搜尋字與價格區間（同一個清單的預設檢視會被重複使用，排序要明確清掉）
            control._view.SortDescriptions.Clear();
            foreach (var column in control.ProductListView.Columns)
                column.SortDirection = null;
            control.SearchBox.Text = "";
            control.MinPriceBox.Text = "";
            control.MaxPriceBox.Text = "";
            control.ApplyFilter();
        }

        private void Filter_TextChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private void ClearPriceRange_Click(object sender, RoutedEventArgs e)
        {
            MinPriceBox.Text = "";
            MaxPriceBox.Text = "";
        }

        // 搜尋文字與價格區間同時符合才顯示；都沒有設定時不篩選
        private void ApplyFilter()
        {
            if (_view is null) return;

            var search = SearchBox.Text;
            var min = ParsePrice(MinPriceBox.Text);
            var max = ParsePrice(MaxPriceBox.Text);
            _view.Filter = string.IsNullOrWhiteSpace(search) && min is null && max is null
                ? null
                : item => item is MyProductDTO p
                          && (string.IsNullOrWhiteSpace(search) || MatchesSearch(p, search))
                          && (min is null || p.Price >= min)
                          && (max is null || p.Price <= max);
            _view.Refresh();
        }

        // 點欄位標題依序切換：低→高、高→低、原本順序（原價屋的排列，同品牌、同系列排在一起）。
        // DataGrid 預設只在升冪、降冪之間切換，回不到原本順序，所以自己處理
        private void ProductListView_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            if (_view is null) return;

            ListSortDirection? next = e.Column.SortDirection switch
            {
                null => ListSortDirection.Ascending,
                ListSortDirection.Ascending => ListSortDirection.Descending,
                _ => null,
            };
            e.Column.SortDirection = next;
            _view.SortDescriptions.Clear();
            if (next is { } direction)
                _view.SortDescriptions.Add(new SortDescription(e.Column.SortMemberPath, direction));
        }

        // 讀輸入框目前的文字（不是 Value），打字當下就能篩選；空白或不是數字視為沒有設定
        private static decimal? ParsePrice(string? text) =>
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : null;

        private static bool MatchesSearch(MyProductDTO product, string search)
        {
            if (!string.IsNullOrEmpty(product.RawText) && product.RawText.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(product.Group) && product.Group.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
            if (product.Details is not null && product.Details.Any(d => d.Contains(search, StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private void ThumbnailImage_MouseEnter(object sender, RoutedEventArgs e)
        {
            // 顯示的是 NO IMAGE 替代圖時不必彈出放大預覽
            if (sender is not Image { Parent: Grid grid } image || image.Source == FindResource("NoImageBitmap")) return;
            if (grid.Children.OfType<Popup>().FirstOrDefault() is { } popup)
                popup.IsOpen = true;
        }

        // 有圖片連結但下載失敗（連結壞掉、圖被刪除）時改顯示 NO IMAGE。
        // 用 SetCurrentValue 而不是直接指定 Source：保留綁定，表格捲動重用這一格給別的商品時才會換回該商品的圖片
        private void Thumbnail_ImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            if (sender is Image image)
                image.SetCurrentValue(Image.SourceProperty, FindResource("NoImageBitmap"));
        }

        private void ThumbnailImage_MouseLeave(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Parent: Grid grid }) return;
            if (grid.Children.OfType<Popup>().FirstOrDefault() is { } popup)
                popup.IsOpen = false;
        }

        // 不能用 LostFocus：NumberBox 在 LostFocus 事件處理器執行時還沒把輸入文字寫回 Value，讀到的仍是舊值。
        // ValueChanged 在值確定後才觸發（Enter、失焦、上下按鈕）；資料重新綁定也會觸發，
        // 是否真的要存檔由 HomeViewModel 對照菜單已儲存的數量判斷。
        private void QtyNumberBox_ValueChanged(object sender, Wpf.Ui.Controls.NumberBoxValueChangedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: MyProductDTO product }) return;

            var newQty = (int)e.NewValue.GetValueOrDefault();
            RaiseEvent(new AddMenuProductRoutedEventArgs(AddMenuProductRequestedEvent, this, product, newQty));
        }
    }

    public class AddMenuProductRoutedEventArgs(RoutedEvent routedEvent, object source, MyProductDTO product, int newQty)
        : RoutedEventArgs(routedEvent, source)
    {
        public MyProductDTO Product { get; } = product;
        public int NewQty { get; } = newQty;
    }
}
