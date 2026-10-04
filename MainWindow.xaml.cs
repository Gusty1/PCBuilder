using PCBuilder.ViewModels;
using PCBuilder.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PCBuilder
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(
            MainWindowViewModel viewModel,
            ISnackbarService snackbarService,
            IContentDialogService contentDialogService,
            INavigationService navigationService)
        {
            InitializeComponent();
            DataContext = viewModel;

            snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
            contentDialogService.SetDialogHost(RootContentDialogHost);

            navigationService.SetNavigationControl(RootNavigation);

            // NavigationView 的內部 Frame 模板部件要等 Loaded 才會就緒，太早 Navigate 會 NullReferenceException
            Loaded += (_, _) =>
            {
                navigationService.Navigate(typeof(HomeView));
                _ = viewModel.CheckForUpdatesAsync();
            };
        }

        // 點擊對話框以外的空白處自動關閉，只套用在沒有底部按鈕的資訊型對話框（例如「我的電腦」），
        // 有確定／取消按鈕的對話框仍需使用者明確選擇。
        // 掛在整個視窗而不是 ContentDialogHost：WPF-UI 的 ContentDialog 只有中間方塊大小，外面的區域不屬於對話框，
        // 點擊可能落在被停用的背景元件上，事件路由根本不會經過 ContentDialogHost。
        private void MainWindow_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RootContentDialogHost.Content is ContentDialog { IsFooterVisible: false, IsMouseOver: false } dialog)
                dialog.Hide(ContentDialogResult.None);
        }

        private void RelatedLinksItem_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is not System.Windows.FrameworkElement { ContextMenu: { } menu } item) return;

            menu.PlacementTarget = item;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
            menu.IsOpen = true;
        }
    }
}
