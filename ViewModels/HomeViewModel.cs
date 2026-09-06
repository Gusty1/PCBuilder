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

        private List<MyCategoryDTO> _categories = [];
        private bool _isManualUpdate;

        public HomeViewModel(DataService dataService, CategoryService categoryService, MenuService menuService, AppPreferences preferences)
        {
            _dataService = dataService;
            _categoryService = categoryService;
            _menuService = menuService;
            _preferences = preferences;

            _dataService.OnStateChanged += HandleDataStateChanged;
            _menuService.OnStateChanged += HandleMenuStateChanged;

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
        private Dictionary<string, List<MenuProduct>> currentMenuItems = [];

        [ObservableProperty]
        private bool isPopoverOpen;

        [ObservableProperty]
        private string searchString = "";

        public int CurrentMenuTotalQty => CurrentMenuItems.Sum(kv => kv.Value.Sum(p => p.Qty));

        public int CurrentMenuTotalPrice => CurrentMenuItems.Sum(kv => kv.Value.Sum(p => p.ProductPrice * p.Qty));

        private async Task InitializeAsync()
        {
            CurrentMenuItems = await _menuService.GetDictMyMenu(CurrentMenuCategory);
            await _menuService.GetMenuCategoriesAsync();

            RefreshAvailableMenus();

            if (AvailableMenus.Count > 0)
            {
                var lastMenuId = _preferences.GetInt("LastMenuId") ?? -1;
                CurrentMenuCategory = AvailableMenus.FirstOrDefault(m => m.Id == lastMenuId) ?? AvailableMenus.First();
                _preferences.SetInt("LastMenuId", CurrentMenuCategory?.Id ?? -1);
            }

            if (_dataService.IsInitialized && !_dataService.IsLoading)
            {
                await LoadCategoriesAndRestoreSelectionAsync();
            }
        }

        private void RefreshAvailableMenus()
        {
            AvailableMenus.Clear();
            foreach (var menu in _menuService.MenuCategories.Where(m => !m.IsSend))
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
            Subcategories.Clear();
            if (value is null) return;

            _preferences.SetInt("LastCategoryId", value.CategoryId);
            var subs = (_categories.FirstOrDefault(c => c.CategoryId == value.CategoryId)?.Subcategories ?? [])
                .OrderBy(s => s.CategoryId);
            foreach (var s in subs) Subcategories.Add(s);
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

        /// <summary>供 XAML ComboBox 的 SelectionChanged 呼叫：使用者手動切換菜單。</summary>
        [RelayCommand]
        private async Task ChangeCurrentMenu(MenuCategory? value)
        {
            if (ReferenceEquals(value, CurrentMenuCategory)) return;

            CurrentMenuCategory = value;
            _preferences.SetInt("LastMenuId", value?.Id ?? -1);
            await RefreshCategoriesAndMenuAsync();
        }

        private async Task RefreshCategoriesAndMenuAsync()
        {
            _categories = await _categoryService.GetCategoriesWithDetailsAsync(CurrentMenuCategory!);
            Categories.Clear();
            foreach (var c in _categories) Categories.Add(c);

            CurrentMenuItems = await _menuService.GetDictMyMenu(CurrentMenuCategory);

            if (SelectedCategory is not null)
            {
                var subs = (_categories.FirstOrDefault(c => c.CategoryId == SelectedCategory.CategoryId)?.Subcategories ?? [])
                    .OrderBy(s => s.CategoryId);
                Subcategories.Clear();
                foreach (var s in subs) Subcategories.Add(s);
            }

            OnPropertyChanged(nameof(CurrentMenuTotalQty));
            OnPropertyChanged(nameof(CurrentMenuTotalPrice));
        }

        [RelayCommand]
        private async Task UpdateData()
        {
            _isManualUpdate = true;
            try
            {
                SetSelectedCategory(null);
                await _dataService.SeedDataIfNeededAsync();

                _categories = await _categoryService.GetCategoriesWithDetailsAsync(CurrentMenuCategory!);
                Categories.Clear();
                foreach (var c in _categories) Categories.Add(c);
                CurrentMenuItems = await _menuService.GetDictMyMenu(CurrentMenuCategory);

                if (_categories.Count > 0)
                {
                    var lastCategoryId = _preferences.GetInt("LastCategoryId") ?? -1;
                    SetSelectedCategory(_categories.FirstOrDefault(c => c.CategoryId == lastCategoryId) ?? _categories.First());
                }
            }
            finally
            {
                _isManualUpdate = false;
                _dataService.SetGlobalLoading(false);
            }
        }

        [RelayCommand]
        private async Task AddNewMenu()
        {
            await _menuService.AddMenuCategory();
            RefreshAvailableMenus();

            if (CurrentMenuCategory is null || AvailableMenus.All(m => m.Id != CurrentMenuCategory.Id))
            {
                CurrentMenuCategory = AvailableMenus.LastOrDefault();
                _preferences.SetInt("LastMenuId", CurrentMenuCategory?.Id ?? -1);
                await RefreshCategoriesAndMenuAsync();
            }
        }

        [RelayCommand]
        private async Task AddMenuProduct((MyProductDTO Product, int NewQty) args)
        {
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
        private async Task UpdatePopoverProductQty((MenuProduct Item, int NewQty) args)
        {
            var safeQty = Math.Max(1, args.NewQty);
            await _menuService.UpdateMenuProductQtyAsync(args.Item.Id, safeQty);
        }

        [RelayCommand]
        private async Task DeletePopoverProduct(MenuProduct item)
        {
            await _menuService.DeleteMenuProductAsync(item.Id);
        }

        [RelayCommand]
        private void TogglePopover() => IsPopoverOpen = !IsPopoverOpen;

        private void HandleDataStateChanged()
        {
            if (_isManualUpdate || _dataService.IsLoading) return;
            _ = LoadCategoriesAndRestoreSelectionAsync();
        }

        private async void HandleMenuStateChanged()
        {
            try
            {
                RefreshAvailableMenus();
                if (AvailableMenus.Count == 0)
                {
                    CurrentMenuCategory = null;
                    _preferences.SetInt("LastMenuId", -1);
                }

                await RefreshCategoriesAndMenuAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HandleMenuStateChanged 發生錯誤: {ex.Message}");
            }
        }

        public bool FilterProduct(MyProductDTO product)
        {
            if (string.IsNullOrWhiteSpace(SearchString)) return true;
            if (!string.IsNullOrEmpty(product.RawText) && product.RawText.Contains(SearchString, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(product.Group) && product.Group.Contains(SearchString, StringComparison.OrdinalIgnoreCase)) return true;
            if (product.Details is not null && product.Details.Any(d => d.Contains(SearchString, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        public void Dispose()
        {
            _dataService.OnStateChanged -= HandleDataStateChanged;
            _menuService.OnStateChanged -= HandleMenuStateChanged;
        }
    }
}
