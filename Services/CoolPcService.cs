using PCBuilder.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace PCBuilder.Services
{
    /// <summary>
    /// 負責與原價屋網站進行 HTTP 通訊、解析估價單回應、以及組裝 Payload 的服務實作。
    /// HttpClient 由 IHttpClientFactory 統一管理，可透過 DI 注入並支援單元測試 Mock。
    /// </summary>
    public class CoolPcService
    {
        private static readonly string CoolPcBase = "https://www.coolpc.com.tw";
        private readonly HttpClient _httpClient;
        private readonly Encoding _big5Encoding;

        /// <summary>
        /// 建構子：由 IHttpClientFactory（AddHttpClient）注入已設定標頭的 HttpClient。
        /// Big5 編碼在首次建立時一次性初始化。
        /// </summary>
        public CoolPcService(HttpClient httpClient)
        {
            _httpClient = httpClient;

            // 確保 Big5 編碼提供者已載入（IHttpClientFactory 情境下可能由 MauiProgram 預先呼叫，此處保險起見再呼叫一次）
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try
            {
                _big5Encoding = Encoding.GetEncoding("big5");
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[嚴重] 無法載入 'big5' 編碼: {e.Message}");
                _big5Encoding = Encoding.Default;
            }
        }

        /// <inheritdoc />
        public async Task<string?> GetSessionIdAsync()
        {
            var url = $"{CoolPcBase}/evaluate.php";
            try
            {
                Debug.WriteLine($"正在向 {url} 發送 GET 請求...");
                var response = await _httpClient.GetAsync(url);
                Debug.WriteLine($"收到回應狀態碼: {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
                {
                    foreach (var header in cookieHeaders)
                    {
                        if (header.StartsWith("PHPSESSID="))
                        {
                            var sessionId = header.Split(';').First().Split('=')[1];
                            Debug.WriteLine($"成功提取 PHPSESSID: {sessionId}");
                            return sessionId;
                        }
                    }
                    Debug.WriteLine("Set-Cookie 標頭中未找到 PHPSESSID。");
                }
                else
                {
                    Debug.WriteLine("回應中未找到 Set-Cookie 標頭。");
                }

                return null;
            }
            catch (HttpRequestException e)
            {
                Debug.WriteLine($"網路請求錯誤: {e.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"取得 Session 時發生未預期錯誤: {ex.Message}");
                return null;
            }
        }

        /// <inheritdoc />
        public string BuildPayload(List<MenuProduct> products)
        {
            var sb = new StringBuilder();
            sb.Append("<table cellspacing=1 bgcolor=black>");
            sb.Append("<tr bgcolor=#cceeee><td>品 名<td>產 品 名 稱<td>備 註<td>數量<td>小 計</tr>");

            foreach (var product in products)
            {
                sb.Append("<tr bgcolor=#eeeecc>");
                sb.Append($"<td>{product.CategoryName}<td>{product.ProductFullText}<td><div bis_skin_checked=\"1\">{product.SubcategoryName}</div><td>{product.Qty}<td><div bis_skin_checked=\"1\">{product.ProductPrice * product.Qty}</div>");
            }

            int total = products.Sum(x => x.ProductPrice * x.Qty);
            sb.Append($"<tr bgcolor=gold><td colspan=5 align=right>TDP耗電X瓦 　含稅 現金價：{total}");
            sb.Append("</table>");

            return sb.ToString();
        }

        /// <inheritdoc />
        public async Task<(string? HtmFilename, string? PngFilename)?> SendAndParseEstimateAsync(
            string sessionId, Dictionary<string, string> payload)
        {
            var htmlResponse = await SendEstimateInternalAsync(sessionId, payload);
            if (string.IsNullOrEmpty(htmlResponse))
            {
                Debug.WriteLine("SendEstimateInternalAsync 未返回有效的 HTML 內容。");
                return null;
            }

            return ParseEstimateResult(htmlResponse);
        }

        /// <summary>
        /// 向 eval-save.php 發送 Big5 編碼的 POST 請求，回傳原始 HTML 字串。
        /// </summary>
        private async Task<string?> SendEstimateInternalAsync(string sessionId, Dictionary<string, string> payload)
        {
            if (_big5Encoding.EncodingName == Encoding.Default.EncodingName)
            {
                Debug.WriteLine("Big5 編碼不可用，已終止請求。");
                return null;
            }

            var url = $"{CoolPcBase}/eval-save.php";
            try
            {
                // 替換特殊換行字元並以 Big5 URL 編碼 Payload
                var encodedPairs = payload
                    .Select(kv => new
                    {
                        Key = HttpUtility.UrlEncode(kv.Key, _big5Encoding),
                        Value = HttpUtility.UrlEncode(
                            (kv.Value ?? string.Empty).Replace('\u21a9', '\n'),
                            _big5Encoding)
                    })
                    .Select(kv => $"{kv.Key}={kv.Value}");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
                requestMessage.Headers.Add("Accept-Language", "zh-TW,zh;q=0.9");
                requestMessage.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                requestMessage.Headers.Referrer = new Uri($"{CoolPcBase}/eval-save.php");
                requestMessage.Headers.Add("Origin", CoolPcBase);
                requestMessage.Headers.Add("Cookie", $"PHPSESSID={sessionId}");

                requestMessage.Content = new StringContent(string.Join("&", encodedPairs), _big5Encoding);
                requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
                {
                    CharSet = "big5"
                };

                Debug.WriteLine($"正在向 {url} 發送 POST 請求...");
                var response = await _httpClient.SendAsync(requestMessage);
                Debug.WriteLine($"收到回應狀態碼: {response.StatusCode}");
                response.EnsureSuccessStatusCode();

                // 解碼回應（強制使用 Big5，無論伺服器宣告何種 charset）
                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                return _big5Encoding.GetString(responseBytes);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"發送估價單時發生錯誤: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 從 eval-save.php 回傳的 HTML 中，以 Regex 提取 HTM 與 PNG 的檔名。
        /// </summary>
        private static (string? HtmFilename, string? PngFilename)? ParseEstimateResult(string htmlContent)
        {
            string? htmFilename = null;
            string? pngFilename = null;

            try
            {
                var fnameMatch = Regex.Match(htmlContent,
                    @"f\.fname\.value\s*=\s*.*?\/tmp\/'\s*\+\s*'([^']+)'",
                    RegexOptions.IgnoreCase);
                if (fnameMatch.Success)
                {
                    htmFilename = fnameMatch.Groups[1].Value;
                    Debug.WriteLine($"成功解析 HTM 檔名: {htmFilename}");
                }

                var inameMatch = Regex.Match(htmlContent,
                    @"f\.iname\.value\s*=\s*.*?\/tmp\/'\s*\+\s*'([^']+)'",
                    RegexOptions.IgnoreCase);
                if (inameMatch.Success)
                {
                    pngFilename = inameMatch.Groups[1].Value;
                    Debug.WriteLine($"成功解析 PNG 檔名: {pngFilename}");
                }

                if (htmFilename != null || pngFilename != null)
                    return (htmFilename, pngFilename);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析估價單回應 HTML 時發生錯誤: {ex.Message}");
            }

            Debug.WriteLine("ParseEstimateResult 解析完成，未找到任何檔名。");
            return null;
        }
    }
}
