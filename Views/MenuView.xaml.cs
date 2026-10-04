using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.ViewModels;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PCBuilder.Views
{
    public partial class MenuView : Page
    {
        private readonly MenuViewModel _viewModel;

        public MenuView(MenuViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Unloaded += (_, _) => viewModel.Dispose();
            // 待拖曳的商品只屬於「這一次」按壓：頁面上任何地方按下或放開左鍵都先清掉，按到可拖曳的列時再由該列重新記下
            // （頁面比裡面的列先收到 Preview 按下事件）。不清的話，只按一下沒拖曳的商品會留著，之後在別處按住滑過其他列就被拖出去。
            // handledEventsToo：上層的 WPF-UI 控制項可能先把滑鼠事件標記為已處理，一般的 += 訂閱會收不到
            AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, _) => ClearPendingDrag()), handledEventsToo: true);
            AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => ClearPendingDrag()), handledEventsToo: true);
        }

        // 菜單清單比這個寬度窄時（例如開著 AI 面板），標題列放不下名稱框、刪除鈕與一整行摘要（實測約需 740），
        // 摘要改放到名稱下面一行；多留一點餘裕給金額位數較多的菜單
        private const double CompactMenuHeaderWidth = 800;

        public static readonly DependencyProperty IsCompactMenuHeaderProperty = DependencyProperty.Register(
            nameof(IsCompactMenuHeader), typeof(bool), typeof(MenuView));

        /// <summary>菜單標題列改成兩行（摘要在名稱下面），由菜單清單的寬度決定；XAML 以 RelativeSource Page 綁定。</summary>
        public bool IsCompactMenuHeader
        {
            get => (bool)GetValue(IsCompactMenuHeaderProperty);
            private set => SetValue(IsCompactMenuHeaderProperty, value);
        }

        private void MenuList_SizeChanged(object sender, SizeChangedEventArgs e) =>
            IsCompactMenuHeader = e.NewSize.Width < CompactMenuHeaderWidth;

        // ── 零件暫存區的拖曳：菜單商品 → 暫存區、暫存區商品 → 菜單（菜單之間不能互相拖曳）──

        /// <summary>可以拖曳的列與卡片上的張開手掌游標（Assets\grab.cur，由 build-icon.ps1 產生），XAML 用 x:Static 引用。</summary>
        public static Cursor GrabCursor { get; } = LoadGrabCursor();

        private static Cursor LoadGrabCursor()
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/grab.cur")).Stream;
            // scaleWithDpi：依螢幕縮放與系統游標大小挑 32 / 48 / 64 px 的版本
            return new Cursor(stream, scaleWithDpi: true);
        }

        private Point _dragStart;
        private object? _pendingDragItem;
        private UIElement? _pendingDragSource;

        // 記下按下的位置、那一列與商品（MenuProduct 或 StagedProduct）；從輸入框、按鈕、商品連結按下的不算拖曳，那些地方要照常操作
        private void DragSource_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var source = (FrameworkElement)sender;
            if (IsFromInteractiveElement(e.OriginalSource, source))
            {
                ClearPendingDrag();
                return;
            }
            _pendingDragItem = source.DataContext;
            _pendingDragSource = source;
            _dragStart = e.GetPosition(this);
        }

        private void ClearPendingDrag()
        {
            _pendingDragItem = null;
            _pendingDragSource = null;
        }

        private void DragSource_MouseMove(object sender, MouseEventArgs e)
        {
            if (_pendingDragItem is null || _pendingDragSource is null) return;
            // 在視窗外放開左鍵時收不到 MouseUp，回來移動時按鈕已放開，也要清掉
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ClearPendingDrag();
                return;
            }

            var offset = e.GetPosition(this) - _dragStart;
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            // 一律以按下的那一列為來源：滑鼠可能已經移到別的列上（甚至是已生成估價單的菜單）
            var item = _pendingDragItem;
            var source = _pendingDragSource;
            ClearPendingDrag();
            // 已生成估價單的菜單不能修改，裡面的商品不能拖出來
            if (item is MenuProduct && !IsInEditableMenu(source)) return;

            // 半透明小卡片跟著滑鼠：拖曳期間 source 會持續收到 GiveFeedback，每次都移到游標的位置（游標本身維持系統預設）
            var layer = AdornerLayer.GetAdornerLayer(PageRoot);
            var preview = layer is null ? null : new DragPreviewAdorner(PageRoot, item, (DataTemplate)FindResource("DragPreviewTemplate"));
            if (preview is not null)
            {
                layer!.Add(preview);
                preview.MoveTo(CursorPosition());
            }
            GiveFeedbackEventHandler follow = (_, _) => preview?.MoveTo(CursorPosition());
            source.GiveFeedback += follow;

            // DoDragDrop 會等到放下或取消才返回，期間標示可以放下的位置
            _viewModel.IsDraggingMenuItem = item is MenuProduct;
            _viewModel.IsDraggingStagedItem = item is StagedProduct;
            try
            {
                DragDrop.DoDragDrop(source, new DataObject(item.GetType(), item), DragDropEffects.Move);
            }
            finally
            {
                source.GiveFeedback -= follow;
                if (preview is not null) layer!.Remove(preview);
                _viewModel.IsDraggingMenuItem = false;
                _viewModel.IsDraggingStagedItem = false;
            }
        }

        // 拖曳期間滑鼠由系統的拖放流程接管，Mouse.GetPosition 不會更新，改用系統游標位置換算成頁面座標
        private Point CursorPosition()
        {
            GetCursorPos(out var point);
            return PageRoot.PointFromScreen(new Point(point.X, point.Y));
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// 拖曳時跟著滑鼠的半透明商品小卡片（範本 DragPreviewTemplate）。
        /// 不接收滑鼠（IsHitTestVisible = false），才不會擋住底下要放下的目標。
        /// </summary>
        private sealed class DragPreviewAdorner : Adorner
        {
            // 卡片放在游標右下方一點，不遮住游標
            private static readonly Vector CursorOffset = new(14, 14);
            private readonly ContentPresenter _card;
            private Point _position;

            public DragPreviewAdorner(UIElement adornedElement, object item, DataTemplate template) : base(adornedElement)
            {
                _card = new ContentPresenter { Content = item, ContentTemplate = template };
                IsHitTestVisible = false;
                AddVisualChild(_card);
            }

            public void MoveTo(Point position)
            {
                _position = position;
                InvalidateArrange();
            }

            protected override int VisualChildrenCount => 1;

            protected override Visual GetVisualChild(int index) => _card;

            protected override Size ArrangeOverride(Size finalSize)
            {
                _card.Arrange(new Rect(_position + CursorOffset, _card.DesiredSize));
                return finalSize;
            }
        }

        // 用 Preview 事件：數量輸入框（TextBox）自己會處理拖放，冒泡事件到不了外層的菜單卡片
        private void StagingArea_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(MenuProduct)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void StagingArea_PreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetData(typeof(MenuProduct)) is MenuProduct item)
                _viewModel.MoveToStagingCommand.Execute(item);
        }

        private void MenuCard_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(StagedProduct)) && EditableMenuOf(sender) is not null
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void MenuCard_PreviewDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (e.Data.GetData(typeof(StagedProduct)) is StagedProduct item && EditableMenuOf(sender) is { } menu)
                _viewModel.MoveToMenuCommand.Execute((item, menu.Id));
        }

        // 「放回菜單」按鈕：列出可以放入的菜單（已生成估價單的除外）
        private void ReturnToMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: StagedProduct item } button) return;

            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
            foreach (var target in _viewModel.Menus.Where(m => !m.IsSend))
                menu.Items.Add(new MenuItem { Header = target.Name, Command = _viewModel.MoveToMenuCommand, CommandParameter = (item, target.Id) });
            if (menu.Items.Count == 0)
                menu.Items.Add(new MenuItem { Header = "沒有可以放入的菜單（已生成估價單的菜單不能修改）", IsEnabled = false });
            menu.IsOpen = true;
        }

        private static MyMenuCategoryDTO? EditableMenuOf(object element) =>
            element is FrameworkElement { DataContext: MyMenuCategoryDTO { IsSend: false } menu } ? menu : null;

        private static bool IsInEditableMenu(DependencyObject element)
        {
            for (DependencyObject? d = element; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement { DataContext: MyMenuCategoryDTO menu })
                    return !menu.IsSend;
            return false;
        }

        // 從按下的元素往上找到拖曳來源為止：途中有輸入框、按鈕或商品連結就不是拖曳
        private static bool IsFromInteractiveElement(object originalSource, DependencyObject dragSource)
        {
            var d = originalSource as DependencyObject;
            // 按在文字上時來源是文字片段（Run）：在連結裡就照常點連結，否則從所在的 TextBlock 繼續往上找
            while (d is FrameworkContentElement content)
            {
                if (content is Hyperlink) return true;
                d = content.Parent;
            }
            for (; d is Visual && d != dragSource; d = VisualTreeHelper.GetParent(d))
                if (d is TextBoxBase or ButtonBase) return true;
            return false;
        }

        private void MenuName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            SaveMenuName(sender);
        }

        private void MenuName_LostFocus(object sender, RoutedEventArgs e) => SaveMenuName(sender);

        // 直接讀 TextBox.Text：名稱是單向綁定，DTO 不會被輸入內容改掉；名稱沒變時 MenuService 不會存檔
        private void SaveMenuName(object sender)
        {
            if (sender is not TextBox { DataContext: MyMenuCategoryDTO menu } textBox) return;

            var arg = (menu, textBox.Text);
            if (_viewModel.RenameMenuCommand.CanExecute(arg))
                _viewModel.RenameMenuCommand.Execute(arg);
        }

        // 用 ValueChanged 而非 LostFocus：LostFocus 當下 NumberBox 還沒把輸入文字寫回 Value
        private void MenuItemQty_ValueChanged(object sender, Wpf.Ui.Controls.NumberBoxValueChangedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.NumberBox { Tag: MenuProduct item }) return;

            // Value 是單向綁定到已儲存的數量，相同代表只是資料重新綁定，不必存檔
            var newQty = (int)e.NewValue.GetValueOrDefault();
            if (newQty == item.Qty) return;

            var arg = (item, newQty);
            if (_viewModel.UpdateItemQtyCommand.CanExecute(arg))
                _viewModel.UpdateItemQtyCommand.Execute(arg);
        }
    }
}
