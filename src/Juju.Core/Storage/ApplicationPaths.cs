namespace Juju.Core.Storage;

/// <summary>集中计算应用安装目录下的约定路径；静态类不能实例化，类似 Java 的工具类。</summary>
public static class ApplicationPaths
{
    /// <summary>应用二进制的基目录。</summary>
    private static string InstallDirectory => AppContext.BaseDirectory;

    /// <summary>宿主级配置文件路径。</summary>
    public static string ConfigurationFile => Path.Combine(InstallDirectory, "config.json");

    /// <summary>默认数据根目录路径。</summary>
    public static string DataRoot => Path.Combine(InstallDirectory, "data");

    /// <summary>指定工具在数据根目录下的专属目录。</summary>
    public static string ToolDataDirectory(string toolName) => Path.Combine(DataRoot, toolName);

    /// <summary>应用自身（非工具）的持久化目录。</summary>
    public static string ApplicationDataDirectory => Path.Combine(DataRoot, "app");
}