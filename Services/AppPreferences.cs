using System.IO;
using System.Text.Json;

namespace PCBuilder.Services
{
    /// <summary>
    /// 以 JSON 檔實作簡易 key-value 偏好設定儲存，取代 MAUI 的 Preferences 靜態類別。
    /// 對應 MAUI Preferences 的 WPF 替代方案：以 JSON 檔存於 LocalAppData。
    /// </summary>
    public class AppPreferences
    {
        private readonly string _filePath;
        private readonly Dictionary<string, JsonElement> _data;

        public AppPreferences()
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            _filePath = AppPaths.PreferencesPath;
            _data = Load();
        }

        private Dictionary<string, JsonElement> Load()
        {
            if (!File.Exists(_filePath))
                return [];

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_data);
            File.WriteAllText(_filePath, json);
        }

        public int? GetInt(string key) =>
            _data.TryGetValue(key, out var value) && value.TryGetInt32(out var i) ? i : null;

        public void SetInt(string key, int value)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }

        public string? GetString(string key) =>
            _data.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        public void SetString(string key, string value)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }

        public bool GetBool(string key, bool defaultValue = false) =>
            _data.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : defaultValue;

        public void SetBool(string key, bool value)
        {
            _data[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }

        public void Remove(string key)
        {
            if (_data.Remove(key))
                Save();
        }

        public void Clear()
        {
            _data.Clear();
            Save();
        }
    }
}
