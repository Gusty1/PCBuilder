using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using PCBuilder.Data;
using PCBuilder.Models;
using PCBuilder.Models.DTOs;
using System.Diagnostics;

namespace PCBuilder.Services
{
    /// <summary>
    /// 首頁我的菜單的商品相關服務的實作
    /// </summary>
    /// <seealso cref="CommunityToolkit.Mvvm.ComponentModel.ObservableObject" />
    public class MenuService(AppDbContext dbContext, NotificationService notificationService, CoolPcService coolPcService,
        AppPreferences preferences) : ObservableObject
    {
        private static readonly string CoolPC = "https://www.coolpc.com.tw/tmp/";
        private const string LastMenuIdKey = "LastMenuId";

        /// <summary>
        /// 首頁底部菜單列與 AI 助手共用的「當前菜單」Id（-1 代表沒有），存在偏好設定，重開 App 會還原。
        /// 任何一邊切換都呼叫 SetCurrentMenu，另一邊監聽 CurrentMenuChanged 跟著切換。
        /// </summary>
        public int CurrentMenuId => preferences.GetInt(LastMenuIdKey) ?? -1;

        public event Action? CurrentMenuChanged;

        public void SetCurrentMenu(int? id)
        {
            var value = id ?? -1;
            if (CurrentMenuId == value) return;

            preferences.SetInt(LastMenuIdKey, value);
            CurrentMenuChanged?.Invoke();
        }

        private bool _isLoading = false;
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

        private List<MenuCategory> _menuCategories = [];
        public List<MenuCategory> MenuCategories
        {
            get => _menuCategories;
            private set
            {
                if (SetProperty(ref _menuCategories, value))
                {
                    OnStateChanged?.Invoke();
                }
            }
        }

        public event Action? OnStateChanged;

        public async Task AddMenuCategory()
        {
            var count = await dbContext.MenuCategory.CountAsync(); // 使用非同步
            try
            {
                dbContext.MenuCategory.Add(new MenuCategory
                {
                    Name = $"菜單 {count + 1}",
                });
                var result = await dbContext.SaveChangesAsync(); // 使用非同步
                if (result != 0)
                {
                    notificationService.ShowSuccess($"菜單 {count + 1} 建立成功");
                }
                else
                {
                    notificationService.ShowError($"菜單 {count + 1} 建立失敗");
                }
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"新增菜單時發生錯誤: {ex.Message}");
                notificationService.ShowError($"菜單 {count + 1} 建立失敗");
            }
        }

        public async Task GetMenuCategoriesAsync()
        {
            try
            {
                var result = await dbContext.MenuCategory.Include(x => x.MenuProducts).ToListAsync();
                MenuCategories = result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查詢分類資料時發生錯誤: {ex.Message}");
                MenuCategories = [];
            }
        }

