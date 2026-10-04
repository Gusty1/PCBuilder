using PCBuilder.ViewModels;
using System.Windows.Controls;

namespace PCBuilder.Views
{
    public partial class SettingView : Page
    {
        public SettingView(SettingViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Unloaded += (_, _) => viewModel.Dispose();
        }
    }
}
