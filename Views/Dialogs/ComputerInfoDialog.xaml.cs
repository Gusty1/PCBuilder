using PCBuilder.ViewModels;
using System.Windows.Controls;

namespace PCBuilder.Views.Dialogs
{
    public partial class ComputerInfoDialog : UserControl
    {
        public ComputerInfoDialog(ComputerInfoDialogViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
