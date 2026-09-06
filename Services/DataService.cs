using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PCBuilder.Data;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace PCBuilder.Services
{
    /// <summary>
    /// 程式啟動時取得原價屋json並寫入資料庫的實作
    /// </summary>
    /// <seealso cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject" />
    public class DataService(HttpClient httpClient, IServiceProvider serviceProvider) : ObservableObject
    {
        private const string ProductDataUrl = "https://gusty1.github.io/Database/coolPC/product.json";

        private bool _isInitialized = false;
        /// <summary>
        /// SeedDataIfNeededAsync 完成（成功或失敗）後設為 true，之後不再重置。
        /// </summary>
        public bool IsInitialized => _isInitialized;

        private bool _isLoading = false;
        /// <summary>
        /// 表示 SeedDataIfNeededAsync 是否正在執行（用於 UI 禁用按鈕）。
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                // 當值改變時，更新屬性並觸發 OnChange 事件通知 UI
                if (SetProperty(ref _isLoading, value))
                {
                    OnStateChanged?.Invoke();
                }
            }
        }

        private bool _isGlobalLoading = true;
        /// <summary>
        /// 表示是否需要顯示全域載入遮罩（涵蓋 SeedData + 首頁 DB 讀取兩個階段）。
        /// 預設為 true：應用程式一啟動遮罩就顯示，直到首頁商品載入完成後才由外部設為 false。
        /// </summary>
        public bool IsGlobalLoading
        {
            get => _isGlobalLoading;
            private set
            {
                if (SetProperty(ref _isGlobalLoading, value))
                {
                    OnStateChanged?.Invoke();
                }
            }
        }

        private string _loadingMessage = "更新原價屋資訊中...";
        /// <summary>
        /// 全域載入遮罩顯示的訊息文字。
        /// </summary>
        public string LoadingMessage => _loadingMessage;

        /// <summary>
        /// 設定全域載入遮罩的顯示狀態與訊息文字。
        /// </summary>
        public void SetGlobalLoading(bool value, string message = "更新原價屋資訊中...")
        {
            _loadingMessage = message;
            IsGlobalLoading = value;
        }

        /// <summary>
        /// 當載入狀態改變時觸發的事件。
        /// </summary>
        public event Action? OnStateChanged;

        /// <summary>
        /// 取得商品資料，並寫入資料庫
        /// </summary>
        public async Task SeedDataIfNeededAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                // 進入更新流程時，確保全域遮罩也顯示（手動觸發的情況）
                IsGlobalLoading = true;

                // 確保資料庫和資料表結構都存在
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.EnsureCreatedAsync();

                // 遵循「從下往上」的順序，清空所有資料
                Debug.WriteLine("正在清空 Product 資料表...");
                await dbContext.Product.ExecuteDeleteAsync();
                Debug.WriteLine("正在清空 Subcategory 資料表...");
                await dbContext.Subcategory.ExecuteDeleteAsync();
                Debug.WriteLine("正在清空 Category 資料表...");
                await dbContext.Category.ExecuteDeleteAsync();

                Debug.WriteLine("所有相關資料表已清空，開始從網路獲取新資料...");
                // 逾時保護：網路異常時最多等 30 秒，避免全域遮罩無限期卡住
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var jsonResponse = await httpClient.GetStringAsync(ProductDataUrl, cts.Token);
                var parsedJson = JsonSerializer.Deserialize<List<CategoryDTO>>(jsonResponse);
                if (parsedJson == null || parsedJson.Count == 0)
                {
                    Debug.WriteLine("從 JSON 讀取的資料為空。");
                    return;
                }

                var categoriesForDb = new List<Category>();
                Debug.WriteLine($"JSON 解析成功，包含 {parsedJson.Count} 個主分類。開始轉換為資料庫模型...");

                foreach (var jsonCategory in parsedJson)
                {
                    if (!int.TryParse(jsonCategory.CategoryId, out var categoryId))
                    {
                        Debug.WriteLine($"跳過無效的 CategoryId: {jsonCategory.CategoryId}");
                        continue;
                    }
                    var newDbCategory = new Category
                    {
                        CategoryId = categoryId,
                        CategoryName = jsonCategory.CategoryName.Trim(),
                        Summary = jsonCategory.Summary,
                    };
                    foreach (var jsonSubcategory in jsonCategory.Subcategories)
                    {
                        var newDbSubcategory = new Subcategory
                        {
                            CategoryId = newDbCategory.CategoryId,
                            SubcategoryName = jsonSubcategory.Name.Trim(),
                        };

                        for (int i = 0; i < jsonSubcategory.Products.Count; i++)
                        {
                            var jsonProduct = jsonSubcategory.Products[i];

                            if (jsonProduct.Price == null) continue;

                            var newDbProduct = new Product
                            {
                                SubcategoryName = newDbSubcategory.SubcategoryName,
                                Index = jsonProduct.Index,
                                Group = jsonProduct.Group,
                                Price = jsonProduct.Price - (jsonProduct.Discount ?? 0),
                                Markers = (jsonProduct.Markers == null || jsonProduct.Markers.Count == 0) ? [] : jsonProduct.Markers,
                                RawText = jsonProduct.RawText.Trim(),
                                FullText = jsonProduct.FullText.Trim(),
                                ImgUrl = jsonProduct.ImgUrl,
                                ProductUrl = jsonProduct.ProductUrl,
                                Details = jsonProduct.Details
                            };
                            newDbSubcategory.Products.Add(newDbProduct);
                        }
                        newDbCategory.Subcategories.Add(newDbSubcategory);
                    }
                    categoriesForDb.Add(newDbCategory);
                }
                Debug.WriteLine($"資料轉換完成，準備將 {categoriesForDb.Count} 個主分類及其所有子項寫入資料庫...");

                await dbContext.Category.AddRangeAsync(categoriesForDb);
                await dbContext.SaveChangesAsync();

                Debug.WriteLine("資料寫入完成");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化資料時發生嚴重錯誤: {ex.GetType().Name} - {ex.Message}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"--> 內部錯誤訊息: {ex.InnerException.Message}");
                }
                // 透過 scope 解析 Scoped 的 NotificationService，避免 Singleton 直接持有 Scoped 服務
                using var notifyScope = serviceProvider.CreateScope();
                var notificationService = notifyScope.ServiceProvider.GetRequiredService<NotificationService>();
                notificationService.ShowError(@"資料更新失敗，請檢查網路連線或稍後再試，若還有問題請聯絡我");
            }
            finally
            {
                _isInitialized = true;
                IsLoading = false;
            }
        }
    }
}