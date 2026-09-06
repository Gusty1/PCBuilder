using PCBuilder.ViewModels;
using System.Windows.Controls;

namespace PCBuilder.Views
{
    public partial class MenuView : UserControl
    {
        public MenuView(MenuViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
