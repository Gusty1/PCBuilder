using PCBuilder.ViewModels;
using System.Windows.Controls;

namespace PCBuilder.Views
{
    public partial class SettingView : UserControl
    {
        public SettingView(SettingViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
