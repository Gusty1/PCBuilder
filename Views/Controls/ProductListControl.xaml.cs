using PCBuilder.Models.DTOs;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PCBuilder.Views.Controls
{
    public partial class ProductListControl : UserControl
    {
        public static readonly DependencyProperty ProductsProperty = DependencyProperty.Register(
            nameof(Products), typeof(List<MyProductDTO>), typeof(ProductListControl),
            new PropertyMetadata(null, OnProductsChanged));

        public List<MyProductDTO>? Products
        {
            get => (List<MyProductDTO>?)GetValue(ProductsProperty);
            set => SetValue(ProductsProperty, value);
        }

        private ICollectionView? _view;

        public ProductListControl()
        {
            InitializeComponent();
        }

        private static void OnProductsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProductListControl)d;
            control._view = CollectionViewSource.GetDefaultView(e.NewValue ?? new List<MyProductDTO>());
            control.ProductListView.ItemsSource = control._view;
        }

        private void SearchBox_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_view is null) return;

            var search = SearchBox.Text;
            _view.Filter = string.IsNullOrWhiteSpace(search)
                ? null
                : item => item is MyProductDTO p && MatchesSearch(p, search);
            _view.Refresh();
        }

        private static bool MatchesSearch(MyProductDTO product, string search)
        {
            if (!string.IsNullOrEmpty(product.RawText) && product.RawText.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(product.Group) && product.Group.Contains(search, StringComparison.OrdinalIgnoreCase))
                return true;
            if (product.Details is not null && product.Details.Any(d => d.Contains(search, StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }
    }
}
