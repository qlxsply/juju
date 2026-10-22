namespace Juju.Core.Storage;

/// <summary>配置文件与工具数据的统一定位；安装目录只保留“数据根目录指针”。</summary>
public static class ApplicationPaths
{
    /// <summary>应用二进制的基目录。</summary>
    public static string InstallDirectory => AppContext.BaseDirectory;
    private static IReadOnlyDictionary<string, string> _toolDirectories = new Dictionary<string, string>();
    private static readonly string StartupDataRoot = ReadDataRoot();

    /// <summary>安装目录中的引导指针只记录数据根目录，不保存任何应用设置。</summary>
    public static string DataRootPointerFile => Path.Combine(InstallDirectory, "data-location.json");

    /// <summary>宿主级配置文件路径。</summary>
    public static string ConfigurationFile => Path.Combine(DataRoot, "config.json");

    /// <summary>默认数据根目录路径。</summary>
    public static string DataRoot => StartupDataRoot;

    public static string DefaultDataRoot => Path.Combine(InstallDirectory, "data");

    /// <summary>指定工具在数据根目录下的专属目录。</summary>
    public static string ToolDataDirectory(string toolName) =>
        _toolDirectories.TryGetValue(toolName, out var path) ? path : Path.Combine(DataRoot, "tools", toolName);

    /// <summary>加载 config.json 后调用一次；修改目录需重启才改变当前服务持有的路径。</summary>
    public static void ConfigureToolDirectories(IReadOnlyDictionary<string, string>? directories) =>
        _toolDirectories = (directories ?? new Dictionary<string, string>())
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => Path.GetFullPath(item.Value), StringComparer.OrdinalIgnoreCase);

    private static string ReadDataRoot()
    {
        var pointer = Path.Combine(InstallDirectory, "data-location.json");
        if (!File.Exists(pointer)) return DefaultDataRoot;
        var location = System.Text.Json.JsonSerializer.Deserialize<DataRootPointer>(File.ReadAllText(pointer));
        if (string.IsNullOrWhiteSpace(location?.DataRoot) || !Path.IsPathFullyQualified(location.DataRoot))
            throw new InvalidDataException("数据目录引导文件无效：" + pointer);
        return Path.GetFullPath(location.DataRoot);
    }

    public sealed record DataRootPointer(string DataRoot);

    /// <summary>应用自身（非工具）的持久化目录。</summary>
    public static string ApplicationDataDirectory => Path.Combine(DataRoot, "app");
}
