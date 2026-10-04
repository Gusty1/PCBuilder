using Microsoft.EntityFrameworkCore;
using PCBuilder.Data;
using PCBuilder.Models;
using System.Security.Cryptography;
using System.Text;

namespace PCBuilder.Services
{
    /// <summary>
    /// 加密設定存取：以 DPAPI（CurrentUser）加密後存進 SQLite，其他 Windows 帳號或電腦拿到 DB 也無法解密。
    /// </summary>
    public class AppSettingsService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        public const string GeminiApiKey = "GeminiApiKey";

        /// <summary>儲存或清除設定後觸發（例如 AI 面板據此啟用 / 停用輸入框）。</summary>
        public event Action? SecretsChanged;

        public async Task<string?> GetSecretAsync(string key)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var setting = await dbContext.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
            if (setting is null) return null;

            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(setting.Value, null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                // 換了 Windows 帳號或電腦，舊的加密值無法解開，視同未設定
                return null;
            }
        }

        /// <summary>儲存加密設定；value 為空白時刪除該設定。</summary>
        public async Task SetSecretAsync(string key, string? value)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync();
            var setting = await dbContext.AppSettings.FirstOrDefaultAsync(x => x.Key == key);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (setting is not null) dbContext.AppSettings.Remove(setting);
            }
            else
            {
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser);
                if (setting is null)
                    dbContext.AppSettings.Add(new AppSettings { Key = key, Value = encrypted });
                else
                    setting.Value = encrypted;
            }

            await dbContext.SaveChangesAsync();
            SecretsChanged?.Invoke();
        }
    }
}
