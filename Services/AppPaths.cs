using System.IO;

namespace PCBuilder.Services
{
    /// <summary>
    /// PCBuilder 的本機資料位置（%LocalAppData%\PCBuilder）：資料庫、偏好設定、AI 對話紀錄、錯誤記錄。
    /// </summary>
    public static class AppPaths
    {
        public static string DataFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCBuilder");

        public static string DbPath => Path.Combine(DataFolder, "PCBuilder.db3");

        public static string PreferencesPath => Path.Combine(DataFolder, "preferences.json");

        public static string AiChatPath => Path.Combine(DataFolder, "ai-chat.json");

        public static string LogPath => Path.Combine(DataFolder, "PCBuilder.log");
    }
}
