namespace Toto.App;

/// <summary>
/// 集中计算应用程序在当前用户主目录下使用的文件系统路径。
/// 对 Java 开发者而言，它类似于保存应用配置目录的不可继承值对象；<c>sealed</c> 禁止继承。
/// </summary>
internal sealed class AppPaths
{
    /// <summary>获取 toto 数据、配置和日志的根目录。</summary>
    public string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toto");

    /// <summary>获取 INI 格式配置文件的完整路径。</summary>
    public string ConfigPath => Path.Combine(DataDirectory, "config.ini");
    /// <summary>获取进行中事项 CSV 文件的完整路径。</summary>
    public string ActiveCsvPath => Path.Combine(DataDirectory, "toto_ing.csv");
    /// <summary>获取历史事项 CSV 文件的完整路径。</summary>
    public string HistoryCsvPath => Path.Combine(DataDirectory, "toto_end.csv");
    /// <summary>获取日志目录的完整路径。</summary>
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    /// <summary>返回指定年份节假日 JSON 文件的完整路径。</summary>
    /// <param name="year">四位年份。</param>
    /// <returns>该年份日历缓存文件的路径。</returns>
    public string CalendarPath(int year) => Path.Combine(DataDirectory, $"{year:D4}.json");
}
