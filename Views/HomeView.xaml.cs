using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.ViewModels;
using PCBuilder.Views.Controls;
using System.Windows.Controls;

namespace PCBuilder.Views
{
    public partial class HomeView : Page
    {
        private readonly HomeViewModel _viewModel;

        public HomeView(HomeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            Unloaded += (_, _) => _viewModel.Dispose();
        }

        // 菜單面板展開時，點面板以外的地方就收合；不標記 Handled，那一下點擊照常作用（例如同時切換子分類）。
        // 點展開按鈕本身交給按鈕的命令切換，否則會先被收合、再被按鈕打開
        private void Page_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_viewModel.IsMenuPanelOpen || MenuPanel.IsMouseOver || MenuPanelToggle.IsMouseOver) return;
            _viewModel.IsMenuPanelOpen = false;
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: MyCategoryDTO category } &&
                _viewModel.ChangeSelectedCategoryCommand.CanExecute(category))
            {
                _viewModel.ChangeSelectedCategoryCommand.Execute(category);
            }
        }

        private void MenuComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: MenuCategory menu } &&
                _viewModel.ChangeCurrentMenuCommand.CanExecute(menu))
            {
                _viewModel.ChangeCurrentMenuCommand.Execute(menu);
            }
        }

        private void ProductListControl_AddMenuProductRequested(object sender, System.Windows.RoutedEventArgs e)
        {
            if (e is not AddMenuProductRoutedEventArgs args) return;

            var arg = (args.Product, args.NewQty);
            if (_viewModel.AddMenuProductCommand.CanExecute(arg))
                _viewModel.AddMenuProductCommand.Execute(arg);
        }

        // 用 ValueChanged 而非 LostFocus：LostFocus 當下 NumberBox 還沒把輸入文字寫回 Value
        private void MenuItemQty_ValueChanged(object sender, Wpf.Ui.Controls.NumberBoxValueChangedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.NumberBox { Tag: MenuProduct item }) return;

            // Value 是單向綁定到已儲存的數量，相同代表只是資料重新綁定，不必存檔
            var newQty = (int)e.NewValue.GetValueOrDefault();
            if (newQty == item.Qty) return;

            var arg = (item, newQty);
            if (_viewModel.UpdateMenuItemQtyCommand.CanExecute(arg))
                _viewModel.UpdateMenuItemQtyCommand.Execute(arg);
        }
    }
}
