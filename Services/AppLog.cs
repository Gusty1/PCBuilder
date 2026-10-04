using System.Diagnostics;
using System.IO;

namespace PCBuilder.Services
{
    /// <summary>把錯誤寫到 %LocalAppData%\PCBuilder\PCBuilder.log，出問題時可以直接查看真正的原因。</summary>
    public static class AppLog
    {
        private const long MaxLogBytes = 1_000_000;
        private static readonly Lock WriteLock = new();

        public static void Error(string context, Exception ex)
        {
            Debug.WriteLine($"{context}: {ex}");
            try
            {
                lock (WriteLock)
                {
                    Directory.CreateDirectory(AppPaths.DataFolder);
                    // ponytail: 超過 1 MB 直接清空重寫，不做輪替；錯誤很少，夠用
                    if (File.Exists(AppPaths.LogPath) && new FileInfo(AppPaths.LogPath).Length > MaxLogBytes)
                        File.Delete(AppPaths.LogPath);

                    File.AppendAllText(AppPaths.LogPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
                }
            }
            catch (Exception logEx) when (logEx is IOException or UnauthorizedAccessException)
            {
                // 記錄檔寫不進去時，不能讓原本的錯誤處理再出錯
                Debug.WriteLine($"寫入記錄檔失敗: {logEx.Message}");
            }
        }
    }
}
