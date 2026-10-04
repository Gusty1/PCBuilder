using PCBuilder.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PCBuilder.Views.Controls
{
    public partial class AiChatPanel : UserControl
    {
        public AiChatPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is AiChatViewModel oldViewModel)
                oldViewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
            if (e.NewValue is AiChatViewModel newViewModel)
                newViewModel.Messages.CollectionChanged += Messages_CollectionChanged;
        }

        // 新訊息加入後捲到最底；要等版面配置完成再捲，否則只會捲到新訊息加入前的底部
        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(MessagesScrollViewer.ScrollToEnd));

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

            e.Handled = true;
            if (DataContext is AiChatViewModel viewModel && viewModel.SendCommand.CanExecute(null))
                viewModel.SendCommand.Execute(null);
        }
    }
}
