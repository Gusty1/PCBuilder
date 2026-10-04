using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 首頁 ViewModel：商品瀏覽、分類切換、菜單操作、當前菜單彈出視窗。
    /// 對應原 Home.razor + ProductContent.razor + CurrentMenuPopover.razor 三者邏輯的合併。
    /// </summary>
    public partial class HomeViewModel : ObservableObject, IDisposable
    {
        private readonly DataService _dataService;
        private readonly CategoryService _categoryService;
        private readonly MenuService _menuService;
        private readonly AppPreferences _preferences;
        private readonly ProductBrowserService _productBrowserService;

        private List<MyCategoryDTO> _categories = [];
        private bool _isManualUpdate;
        // DataService.OnStateChanged 會因 IsLoading/IsGlobalLoading 各自變化而觸發多次；
        // 用「是否已載入過初始商品目錄」的旗標只載入一次，之後的更新由 CatalogUpdated / 手動更新處理。
        // 不追蹤 IsLoading 的 true→false 邊緣：HomeViewModel 訂閱事件的時機可能晚於狀態變化，會錯過通知、遮罩永遠不關閉。
        private bool _hasHandledCompletion;

        public HomeViewModel(DataService dataService, CategoryService categoryService, MenuService menuService,
            AppPreferences preferences, ProductBrowserService productBrowserService)
        {
            _dataService = dataService;
            _categoryService = categoryService;
            _menuService = menuService;
            _preferences = preferences;
            _productBrowserService = productBrowserService;

            _dataService.OnStateChanged += HandleDataStateChanged;
            _dataService.CatalogUpdated += HandleCatalogUpdated;
            _menuService.OnStateChanged += HandleMenuStateChanged;
            _menuService.CurrentMenuChanged += HandleCurrentMenuChanged;

            _ = InitializeAsync();
        }

        public ObservableCollection<MyCategoryDTO> Categories { get; } = [];

        public ObservableCollection<MySubcategoryDTO> Subcategories { get; } = [];

        public ObservableCollection<MenuCategory> AvailableMenus { get; } = [];

        // 手寫屬性（不用 [ObservableProperty]）：SelectedCategory / CurrentMenuCategory 的「切換後動作」
        // 在 Blazor 原版是靠 UI 的 ValueChanged 事件觸發，跟「內部程式設定初始值」是兩條不會互相呼叫的路徑。
        // 若改用 [ObservableProperty] 自動產生的 On...Changed partial method，其内部若又對同一屬性賦值，
        // 會形成 setter → OnChanged → 又賦值 → 又觸發 OnChanged 的無限遞迴（實測會 StackOverflow 讓 App 崩潰）。
        // 因此這兩個屬性維持單純的屬性通知，「切換」動作一律由 ChangeSelectedCategoryCommand / 內部方法明確呼叫。
        private MyCategoryDTO? selectedCategory;
        public MyCategoryDTO? SelectedCategory
        {
            get => selectedCategory;
            private set => SetProperty(ref selectedCategory, value);
        }

        private MenuCategory? currentMenuCategory;
        public MenuCategory? CurrentMenuCategory
        {
            get => currentMenuCategory;
            private set => SetProperty(ref currentMenuCategory, value);
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentMenuTotalQty), nameof(CurrentMenuTotalPrice), nameof(HasCurrentMenuItems))]
        private Dictionary<string, List<MenuProduct>> currentMenuItems = [];

        public bool HasCurrentMenuItems => CurrentMenuItems.Count > 0;

        /// <summary>底部菜單列往上展開的「當前菜單」面板是否開啟。</summary>
        [ObservableProperty]
        private bool isMenuPanelOpen;

        /// <summary>左側子分類清單目前選取的子分類，右側表格顯示它的商品。</summary>
        [ObservableProperty]
        private MySubcategoryDTO? selectedSubcategory;

        public int CurrentMenuTotalQty => CurrentMenuItems.Sum(kv => kv.Value.Sum(p => p.Qty));

        public int CurrentMenuTotalPrice => CurrentMenuItems.Sum(kv => kv.Value.Sum(p => p.ProductPrice * p.Qty));

        private async Task InitializeAsync()
        {
            // 會觸發 HandleMenuStateChanged：重建菜單清單、選回當前菜單並載入菜單商品
            await _menuService.GetMenuCategoriesAsync();
            await TryLoadInitialCatalogAsync();
        }

        // 商品目錄可以顯示時（DataService.IsInitialized，可能是資料庫裡上次的資料）只載入一次
        private Task TryLoadInitialCatalogAsync()
        {
            if (!_dataService.IsInitialized || _hasHandledCompletion) return Task.CompletedTask;

            _hasHandledCompletion = true;
            return LoadCategoriesAndRestoreSelectionAsync();
        }

        private void RefreshAvailableMenus()
        {
            AvailableMenus.Clear();
            // 已生成估價單的菜單也列出來（下拉選單裡顯示為停用），但不能被選為當前菜單
            foreach (var menu in _menuService.MenuCategories)
                AvailableMenus.Add(menu);
        }

        private async Task LoadCategoriesAndRestoreSelectionAsync()
        {
            _categories = await _categoryService.GetCategoriesWithDetailsAsync(CurrentMenuCategory!);
            Categories.Clear();
            foreach (var c in _categories) Categories.Add(c);

            CurrentMenuItems = await _menuService.GetDictMyMenu(CurrentMenuCategory);

            if (_categories.Count > 0)
            {
                var lastCategoryId = _preferences.GetInt("LastCategoryId") ?? -1;
                SetSelectedCategory(_categories.FirstOrDefault(c => c.CategoryId == lastCategoryId) ?? _categories.First());
            }

            _dataService.SetGlobalLoading(false);
        }

        // 內部設定選中分類（初始化、資料重載時使用），不顯示遮罩動畫。
        private void SetSelectedCategory(MyCategoryDTO? value)
        {
            SelectedCategory = value;
            RefreshSubcategoriesFor(value);
        }

        private void RefreshSubcategoriesFor(MyCategoryDTO? value)
        {
            // 要在 Clear 之前記下來：清單清空時 ListView 會透過雙向綁定把 SelectedSubcategory 設成 null
            var previous = SelectedSubcategory;

            Subcategories.Clear();
            if (value is null)
            {
                SelectedSubcategory = null;
                return;
            }

            _preferences.SetInt("LastCategoryId", value.CategoryId);
            var subs = (_categories.FirstOrDefault(c => c.CategoryId == value.CategoryId)?.Subcategories ?? [])
                .OrderBy(s => s.CategoryId);
            foreach (var s in subs) Subcategories.Add(s);

            // 同一個主分類重新載入（例如更新資料）時留在原本的子分類；切換主分類則選第一個有商品的子分類
            // （「其他」等子分類可能沒有商品，選到會顯示空表格）。比對要含主分類：不同主分類可能有同名子分類
            SelectedSubcategory = Subcategories.FirstOrDefault(s => s.CategoryId == previous?.CategoryId && s.SubcategoryName == previous.SubcategoryName)
                                  ?? Subcategories.FirstOrDefault(s => s.ProductCount > 0)
                                  ?? Subcategories.FirstOrDefault();
        }

        /// <summary>供 XAML ComboBox 的 SelectionChanged 呼叫：使用者手動切換分類，顯示短暫遮罩。</summary>
        [RelayCommand]
        private async Task ChangeSelectedCategory(MyCategoryDTO? value)
        {
            if (ReferenceEquals(value, SelectedCategory)) return;

            _dataService.SetGlobalLoading(true, "商品資訊載入中...");
            await Task.Yield();
            SelectedCategory = value;
            RefreshSubcategoriesFor(value);
            _dataService.SetGlobalLoading(false);
        }

        /// <summary>供 XAML ComboBox 的 SelectionChanged 呼叫：使用者手動切換菜單，實際切換由 HandleCurrentMenuChanged 處理。</summary>
        [RelayCommand]
        private void ChangeCurrentMenu(MenuCategory? value) => _menuService.SetCurrentMenu(value?.Id);

        // 首頁或 AI 助手任一邊切換菜單都會觸發，首頁跟著換成同一個菜單
        private async void HandleCurrentMenuChanged()
        {
            try
            {
                var menu = AvailableMenus.FirstOrDefault(m => m.Id == _menuService.CurrentMenuId && !m.IsSend);
                if (menu is null || ReferenceEquals(menu, CurrentMenuCategory)) return;

                CurrentMenuCategory = menu;
                await RefreshMenuQuantitiesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleCurrentMenuChanged 發生錯誤: {ex.Message}");
            }
        }

        // 菜單變動只就地更新商品/子分類數量，不重建分類清單：重建會讓子分類 Tab 跳回第一頁、主分類下拉變空白、表格捲動位置重置
        private async Task RefreshMenuQuantitiesAsync()
        {
            CurrentMenuItems = await _menuService.GetDictMyMenu(CurrentMenuCategory);

            var qtyByName = CurrentMenuItems.Values.SelectMany(v => v)
                .GroupBy(p => p.ProductName)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Qty));

            foreach (var product in _categories.SelectMany(c => c.Subcategories ?? []).SelectMany(s => s.Products ?? []))
                product.Qty = product.RawText is not null && qtyByName.TryGetValue(product.RawText, out var qty) ? qty : 0;
        }

        [RelayCommand]
        private void OpenLink(string? url) => _ = _productBrowserService.OpenAsync(url);

        [RelayCommand]
        private async Task UpdateData()
        {
            _isManualUpdate = true;
            try
            {
                // 資料沒變（伺服器回 304）就不必重新載入，畫面與捲動位置都保持原樣
                if (await _dataService.RefreshAsync())
                    await LoadCategoriesAndRestoreSelectionAsync();
            }
            finally
            {
                _isManualUpdate = false;
                _dataService.SetGlobalLoading(false);
            }
        }

        // 不能在這裡再呼叫 RefreshAvailableMenus：AddMenuCategory 觸發的 HandleMenuStateChanged 已經重建清單並選好菜單
        // （原本沒有菜單時會選到這個新菜單），再重建一次會讓下拉選單失去選取而變空白
        [RelayCommand]
        private Task AddNewMenu() => _menuService.AddMenuCategory();

        [RelayCommand]
        private async Task AddMenuProduct((MyProductDTO Product, int NewQty) args)
        {
            // 數量框是雙向綁定，DTO 上的 Qty 早已等於輸入值，只能對照「菜單裡已儲存的數量」判斷是否真的有改；
            // 資料重新綁定、切換菜單時數量框也會觸發 ValueChanged，這些情況數量與已儲存相同，不應存檔
            var savedQty = CurrentMenuItems.Values.SelectMany(v => v)
                .FirstOrDefault(p => p.ProductName == args.Product.RawText)?.Qty ?? 0;
            if (savedQty == Math.Max(0, args.NewQty)) return;

            try
            {
                await _menuService.AddMenuProduct(CurrentMenuCategory!, SelectedCategory!, args.Product, args.NewQty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddMenuProduct 發生錯誤: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task UpdateMenuItemQty((MenuProduct Item, int NewQty) args)
        {
            var safeQty = Math.Max(1, args.NewQty);
            await _menuService.UpdateMenuProductQtyAsync(args.Item.Id, safeQty);
        }

        [RelayCommand]
        private async Task DeleteMenuItem(MenuProduct item)
        {
            await _menuService.DeleteMenuProductAsync(item.Id);
        }

        [RelayCommand]
        private void ToggleMenuPanel() => IsMenuPanelOpen = !IsMenuPanelOpen;

        private void HandleDataStateChanged() => _ = TryLoadInitialCatalogAsync();

        // 啟動時的背景更新寫入了新商品資料：重新載入並保留目前的分類與子分類，不顯示遮罩；
        // 手動更新會自己重新載入，初始載入還沒做時之後自然會讀到新資料
        private async void HandleCatalogUpdated()
        {
            if (_isManualUpdate || !_hasHandledCompletion) return;
            try
            {
                await LoadCategoriesAndRestoreSelectionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleCatalogUpdated 發生錯誤: {ex.Message}");
            }
        }

        private async void HandleMenuStateChanged()
        {
            try
            {
                RefreshAvailableMenus();

                // 清單重建後下拉選單會失去選取；目前菜單即使是同一個物件也要重新通知，下拉選單才會重新選回來。
                // 以共用的當前菜單 Id 為準（啟動時即上次選的菜單）；若已被刪除或送出，改選第一個可用的菜單（沒有就清空）
                var selectable = AvailableMenus.Where(m => !m.IsSend).ToList();
                currentMenuCategory = selectable.FirstOrDefault(m => m.Id == _menuService.CurrentMenuId) ?? selectable.FirstOrDefault();
                OnPropertyChanged(nameof(CurrentMenuCategory));
                _menuService.SetCurrentMenu(CurrentMenuCategory?.Id);

                await RefreshMenuQuantitiesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleMenuStateChanged 發生錯誤: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _dataService.OnStateChanged -= HandleDataStateChanged;
            _dataService.CatalogUpdated -= HandleCatalogUpdated;
            _menuService.OnStateChanged -= HandleMenuStateChanged;
            _menuService.CurrentMenuChanged -= HandleCurrentMenuChanged;
        }
    }
}
