using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PCBuilder.Services
{
    /// <summary>
    /// 檢查 GitHub Release 是否有更新的服務實作。
    /// </summary>
    public class UpdateCheckService(HttpClient httpClient, DialogService dialogService, LinkOpenerService linkOpenerService)
    {
        private const string GitHubOwner = "gusty1";
        private const string GitHubRepo = "PCCustomizer";
        private const string ApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        private const string DownloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

        public async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var response = await httpClient.GetAsync(ApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Error getting latest version: {response.StatusCode}");
                    return "檢查失敗";
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                string latestVersionStr = doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0";
                return latestVersionStr.TrimStart('v');
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetLatestVersionAsync: {ex.Message}");
                return "檢查失敗";
            }
        }

        public async Task NotifyUpdateAvailableAsync(string latestVersionStr)
        {
            try
            {
                var currentVersionStr = GetCurrentVersionString();
                bool goToDownload = await dialogService.ShowConfirmAsync(
                    "發現新版本",
                    $"PCCustomizer {latestVersionStr} 已經發布了！\n\n您目前使用的是 {currentVersionStr}。\n是否前往 GitHub 下載頁面？",
                    "前往下載",
                    "稍後再說");

                if (goToDownload)
                    linkOpenerService.OpenExternalLink(DownloadUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in NotifyUpdateAvailableAsync: {ex.Message}");
            }
        }

        public async Task CheckAndNotifyUpdatesAsync()
        {
            try
            {
                var currentVersionStr = GetCurrentVersionString();
                var currentVersion = new Version(currentVersionStr);

                string latestVersionStr = await GetLatestVersionAsync();
                if (latestVersionStr == "檢查失敗") return;

                var latestVersion = new Version(latestVersionStr);
                if (latestVersion > currentVersion)
                    await NotifyUpdateAvailableAsync(latestVersionStr);
                else
                    Debug.WriteLine("已經是最新版本");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in CheckAndNotifyUpdatesAsync: {ex.Message}");
            }
        }

        private static string GetCurrentVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
