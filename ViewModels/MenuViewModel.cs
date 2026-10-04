using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 菜單管理頁 ViewModel：新增/改名/刪除菜單、調整或移除商品、送出估價單、零件暫存區（商品移入 / 放回 / 刪除）。
    /// 未送出的菜單一律可直接修改並立即儲存；MenuService 每次修改後都會觸發 OnStateChanged，這裡統一重新載入卡片。
    /// 對應原 Menu.razor。
    /// </summary>
    public partial class MenuViewModel : ObservableObject, IDisposable
    {
        private readonly MenuService _menuService;
        private readonly DialogService _dialogService;
        private readonly LinkOpenerService _linkOpenerService;
        private readonly ProductBrowserService _productBrowserService;

        public MenuViewModel(MenuService menuService, DialogService dialogService, LinkOpenerService linkOpenerService,
            ProductBrowserService productBrowserService)
        {
            _menuService = menuService;
            _dialogService = dialogService;
            _linkOpenerService = linkOpenerService;
            _productBrowserService = productBrowserService;

            _menuService.OnStateChanged += HandleMenuStateChanged;
            _menuService.PropertyChanged += HandleMenuServicePropertyChanged;

            _ = LoadAsync();
        }

        public ObservableCollection<MyMenuCategoryDTO> Menus { get; } = [];

        /// <summary>零件暫存區：從菜單移出、暫時不放在任何菜單的商品（存在資料庫）。</summary>
        public ObservableCollection<StagedProduct> StagedProducts { get; } = [];

        public bool IsLoading => _menuService.IsLoading;

        public bool HasMenus => Menus.Count > 0;

        public bool HasStagedProducts => StagedProducts.Count > 0;

        /// <summary>正在拖曳菜單裡的商品：標示零件暫存區可以放下。</summary>
        [ObservableProperty]
        private bool isDraggingMenuItem;

        /// <summary>正在拖曳暫存區的商品：標示可以放下的菜單（已生成估價單的除外）。</summary>
        [ObservableProperty]
        private bool isDraggingStagedItem;

        private async Task LoadAsync()
        {
            // 任何修改（數量、改名、刪除商品）都會重新載入整個清單、重建卡片；保留原本展開的菜單，否則一改數量卡片就自己收起來
            var expandedIds = Menus.Where(m => m.IsExpanded).Select(m => m.Id).ToHashSet();
            var menus = await _menuService.GetMyMenuCategoryDTOs();
            var staged = await _menuService.GetStagedProductsAsync();
            foreach (var m in menus) m.IsExpanded = expandedIds.Contains(m.Id);
            Menus.Clear();
            foreach (var m in menus) Menus.Add(m);
            StagedProducts.Clear();
            foreach (var s in staged) StagedProducts.Add(s);
            OnPropertyChanged(nameof(HasMenus));
            OnPropertyChanged(nameof(HasStagedProducts));
        }

        // OnStateChanged 只在菜單資料變更時觸發（載入狀態不會），收到才重新載入
        private void HandleMenuStateChanged() => _ = LoadAsync();

        // 生成估價單的載入狀態：MenuService.IsLoading 只發 PropertyChanged
        private void HandleMenuServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MenuService.IsLoading)) OnPropertyChanged(nameof(IsLoading));
        }

        [RelayCommand]
        private Task AddNewMenu() => _menuService.AddMenuCategory();

        [RelayCommand]
        private Task RenameMenu((MyMenuCategoryDTO Menu, string Name) args) =>
            _menuService.RenameMenuCategoryAsync(args.Menu.Id, args.Name);

        [RelayCommand]
        private Task UpdateItemQty((MenuProduct Item, int NewQty) args) =>
            _menuService.UpdateMenuProductQtyAsync(args.Item.Id, args.NewQty);

        [RelayCommand]
        private Task DeleteItem(MenuProduct item) => _menuService.DeleteMenuProductAsync(item.Id);

        [RelayCommand]
        private Task SendMenu(int menuId) => _menuService.SendMenu(menuId);

        [RelayCommand]
        private Task MoveToStaging(MenuProduct item) => _menuService.MoveToStagingAsync(item.Id);

        [RelayCommand]
        private Task MoveToMenu((StagedProduct Item, int MenuId) args) => _menuService.MoveToMenuAsync(args.Item.Id, args.MenuId);

        [RelayCommand]
        private Task DeleteStaged(StagedProduct item) => _menuService.DeleteStagedProductAsync(item.Id);

        [RelayCommand]
        private async Task DeleteMenu(MyMenuCategoryDTO menuCategory)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                "確認刪除",
                $"您確定要刪除 {menuCategory.Name} 嗎？此操作無法復原。",
                "確定刪除",
                "取消");

            if (!confirmed) return;

            await _menuService.DeleteMenuCategory(menuCategory.Id);
        }

        /// <summary>估價單網頁／圖片：用外部瀏覽器開啟。</summary>
        [RelayCommand]
        private void OpenExternalLink(string? url)
        {
            if (!string.IsNullOrEmpty(url))
                _linkOpenerService.OpenExternalLink(url);
        }

        /// <summary>商品名稱：在 App 內的「商品頁面」視窗開啟。</summary>
        [RelayCommand]
        private void OpenProductLink(string? url) => _ = _productBrowserService.OpenAsync(url);

        public void Dispose()
        {
            _menuService.OnStateChanged -= HandleMenuStateChanged;
            _menuService.PropertyChanged -= HandleMenuServicePropertyChanged;
        }
    }
}
