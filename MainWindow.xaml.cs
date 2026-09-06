using Microsoft.Extensions.DependencyInjection;
using PCBuilder.ViewModels;
using PCBuilder.Views;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PCBuilder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly IServiceProvider _serviceProvider;

        public MainWindow(
            MainWindowViewModel viewModel,
            ISnackbarService snackbarService,
            IContentDialogService contentDialogService,
            IServiceProvider serviceProvider)
        {
            InitializeComponent();
            DataContext = viewModel;
            _serviceProvider = serviceProvider;

            snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
            contentDialogService.SetDialogHost(RootContentDialogHost);

            MainContent.Content = _serviceProvider.GetRequiredService<HomeView>();
        }

        private void RootNavigation_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (RootNavigation.SelectedItem is not NavigationViewItem { Tag: string tag }) return;

            MainContent.Content = tag switch
            {
                "Home" => _serviceProvider.GetRequiredService<HomeView>(),
                "Menu" => _serviceProvider.GetRequiredService<MenuView>(),
                "Setting" => _serviceProvider.GetRequiredService<SettingView>(),
                _ => MainContent.Content,
            };
        }
    }
}
