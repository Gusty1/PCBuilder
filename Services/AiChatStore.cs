using PCBuilder.Models;
using System.IO;
using System.Text.Json;

namespace PCBuilder.Services
{
    /// <summary>
    /// 目前這段 AI 對話存成 %LocalAppData%\PCBuilder\ai-chat.json，重開 App 可以接著聊；按「新對話」時刪除。
    /// 讀寫失敗只寫記錄檔：對話紀錄不見不影響使用，不必打斷使用者。
    /// </summary>
    public static class AiChatStore
    {
        public static SavedChat? Load()
        {
            try
            {
                return File.Exists(AppPaths.AiChatPath)
                    ? JsonSerializer.Deserialize<SavedChat>(File.ReadAllText(AppPaths.AiChatPath))
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Error("讀取 AI 對話紀錄失敗", ex);
                return null;
            }
        }

        public static void Save(SavedChat chat)
        {
            try
            {
                AtomicFile.WriteAllText(AppPaths.AiChatPath, JsonSerializer.Serialize(chat));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("儲存 AI 對話紀錄失敗", ex);
            }
        }

        public static void Delete()
        {
            try
            {
                File.Delete(AppPaths.AiChatPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("刪除 AI 對話紀錄失敗", ex);
            }
        }
    }
}
