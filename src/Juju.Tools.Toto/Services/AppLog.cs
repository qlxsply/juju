namespace Toto.App.Services;

/// <summary>向按月划分的文本文件追加应用诊断信息。</summary>
/// <remarks>主构造函数参数 <c>directory</c> 可在实例成员中直接使用。</remarks>
internal sealed class AppLog(string directory)
{
    /// <summary>追加一条带本地时间戳的日志；日志写入失败会被忽略以避免影响主功能。</summary>
    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"toto-{DateTime.Now:yyyyMM}.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignored
        }
    }
}
