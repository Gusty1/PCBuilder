using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.Services;
using System.Collections.ObjectModel;

namespace PCBuilder.ViewModels
{
    /// <summary>
    /// 菜單管理頁 ViewModel：新增/改名/刪除菜單、編輯商品、送出估價單。
    /// 對應原 Menu.razor。
    /// </summary>
    public partial class MenuViewModel : ObservableObject, IDisposable
    {
        private readonly MenuService _menuService;
        private readonly DialogService _dialogService;
        private readonly LinkOpenerService _linkOpenerService;

        private readonly List<MenuProduct> _deleteProducts = [];
        private string _originalCategoryName = "";

        public MenuViewModel(MenuService menuService, DialogService dialogService, LinkOpenerService linkOpenerService)
        {
            _menuService = menuService;
            _dialogService = dialogService;
            _linkOpenerService = linkOpenerService;

            _menuService.OnStateChanged += HandleMenuStateChanged;

            _ = LoadAsync();
        }

        public ObservableCollection<MyMenuCategoryDTO> Menus { get; } = [];

        public bool IsLoading => _menuService.IsLoading;

        [ObservableProperty]
        private int? currentlyEditingCardId;

        private async Task LoadAsync()
        {
            var menus = await _menuService.GetMyMenuCategoryDTOs();
            Menus.Clear();
            foreach (var m in menus) Menus.Add(m);
        }

        private void HandleMenuStateChanged()
        {
            OnPropertyChanged(nameof(IsLoading));
            _ = LoadAsync();
        }

        [RelayCommand]
        private async Task AddNewMenu()
        {
            await _menuService.AddMenuCategory();
            await LoadAsync();
        }

        [RelayCommand]
        private void StartEditing(MyMenuCategoryDTO category)
        {
            _deleteProducts.Clear();
            _originalCategoryName = category.Name;
            CurrentlyEditingCardId = category.Id;
        }

        [RelayCommand]
        private async Task SaveChanges(MyMenuCategoryDTO category)
        {
            await _menuService.UpdateMenuCategoryAsync(category, _deleteProducts);
            _deleteProducts.Clear();
            CurrentlyEditingCardId = null;
            _originalCategoryName = "";
            await LoadAsync();
        }

        [RelayCommand]
        private void CancelEdit(MyMenuCategoryDTO category)
        {
            category.Name = _originalCategoryName;
            _deleteProducts.Clear();
            CurrentlyEditingCardId = null;
            _originalCategoryName = "";
        }

        [RelayCommand]
        private void DeleteProduct(MenuProduct product) => _deleteProducts.Add(product);

        public bool IsMarkedForDeletion(MenuProduct product) => _deleteProducts.Any(p => p.Id == product.Id);

        [RelayCommand]
        private async Task SendMenu(int menuId)
        {
            await _menuService.SendMenu(menuId);
            await LoadAsync();
            CurrentlyEditingCardId = null;
            _originalCategoryName = "";
            _deleteProducts.Clear();
        }

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
            await LoadAsync();
        }

        [RelayCommand]
        private void OpenExternalLink(string? url)
        {
            if (!string.IsNullOrEmpty(url))
                _linkOpenerService.OpenExternalLink(url);
        }

        public void Dispose()
        {
            _menuService.OnStateChanged -= HandleMenuStateChanged;
        }
    }
}
