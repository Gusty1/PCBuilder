using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace PCBuilder.Services
{
    /// <summary>
    /// 檢查 GitHub Release 是否有新版本；使用者同意後下載新版 exe 取代目前這個並重新開啟。
    /// 無法自動更新時（從 Visual Studio 執行、Release 沒有 exe、下載或替換失敗）改開 Release 頁面讓使用者自己下載。
    /// </summary>
    public class UpdateCheckService(
        HttpClient httpClient,
        DialogService dialogService,
        IContentDialogService contentDialogService,
        LinkOpenerService linkOpenerService)
    {
        private const string GitHubOwner = "Gusty1";
        private const string GitHubRepo = "PCBuilder";
        private const string ApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        private const string DownloadUrl = $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest";

        // 下載中的新版、被換下來的舊版，都放在 exe 旁邊：同一個資料夾內改名不會失敗一半
        private const string NewExeSuffix = ".new";
        private const string OldExeSuffix = ".old";
        private const string Sha256Prefix = "sha256:";
        private const int DownloadBufferSize = 81920;
        private const double ProgressDialogContentWidth = 300;
        private const int CleanUpRetryCount = 10;
        private static readonly TimeSpan CleanUpRetryDelay = TimeSpan.FromSeconds(1);

        public const string CheckFailed = "檢查失敗";
        public const string NoRelease = "尚未發布";

        /// <summary>新版 exe 的下載網址、SHA-256（GitHub 自動計算）、檔案大小。</summary>
        private sealed record ReleaseExe(string Url, string Sha256, long Size);

        // GetLatestVersionAsync 順便記下新版 exe，按「立即更新」時使用
        private ReleaseExe? _latestExe;

        public async Task<string> GetLatestVersionAsync()
        {
            try
            {
                var response = await httpClient.GetAsync(ApiUrl);
                // repo 還沒有任何 Release 時 GitHub 回 404，這不是網路問題
                if (response.StatusCode == HttpStatusCode.NotFound) return NoRelease;
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Error getting latest version: {response.StatusCode}");
                    return CheckFailed;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                _latestExe = FindExeAsset(doc.RootElement);
                string latestVersionStr = doc.RootElement.GetProperty("tag_name").GetString() ?? "0.0.0";
                return latestVersionStr.TrimStart('v');
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in GetLatestVersionAsync: {ex.Message}");
                return CheckFailed;
            }
        }

        public async Task NotifyUpdateAvailableAsync(string latestVersionStr)
        {
            try
            {
                var currentVersionStr = GetCurrentVersionString();
                var exe = IsSingleFileApp() ? _latestExe : null;
                bool confirmed = await dialogService.ShowConfirmAsync(
                    "發現新版本",
                    $"組電腦小幫手 {latestVersionStr} 已經發布了！\n\n您目前使用的是 {currentVersionStr}。\n" +
                        (exe is null ? "是否前往 GitHub 下載頁面？" : "是否立即更新？更新完成後會自動重新開啟。"),
                    exe is null ? "前往下載" : "立即更新",
                    "稍後再說");
                if (!confirmed) return;

                if (exe is null)
                    linkOpenerService.OpenExternalLink(DownloadUrl);
                else
                    await InstallAsync(exe);
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
                if (latestVersionStr is CheckFailed or NoRelease) return;

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

        /// <summary>
        /// 刪除上次自動更新換下來的舊版 exe。新版啟動時舊版可能還沒完全結束、檔案仍被鎖住，所以隔一秒重試幾次；
        /// 也順便刪掉下載到一半（例如當機）留下的新版檔案。
        /// </summary>
        public static async Task CleanUpAfterUpdateAsync()
        {
            if (Environment.ProcessPath is not { } exePath) return;

            TryDelete(exePath + NewExeSuffix);
            var oldPath = exePath + OldExeSuffix;
            for (var attempt = 0; attempt < CleanUpRetryCount && File.Exists(oldPath); attempt++)
            {
                if (TryDelete(oldPath)) return;
                await Task.Delay(CleanUpRetryDelay);
            }
        }

        /// <summary>下載新版 → 舊版改名讓出位置 → 新版用原本的檔名（捷徑不用改）→ 開啟新版並關閉自己。</summary>
        private async Task InstallAsync(ReleaseExe exe)
        {
            var exePath = Environment.ProcessPath!;
            var newPath = exePath + NewExeSuffix;
            var oldPath = exePath + OldExeSuffix;
            try
            {
                if (!await DownloadWithProgressAsync(exe, newPath)) return;

                // 執行中的 exe 不能覆寫也不能刪除，但可以改名
                File.Move(exePath, oldPath, overwrite: true);
                File.Move(newPath, exePath);
                Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AppLog.Error("自動更新失敗", ex);
                if (!File.Exists(exePath) && File.Exists(oldPath))
                    File.Move(oldPath, exePath);
                TryDelete(newPath);

                if (await dialogService.ShowConfirmAsync("自動更新失敗", "無法自動更新，是否前往 GitHub 下載頁面手動下載？", "前往下載", "取消"))
                    linkOpenerService.OpenExternalLink(DownloadUrl);
            }
        }

        /// <summary>下載時顯示進度對話框，按「取消」回傳 false；下載失敗或檔案驗證不符時拋出例外。</summary>
        private async Task<bool> DownloadWithProgressAsync(ReleaseExe exe, string path)
        {
            var progressBar = new System.Windows.Controls.ProgressBar { Height = 6, Maximum = 100 };
            var statusText = new System.Windows.Controls.TextBlock { Text = "下載中...", Margin = new Thickness(0, 0, 0, 12) };
            var dialog = new ContentDialog
            {
                Title = "下載新版本",
                Content = new System.Windows.Controls.StackPanel { Width = ProgressDialogContentWidth, Children = { statusText, progressBar } },
                CloseButtonText = "取消",
            };
            var progress = new Progress<int>(percent =>
            {
                progressBar.Value = percent;
                statusText.Text = $"下載中... {percent}%";
            });

            using var cancellation = new CancellationTokenSource();
            var dialogTask = contentDialogService.ShowAsync(dialog, CancellationToken.None);
            var downloadTask = DownloadFileAsync(exe, path, progress, cancellation.Token);

            // 對話框先結束代表使用者按了「取消」
            if (await Task.WhenAny(downloadTask, dialogTask) == dialogTask)
            {
                await cancellation.CancelAsync();
                try { await downloadTask; }
                catch (OperationCanceledException) { }
                TryDelete(path);
                return false;
            }

            dialog.Hide();
            await dialogTask;
            await downloadTask;
            await VerifySha256Async(path, exe.Sha256);
            return true;
        }

        private async Task DownloadFileAsync(ReleaseExe exe, string path, IProgress<int> progress, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync(exe.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(path);
            var buffer = new byte[DownloadBufferSize];
            long downloadedBytes = 0;
            int read, lastPercent = -1;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                var percent = (int)Math.Min(100, downloadedBytes * 100 / exe.Size);
                if (percent == lastPercent) continue;
                lastPercent = percent;
                progress.Report(percent);
            }
        }

        private static async Task VerifySha256Async(string path, string expectedSha256)
        {
            await using var stream = File.OpenRead(path);
            var actualSha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"下載的檔案 SHA-256 不符（預期 {expectedSha256}，實際 {actualSha256}）");
        }

        /// <summary>Release 裡的 exe；GitHub 沒提供 SHA-256 時無法驗證下載的檔案，不自動更新。</summary>
        private static ReleaseExe? FindExeAsset(JsonElement release)
        {
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
                    && digest?.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return new ReleaseExe(
                        asset.GetProperty("browser_download_url").GetString()!,
                        digest[Sha256Prefix.Length..],
                        asset.GetProperty("size").GetInt64());
                }
            }
            return null;
        }

        // 只有發佈成單一 exe 時才自動更新：從 Visual Studio 執行時 exe 旁邊還有 dll，只換掉 exe 沒有用。
        // 單一 exe 裡的組件沒有實體檔案，Location 是空字串
#pragma warning disable IL3000
        private static bool IsSingleFileApp() => string.IsNullOrEmpty(typeof(UpdateCheckService).Assembly.Location);
#pragma warning restore IL3000

        private static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string GetCurrentVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }
}
