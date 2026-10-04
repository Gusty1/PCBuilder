using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace PCBuilder
{
    /// <summary>
    /// 讓 WPF UI 的 NavigationService 能透過 DI 容器解析 Page 實例，
    /// 使 Page 的建構子可以像其他類別一樣注入服務。
    /// </summary>
    public class NavigationPageProvider(IServiceProvider serviceProvider) : INavigationViewPageProvider
    {
        public object? GetPage(Type pageType) => serviceProvider.GetRequiredService(pageType);
    }
}