        public async Task AddMenuProduct(MenuCategory menuCategory, MyCategoryDTO myCategoryDTO,
            MyProductDTO myProductDTO, int qty)
        {
            try
            {
                if (menuCategory == null || myCategoryDTO == null || myProductDTO == null) return;

                var existProduct = await dbContext.MenuProduct.FirstOrDefaultAsync(x =>
                    x.ProductName == myProductDTO.RawText && x.MenuCategoryId == menuCategory.Id);

                if (existProduct != null)
                {
                    if (qty <= 0)
                    {
                        dbContext.MenuProduct.Remove(existProduct);
                    }
                    else
                    {
                        existProduct.Qty = qty;
                    }
                }
                else if (qty > 0)
                {
                    var findCategory = await dbContext.Category.FirstOrDefaultAsync(x => x.CategoryId == myCategoryDTO.CategoryId);
                    var subcategory = await dbContext.Subcategory.FirstOrDefaultAsync(x => x.SubcategoryName == myProductDTO.SubcategoryName);
                    var product = await dbContext.Product.FirstOrDefaultAsync(x => x.RawText == myProductDTO.RawText);
                    if (findCategory == null || subcategory == null || product == null) return;

                    var currentMenuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == menuCategory.Id);
                    if (currentMenuCategory != null)
                    {
                        currentMenuCategory.ReviseDate = DateTime.Now;
                    }

                    dbContext.MenuProduct.Add(new MenuProduct
                    {
                        MenuCategoryId = menuCategory.Id,
                        CategoryId = findCategory.CategoryId,
                        CategoryName = findCategory.CategoryName,
                        SubcategoryName = subcategory.SubcategoryName,
                        ProductName = product.RawText,
                        ProductFullText = product.FullText,
                        ProductUrl = myProductDTO.ProductUrl,
                        ProductPrice = product.Price ?? 0,
                        Qty = qty,
                    });
                }
                await dbContext.SaveChangesAsync();

                // 重新載入所有菜單並觸發 OnStateChanged
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"新增商品時發生錯誤: {ex.Message}");
                notificationService.ShowError($"商品新增失敗");
            }
        }

        public async Task<Dictionary<string, List<MenuProduct>>> GetDictMyMenu(MenuCategory menuCategory)
        {
            try
            {
                if (menuCategory == null)
                {
                    return [];
                }

                var fineMenuCategory = await dbContext.MenuCategory.Include(x => x.MenuProducts)
                    .FirstOrDefaultAsync(x => x.Id == menuCategory.Id);
                if (fineMenuCategory == null) return [];
                var menus = fineMenuCategory.MenuProducts.OrderBy(x => x.CategoryId).ToList();
                var result = new Dictionary<string, List<MenuProduct>>();
                foreach (var category in menus.DistinctBy(x => x.CategoryId))
                {
                    result[category.CategoryName] = menus.Where(x => x.CategoryId == category.CategoryId).ToList();
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"主頁簡易查詢我的商品發生錯誤: {ex.Message}");
                return [];
            }
        }

        public async Task<List<MyMenuCategoryDTO>> GetMyMenuCategoryDTOs()
        {
            try
            {
                var myMenuCategories = await dbContext.MenuCategory.Include(x => x.MenuProducts).ToListAsync();
                var result = new List<MyMenuCategoryDTO>();
                foreach (var menuCategory in myMenuCategories)
                {
                    // 直接使用已載入的 MenuProducts 在記憶體中建立 Dictionary，避免 N+1 查詢
                    var menus = menuCategory.MenuProducts.OrderBy(x => x.CategoryId).ToList();
                    var menuProductsDict = new Dictionary<string, List<MenuProduct>>();
                    foreach (var category in menus.DistinctBy(x => x.CategoryId))
                    {
                        menuProductsDict[category.CategoryName] = menus.Where(x => x.CategoryId == category.CategoryId).ToList();
                    }

                    result.Add(new MyMenuCategoryDTO
                    {
                        Id = menuCategory.Id,
                        Name = menuCategory.Name,
                        ReviseDate = menuCategory.ReviseDate,
                        IsSend = menuCategory.IsSend,
                        HtmUrl = menuCategory.HtmUrl,
                        PngUrl = menuCategory.PngUrl,
                        MyMenuProducts = menuProductsDict
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查詢我的菜單全部發生錯誤: {ex.Message}");
                return [];
            }
        }

        public async Task DeleteMenuCategory(int id)
        {
            try
            {
                var findMenuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == id);
                if (findMenuCategory == null) return;

                dbContext.MenuCategory.Remove(findMenuCategory);
                var result = await dbContext.SaveChangesAsync();
                // 重新載入並觸發 OnStateChanged，常駐的 AI 側邊欄、首頁菜單下拉才不會殘留已刪除的菜單
                await GetMenuCategoriesAsync();
                if (result > 0)
                {
                    notificationService.ShowSuccess($"刪除 {findMenuCategory.Name} 成功");
                }
                else
                {
                    notificationService.ShowError($"刪除 {findMenuCategory.Name} 失敗");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"刪除菜單時發生錯誤: {ex.Message}");
                notificationService.ShowError($"刪除菜單失敗");
            }
        }

        /// <summary>
        /// 修改菜單名稱。名稱沒變就不存檔；空白名稱不接受，但仍重新載入讓畫面還原成原本的名稱。
        /// </summary>
        public async Task RenameMenuCategoryAsync(int id, string? name)
        {
            try
            {
                var menu = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == id);
                var newName = name?.Trim();
                if (menu is null || menu.Name == newName) return;

                if (!string.IsNullOrEmpty(newName))
                {
                    menu.Name = newName;
                    menu.ReviseDate = DateTime.Now;
                    await dbContext.SaveChangesAsync();
                }
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"修改菜單名稱時發生錯誤: {ex.Message}");
                notificationService.ShowError("修改菜單名稱失敗");
            }
        }

        public async Task UpdateMenuProductQtyAsync(int menuProductId, int qty)
        {
            try
            {
                var product = await dbContext.MenuProduct.FirstOrDefaultAsync(x => x.Id == menuProductId);
                if (product == null) return;

                // 數量最低為 1
                product.Qty = Math.Max(1, qty);

                // 同步更新所屬菜單的異動時間
                var menuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == product.MenuCategoryId);
                if (menuCategory != null) menuCategory.ReviseDate = DateTime.Now;

                await dbContext.SaveChangesAsync();
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"更新商品數量時發生錯誤: {ex.Message}");
                notificationService.ShowError("更新數量失敗");
            }
        }

        public async Task DeleteMenuProductAsync(int menuProductId)
        {
            try
            {
                var product = await dbContext.MenuProduct.FirstOrDefaultAsync(x => x.Id == menuProductId);
                if (product == null) return;

                var menuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == product.MenuCategoryId);
                if (menuCategory != null) menuCategory.ReviseDate = DateTime.Now;

                dbContext.MenuProduct.Remove(product);
                await dbContext.SaveChangesAsync();
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"刪除菜單商品時發生錯誤: {ex.Message}");
                notificationService.ShowError("刪除商品失敗");
            }
        }

        public async Task<List<StagedProduct>> GetStagedProductsAsync()
        {
            try
            {
                return await dbContext.StagedProduct.OrderBy(x => x.Id).ToListAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"查詢零件暫存區時發生錯誤: {ex.Message}");
                return [];
            }
        }

        /// <summary>把菜單裡的商品整筆（含數量）移到零件暫存區；已生成估價單的菜單不能修改。</summary>
        public async Task MoveToStagingAsync(int menuProductId)
        {
            try
            {
                var product = await dbContext.MenuProduct.FirstOrDefaultAsync(x => x.Id == menuProductId);
                if (product == null) return;
                var menuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == product.MenuCategoryId);
                if (menuCategory == null || menuCategory.IsSend) return;

                dbContext.StagedProduct.Add(new StagedProduct
                {
                    CategoryId = product.CategoryId,
                    CategoryName = product.CategoryName,
                    SubcategoryName = product.SubcategoryName,
                    ProductName = product.ProductName,
                    ProductFullText = product.ProductFullText,
                    ProductUrl = product.ProductUrl,
                    ProductPrice = product.ProductPrice,
                    Qty = product.Qty,
                });
                dbContext.MenuProduct.Remove(product);
                menuCategory.ReviseDate = DateTime.Now;
                // 同一次 SaveChanges：移入暫存區與從菜單移除一起成功或一起失敗，商品不會不見或變成兩份
                await dbContext.SaveChangesAsync();
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"移到零件暫存區時發生錯誤: {ex.Message}");
                notificationService.ShowError("移到零件暫存區失敗");
            }
        }

        /// <summary>把暫存區的商品整筆放回菜單；菜單裡已有同一個商品時數量相加。已生成估價單的菜單不能放入。</summary>
        public async Task MoveToMenuAsync(int stagedProductId, int menuId)
        {
            try
            {
                var staged = await dbContext.StagedProduct.FirstOrDefaultAsync(x => x.Id == stagedProductId);
                var menuCategory = await dbContext.MenuCategory.FirstOrDefaultAsync(x => x.Id == menuId);
                if (staged == null || menuCategory == null || menuCategory.IsSend) return;

                var existing = await dbContext.MenuProduct.FirstOrDefaultAsync(x =>
                    x.MenuCategoryId == menuId && x.ProductName == staged.ProductName);
                if (existing != null)
                {
                    existing.Qty += staged.Qty;
                }
                else
                {
                    dbContext.MenuProduct.Add(new MenuProduct
                    {
                        MenuCategoryId = menuId,
                        CategoryId = staged.CategoryId,
                        CategoryName = staged.CategoryName,
                        SubcategoryName = staged.SubcategoryName,
                        ProductName = staged.ProductName,
                        ProductFullText = staged.ProductFullText,
                        ProductUrl = staged.ProductUrl,
                        ProductPrice = staged.ProductPrice,
                        Qty = staged.Qty,
                    });
                }
                dbContext.StagedProduct.Remove(staged);
                menuCategory.ReviseDate = DateTime.Now;
                await dbContext.SaveChangesAsync();
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"放回菜單時發生錯誤: {ex.Message}");
                notificationService.ShowError("放回菜單失敗");
            }
        }

        public async Task DeleteStagedProductAsync(int stagedProductId)
        {
            try
            {
                var staged = await dbContext.StagedProduct.FirstOrDefaultAsync(x => x.Id == stagedProductId);
                if (staged == null) return;

                dbContext.StagedProduct.Remove(staged);
                await dbContext.SaveChangesAsync();
                // 菜單沒變，但要觸發 OnStateChanged 讓菜單管理頁重新載入暫存區
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"刪除暫存區商品時發生錯誤: {ex.Message}");
                notificationService.ShowError("刪除暫存區商品失敗");
            }
        }

        public async Task ClearAllDataAsync()
        {
            try
            {
                // 刪除所有 MenuProduct（子項），再刪除 MenuCategory（父項）；零件暫存區也一起清空
                dbContext.MenuProduct.RemoveRange(dbContext.MenuProduct);
                dbContext.MenuCategory.RemoveRange(dbContext.MenuCategory);
                dbContext.StagedProduct.RemoveRange(dbContext.StagedProduct);
                await dbContext.SaveChangesAsync();

                // 重新載入（結果為空列表），並觸發 OnStateChanged 通知 UI
                await GetMenuCategoriesAsync();

                notificationService.ShowSuccess("所有菜單、零件暫存區與偏好設定已清除");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除資料時發生錯誤: {ex.Message}");
                notificationService.ShowError("清除資料失敗");
            }
        }

        public async Task SendMenu(int id)
        {
            try
            {
                IsLoading = true;
                var findMenu = await dbContext.MenuCategory.Include(x => x.MenuProducts).FirstOrDefaultAsync(x => x.Id == id);
                if (findMenu == null) return;
                var payloadStr = coolPcService.BuildPayload(findMenu.MenuProducts).Trim();
                var cookie = await coolPcService.GetSessionIdAsync();
                string htmUrl = "";
                string pngUrl = "";
                //由於網址和圖片的名稱是用js動態產生的，只靠C#不能直接取得，第一次先取得網址
                //第二次再把網址和圖片帶入
                for (int i = 0; i < 2; i++)
                {
                    var payload = new Dictionary<string, string>
                    {
                        { "pngdoc", payloadStr },
                        { "fdoc", payloadStr+"<@>"},
                        { "fname", htmUrl},
                        { "iname", pngUrl}
                    };
                    var result = await coolPcService.SendAndParseEstimateAsync(cookie, payload);
                    if (i == 0)
                    {
                        htmUrl = result.GetValueOrDefault().HtmFilename != null ? CoolPC + result.GetValueOrDefault().HtmFilename : null;
                        pngUrl = result.GetValueOrDefault().PngFilename != null ? CoolPC + result.GetValueOrDefault().PngFilename : null;
                    }
                }
                if (string.IsNullOrWhiteSpace(htmUrl) && string.IsNullOrWhiteSpace(pngUrl))
                {
                    notificationService.ShowError("估價單產生失敗，請稍後再試");
                    return;
                }

                findMenu.HtmUrl = htmUrl;
                findMenu.PngUrl = pngUrl;
                findMenu.IsSend = true;
                findMenu.ReviseDate = DateTime.Now;

                await dbContext.SaveChangesAsync();
                // 已送出的菜單要從首頁與 AI 側邊欄的可選菜單中移除
                await GetMenuCategoriesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"傳送菜單資料時發生錯誤: {ex.Message}");
                notificationService.ShowError($"傳送菜單失敗");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}

