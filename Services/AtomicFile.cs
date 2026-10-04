using System.IO;

namespace PCBuilder.Services
{
    /// <summary>
    /// 安全寫入整個檔案：先寫到同資料夾的暫存檔，完成後再一次換掉原檔。
    /// 直接覆寫的話，寫到一半當機或斷電會留下殘缺的檔案，下次啟動讀不到而整份設定被重置。
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents);
            File.Move(tempPath, path, overwrite: true);
        }
    }
}
