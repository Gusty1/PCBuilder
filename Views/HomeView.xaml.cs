using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.ViewModels;
using System.Windows.Controls;

namespace PCBuilder.Views
{
    public partial class HomeView : UserControl
    {
        private readonly HomeViewModel _viewModel;

        public HomeView(HomeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
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
    }
}
