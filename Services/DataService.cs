using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PCBuilder.Data;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PCBuilder.Services
{
    /// <summary>
    /// 取得原價屋商品資料（json）並寫入資料庫的實作
    /// </summary>
    /// <seealso cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject" />
    public class DataService(HttpClient httpClient, IServiceProvider serviceProvider, AppPreferences preferences) : ObservableObject
    {
        private const string ProductDataUrl = "https://gusty1.github.io/Database/coolPC/product.json";
        private const string ETagPreferenceKey = "ProductDataETag";

        private bool _isInitialized = false;
        /// <summary>
        /// 商品目錄可以顯示時設為 true（資料庫已有資料，或第一次下載完成／失敗），之後不再重置。
        /// </summary>
        public bool IsInitialized => _isInitialized;

        private bool _isLoading = false;
        /// <summary>
        /// 表示商品資料更新（啟動時的背景更新或手動更新）是否正在執行。
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

        /// <summary>背景更新寫入了新的商品資料時觸發（首頁據此重新載入，保留目前選的分類）。</summary>
        public event Action? CatalogUpdated;

        /// <summary>一次更新的結果：是否寫入了新資料、失敗時的例外（已寫入記錄檔）、新資料的 ETag。</summary>
        private sealed record CatalogUpdate(bool Changed, Exception? Error, string? ETag = null);

        private Task<CatalogUpdate>? _runningUpdate;

        /// <summary>
        /// 啟動時呼叫。資料庫已有商品目錄就先讓首頁顯示（不用等下載），再在背景更新，有新資料才觸發 CatalogUpdated，
        /// 背景更新失敗只寫記錄檔；第一次使用（沒有任何商品）才讓遮罩等到下載完成，失敗時要提示使用者。
        /// </summary>
        public async Task InitializeAsync()
        {
            bool hasCatalog;
            try
            {
                hasCatalog = await EnsureDatabaseAsync();
            }
            catch (Exception ex)
            {
                AppLog.Error("建立資料庫失敗", ex);
                Notify(n => n.ShowError(DescribeSeedError(ex)));
                MarkInitialized();
                return;
            }

            if (hasCatalog)
            {
                MarkInitialized();
                if ((await UpdateCatalogAsync()).Changed) CatalogUpdated?.Invoke();
                return;
            }

            var result = await UpdateCatalogAsync();
            if (result.Error is not null) Notify(n => n.ShowError(DescribeSeedError(result.Error)));
            MarkInitialized();
        }

        /// <summary>
        /// 使用者按「更新資料」：顯示遮罩並等待更新，失敗時提示原因；遮罩由首頁重新載入後關閉。回傳是否寫入了新資料。
        /// </summary>
        public async Task<bool> RefreshAsync()
        {
            SetGlobalLoading(true);
            var result = await UpdateCatalogAsync();
            if (result.Error is not null) Notify(n => n.ShowError(DescribeSeedError(result.Error)));
            else if (!result.Changed) Notify(n => n.ShowInfo("商品資料已是最新"));
            return result.Changed;
        }

        private void MarkInitialized()
        {
            _isInitialized = true;
            OnStateChanged?.Invoke();
        }

        // 透過 scope 解析 NotificationService，避免 Singleton 直接持有 Scoped 服務
        private void Notify(Action<NotificationService> show)
        {
            using var scope = serviceProvider.CreateScope();
            show(scope.ServiceProvider.GetRequiredService<NotificationService>());
        }

        private Task<AppDbContext> CreateDbContextAsync() =>
            serviceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

        // 建立資料庫與資料表，回傳是否已有商品目錄
        private async Task<bool> EnsureDatabaseAsync()
        {
            await using var dbContext = await CreateDbContextAsync();
            await dbContext.Database.EnsureCreatedAsync();

            // EnsureCreatedAsync 只在資料庫不存在時建表，舊版建立的資料庫不會補上之後新增的資料表；
            // 在這裡補建，使用者就不必為了新功能刪掉資料庫（會失去所有菜單）
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "AppSettings" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AppSettings" PRIMARY KEY AUTOINCREMENT,
                    "Key" TEXT NOT NULL,
                    "Value" BLOB NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_AppSettings_Key" ON "AppSettings" ("Key");
                CREATE TABLE IF NOT EXISTS "StagedProduct" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_StagedProduct" PRIMARY KEY AUTOINCREMENT,
                    "CategoryId" INTEGER NOT NULL,
                    "CategoryName" TEXT NOT NULL,
                    "SubcategoryName" TEXT NOT NULL,
                    "ProductName" TEXT NOT NULL,
                    "ProductFullText" TEXT NOT NULL,
                    "ProductUrl" TEXT NULL,
                    "ProductPrice" INTEGER NOT NULL,
                    "Qty" INTEGER NOT NULL);
                """);

            return await dbContext.Category.AnyAsync();
        }

        // 同時只跑一個更新：手動更新遇到背景更新進行中時，等同一個結果
        private async Task<CatalogUpdate> UpdateCatalogAsync()
        {
            if (_runningUpdate is not null) return await _runningUpdate;

            IsLoading = true;
            // 下載、解析與寫入資料庫都放到背景執行緒：SQLite 的非同步 API 實際上是同步執行，留在 UI 執行緒會讓畫面卡住數秒
            var etag = preferences.GetString(ETagPreferenceKey);
            _runningUpdate = Task.Run(() => DownloadAndSaveAsync(etag));
            try
            {
                var result = await _runningUpdate;
                // 偏好設定不是執行緒安全的，回到 UI 執行緒才寫入；寫入成功才記住 ETag，下次資料沒變就不必重新下載
                if (result.Changed)
                {
                    if (result.ETag is not null) preferences.SetString(ETagPreferenceKey, result.ETag);
                    else preferences.Remove(ETagPreferenceKey);
                }
                return result;
            }
            finally
            {
                _runningUpdate = null;
                IsLoading = false;
            }
        }

        /// <summary>
        /// 下載商品資料並在交易中整批替換。帶上次的 ETag 詢問，資料沒變時伺服器回 304，跳過下載與重寫。
        /// 先下載並轉換完成才動資料庫，失敗時原本的商品資料都還在。
        /// </summary>
        private async Task<CatalogUpdate> DownloadAndSaveAsync(string? etag)
        {
            try
            {
                await using var dbContext = await CreateDbContextAsync();

                using var request = new HttpRequestMessage(HttpMethod.Get, ProductDataUrl);
                // 資料庫沒有商品時（例如剛建立）不能帶 ETag，否則伺服器回 304 會什麼都拿不到
                if (etag is not null && await dbContext.Category.AnyAsync())
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);

                // 逾時保護：避免遮罩無限期卡住
                using var cts = new CancellationTokenSource(DownloadTimeout);
                using var response = await httpClient.SendAsync(request, cts.Token);
                if (response.StatusCode == HttpStatusCode.NotModified) return new CatalogUpdate(false, null);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var categories = ToEntities(JsonSerializer.Deserialize<List<CategoryDTO>>(json) ?? []);
                if (categories.Count == 0) throw new JsonException("商品資料是空的");

                // 清空與寫入放在同一個交易：寫入途中出錯時整批還原，不會留下半套或空的商品目錄；遵循「從下往上」的順序清空
                await using var transaction = await dbContext.Database.BeginTransactionAsync();
                await dbContext.Product.ExecuteDeleteAsync();
                await dbContext.Subcategory.ExecuteDeleteAsync();
                await dbContext.Category.ExecuteDeleteAsync();
                await dbContext.Category.AddRangeAsync(categories);
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                return new CatalogUpdate(true, null, response.Headers.ETag?.ToString());
            }
            catch (Exception ex)
            {
                AppLog.Error("更新原價屋商品資料失敗", ex);
                return new CatalogUpdate(false, ex);
            }
        }

        private static List<Category> ToEntities(List<CategoryDTO> parsedJson)
        {
            var categoriesForDb = new List<Category>();
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
            return categoriesForDb;
        }

        private const int SqliteBusy = 5;
        private const int SqliteLocked = 6;
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 依失敗原因給使用者看得懂的訊息（原本一律顯示「請檢查網路連線」，資料庫被鎖住等非網路問題也被誤導）。
        /// 先下載完才在交易中寫入，所以任何失敗原本的商品資料都還在。
        /// </summary>
        internal static string DescribeSeedError(Exception ex)
        {
            const string Retry = "再按右上角的更新按鈕重試";
            return ex switch
            {
                HttpRequestException => $"無法連線到商品資料來源，請確認網路連線後，{Retry}",
                OperationCanceledException => $"下載商品資料逾時（超過 {DownloadTimeout.TotalSeconds:0} 秒），網路可能較慢，請稍後{Retry}",
                JsonException => "商品資料格式有誤，請稍後再試；若持續發生請聯絡我",
                _ when FindSqliteErrorCode(ex) is SqliteBusy or SqliteLocked =>
                    $"資料庫正被其他程式使用（可能同時開了兩個組電腦小幫手），請關閉其他視窗後，{Retry}",
                _ => $"資料更新失敗，請稍後再試；若持續發生請聯絡我（詳細原因記錄在 {AppPaths.LogPath}）",
            };
        }

        // EF Core 會把 SqliteException 包在 DbUpdateException 等例外裡面，要往內層找
        private static int? FindSqliteErrorCode(Exception? ex)
        {
            for (; ex is not null; ex = ex.InnerException)
                if (ex is SqliteException sqlite) return sqlite.SqliteErrorCode;
            return null;
        }
    }
}
