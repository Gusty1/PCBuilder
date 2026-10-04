using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using PCBuilder.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace PCBuilder.ViewModels
{
    /// <summary>AI 側邊欄中的一則訊息。</summary>
    public partial class AiChatMessage(bool isUser, string text, List<AiRecommendation>? recommendations = null, bool isError = false)
        : ObservableObject
    {
        public bool IsUser { get; } = isUser;

        public bool IsError { get; } = isError;

        public string Text { get; } = text;

        public List<AiRecommendation> Recommendations { get; } = recommendations ?? [];

        public bool HasRecommendations => Recommendations.Count > 0;

        public bool HasMultipleRecommendations => Recommendations.Count > 1;

        /// <summary>推薦商品的合計金額（單價 × 數量）。</summary>
        public int TotalPrice => Recommendations.Sum(r => (r.Product.Price ?? 0) * r.Qty);

        /// <summary>剛按過複製：複製按鈕短暫顯示打勾。</summary>
        [ObservableProperty]
        private bool isCopied;

        /// <summary>複製用的純文字：回覆內容，有推薦商品時接著列出商品、單價、數量與合計，貼到別處就是一份採購清單。</summary>
        public string ToClipboardText()
        {
            if (!HasRecommendations) return Text;

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(Text)) lines.AddRange([Text, ""]);
            lines.Add("推薦商品：");
            lines.AddRange(Recommendations.Select(r => $"{r.Product.RawText}　${r.Product.Price ?? 0:N0} × {r.Qty}"));
            lines.Add($"合計 ${TotalPrice:N0}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// AI 對話側邊欄 ViewModel：對話歷史、目標菜單、送出問題、推薦商品加入菜單 / 建立新菜單。
    /// 跟主視窗同生命週期（Singleton），切換頁面時對話不會消失；對話另存成檔案（AiChatStore），重開 App 可以接著聊。
    /// </summary>
    public partial class AiChatViewModel : ObservableObject
    {
        private readonly AiAssistantService _assistant;
        private readonly MenuService _menuService;
        private readonly DialogService _dialogService;
        private readonly NotificationService _notificationService;
        private readonly ProductBrowserService _productBrowserService;
        private readonly AppSettingsService _settingsService;
        private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromSeconds(1.5);

        private readonly List<ChatMessage> _history = [];
        private bool _isRestored;

        public AiChatViewModel(AiAssistantService assistant, MenuService menuService, DialogService dialogService,
            NotificationService notificationService, ProductBrowserService productBrowserService, AppSettingsService settingsService)
        {
            _assistant = assistant;
            _menuService = menuService;
            _dialogService = dialogService;
            _notificationService = notificationService;
            _productBrowserService = productBrowserService;
            _settingsService = settingsService;

            // 設定頁儲存或清除 API Key 時，面板開著也要立即啟用 / 停用輸入框
            _settingsService.SecretsChanged += () => _ = RefreshApiKeyAsync();
            _menuService.OnStateChanged += RefreshMenus;
            _menuService.CurrentMenuChanged += SelectCurrentMenu;
            RefreshMenus();
        }

        public ObservableCollection<AiChatMessage> Messages { get; } = [];

        public ObservableCollection<MenuCategory> AvailableMenus { get; } = [];

        public bool HasMessages => Messages.Count > 0;

        public bool HasMenus => AvailableMenus.Count > 0;

        /// <summary>有沒有可以加入商品的菜單（已生成估價單的菜單會列在下拉選單裡，但不能選）。</summary>
        public bool HasSelectableMenus => AvailableMenus.Any(m => !m.IsSend);

        [ObservableProperty]
        private MenuCategory? targetMenu;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private string inputText = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private bool isSending;

        [ObservableProperty]
        private bool isPanelOpen;

        /// <summary>是否已設定 Gemini API Key；沒有時停用輸入框與送出按鈕。</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private bool hasApiKey;

        [RelayCommand]
        private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

        partial void OnIsPanelOpenChanged(bool value)
        {
            if (!value) return;

            _ = RefreshApiKeyAsync();
            if (!_isRestored) _ = RestoreAsync();
        }

        // 第一次打開面板時才還原上次的對話：推薦商品要對照整份商品資料，等用到再讀，不拖慢啟動
        private async Task RestoreAsync()
        {
            _isRestored = true;
            if (AiChatStore.Load() is not { } saved) return;

            try
            {
                var messages = saved.Messages ?? [];
                var products = await _assistant.FindProductsAsync(
                    messages.SelectMany(m => m.Recommendations ?? []).Select(r => r.ProductName));

                // 插在最前面：還原期間使用者若已送出新問題，順序仍然正確
                _history.InsertRange(0, saved.History ?? []);
                for (var i = 0; i < messages.Count; i++)
                {
                    var m = messages[i];
                    Messages.Insert(i, new AiChatMessage(m.IsUser, m.Text ?? "", RestoreRecommendations(m.Recommendations ?? [], products), m.IsError));
                }
                OnPropertyChanged(nameof(HasMessages));
                SaveChat();
            }
            catch (Exception ex)
            {
                AppLog.Error("還原 AI 對話紀錄失敗", ex);
            }
        }

        // 價格用最新的商品資料；原價屋已下架、對照不到的商品不顯示
        private static List<AiRecommendation> RestoreRecommendations(List<SavedRecommendation> saved,
            Dictionary<string, (MyCategoryDTO Category, MyProductDTO Product)> products) =>
            [.. saved.Where(r => r.ProductName is not null && products.ContainsKey(r.ProductName))
                .Select(r => new AiRecommendation(products[r.ProductName].Category, products[r.ProductName].Product, r.Reason ?? "", r.Qty)
                {
                    IsAdded = r.IsAdded,
                })];

        private void SaveChat() => AiChatStore.Save(new SavedChat(
            [.. _history],
            [.. Messages.Select(m => new SavedChatMessage(m.IsUser, m.Text, m.IsError,
                [.. m.Recommendations.Select(r => new SavedRecommendation(r.Product.RawText ?? "", r.Reason, r.Qty, r.IsAdded))]))]));

        private async Task RefreshApiKeyAsync()
        {
            try
            {
                HasApiKey = !string.IsNullOrWhiteSpace(await _settingsService.GetSecretAsync(AppSettingsService.GeminiApiKey));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"讀取 API Key 發生錯誤: {ex}");
                HasApiKey = false;
            }
        }

        private bool CanSend() => HasApiKey && !IsSending && !string.IsNullOrWhiteSpace(InputText);

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync()
        {
            var question = InputText.Trim();
            InputText = "";
            AddMessage(new AiChatMessage(true, question));
            _history.Add(new ChatMessage("user", question));

            IsSending = true;
            try
            {
                var reply = await _assistant.ChatAsync(_history, TargetMenu);
                _history.Add(new ChatMessage("model", reply.Text));
                AddMessage(new AiChatMessage(false, reply.Text, reply.Recommendations));
            }
            catch (Exception ex)
            {
                // 失敗的提問不留在歷史，否則下次送出時 user/model 不再交替，Gemini 會拒絕
                _history.RemoveAt(_history.Count - 1);
                if (ex is not AiServiceException) Debug.WriteLine($"AI 對話發生錯誤: {ex}");
                AddMessage(new AiChatMessage(false, ex is AiServiceException ? ex.Message : "發生未預期的錯誤，請稍後再試", isError: true));
            }
            finally
            {
                IsSending = false;
                SaveChat();
            }
        }

        // 目標菜單與首頁底部的當前菜單是同一個：使用者在這裡切換時，首頁也跟著切換
        partial void OnTargetMenuChanged(MenuCategory? value)
        {
            // 清單重建時下拉選單會把 TargetMenu 暫時設成 null，不能把 null 同步出去
            if (value is not null) _menuService.SetCurrentMenu(value.Id);
        }

        [RelayCommand]
        private async Task AddToMenuAsync(AiRecommendation item)
        {
            var menu = TargetMenu ?? await ConfirmAndCreateMenuAsync("這項商品");
            if (menu is null) return;

            await AddRecommendationsAsync(menu, [item]);
        }

        [RelayCommand(CanExecute = nameof(HasPendingRecommendations))]
        private async Task AddAllToMenuAsync(AiChatMessage? message)
        {
            var pending = message?.Recommendations.Where(r => !r.IsAdded).ToList() ?? [];
            if (pending.Count == 0) return;

            var menu = TargetMenu ?? await ConfirmAndCreateMenuAsync("這些商品");
            if (menu is null) return;

            await AddRecommendationsAsync(menu, pending);
            _notificationService.ShowSuccess($"已將 {pending.Count} 項商品加入「{menu.Name}」");
        }

        // CommandParameter 綁定可能比 Command 晚生效，參數為 null 時先視為可執行，加入商品後會重新判斷
        private bool HasPendingRecommendations(AiChatMessage? message) =>
            message is null || message.Recommendations.Any(r => !r.IsAdded);

        [RelayCommand]
        private async Task CreateMenuFromRecommendationsAsync(AiChatMessage message)
        {
            var menu = await CreateMenuAsync();
            if (menu is null) return;

            await AddRecommendationsAsync(menu, message.Recommendations);
            _notificationService.ShowSuccess($"已建立「{menu.Name}」並加入 {message.Recommendations.Count} 項商品");
        }

        private async Task AddRecommendationsAsync(MenuCategory menu, IEnumerable<AiRecommendation> items)
        {
            foreach (var item in items)
            {
                await _menuService.AddMenuProduct(menu, item.Category, item.Product, item.Qty);
                item.IsAdded = true;
            }
            AddAllToMenuCommand.NotifyCanExecuteChanged();
            SaveChat();
        }

        [RelayCommand]
        private void NewChat()
        {
            _history.Clear();
            Messages.Clear();
            OnPropertyChanged(nameof(HasMessages));
            AiChatStore.Delete();
        }

        [RelayCommand]
        private void OpenLink(string? url) => _ = _productBrowserService.OpenAsync(url);

        // AllowConcurrentExecutions：等待打勾消失的 1.5 秒內指令仍算執行中，預設會讓所有訊息的複製按鈕一起停用
        [RelayCommand(AllowConcurrentExecutions = true)]
        private async Task CopyMessageAsync(AiChatMessage message)
        {
            try
            {
                Clipboard.SetText(message.ToClipboardText());
            }
            catch (ExternalException ex)
            {
                // 剪貼簿被其他程式佔用時會失敗，請使用者再按一次即可
                Debug.WriteLine($"複製到剪貼簿失敗: {ex.Message}");
                _notificationService.ShowError("複製失敗，請再試一次");
                return;
            }

            message.IsCopied = true;
            await Task.Delay(CopiedFeedbackDuration);
            message.IsCopied = false;
        }

        private void AddMessage(AiChatMessage message)
        {
            Messages.Add(message);
            OnPropertyChanged(nameof(HasMessages));
        }

        private async Task<MenuCategory?> ConfirmAndCreateMenuAsync(string itemsText)
        {
            var confirmed = await _dialogService.ShowConfirmAsync(
                "沒有可用的菜單",
                $"目前沒有未送出的菜單，要新增一個菜單並加入{itemsText}嗎？",
                "新增菜單",
                "取消");

            return confirmed ? await CreateMenuAsync() : null;
        }

        private async Task<MenuCategory?> CreateMenuAsync()
        {
            await _menuService.AddMenuCategory();
            // AddMenuCategory 會重新載入 MenuCategories 並觸發 RefreshMenus；新建的菜單是 Id 最大的那一個
            TargetMenu = AvailableMenus.MaxBy(m => m.Id);
            return TargetMenu;
        }

        private void RefreshMenus()
        {
            // 跟首頁一樣列出全部菜單，已生成估價單的在下拉選單裡停用、也不會被自動選取
            AvailableMenus.Clear();
            foreach (var menu in _menuService.MenuCategories)
                AvailableMenus.Add(menu);

            // 當前菜單已被刪除或送出時改選第一個可用的，並透過 OnTargetMenuChanged 同步給首頁
            var selectable = AvailableMenus.Where(m => !m.IsSend).ToList();
            TargetMenu = selectable.FirstOrDefault(m => m.Id == _menuService.CurrentMenuId) ?? selectable.FirstOrDefault();
            OnPropertyChanged(nameof(HasMenus));
            OnPropertyChanged(nameof(HasSelectableMenus));
        }

        private void SelectCurrentMenu()
        {
            if (AvailableMenus.FirstOrDefault(m => m.Id == _menuService.CurrentMenuId && !m.IsSend) is { } menu)
                TargetMenu = menu;
        }
    }
}
