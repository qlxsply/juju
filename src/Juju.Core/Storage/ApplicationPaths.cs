namespace Juju.Core.Storage;

public static class ApplicationPaths
{
    public static string InstallDirectory => AppContext.BaseDirectory;
    public static string ConfigurationFile => Path.Combine(InstallDirectory, "config.json");
    public static string DataRoot => Path.Combine(InstallDirectory, "data");
    public static string ToolDataDirectory(string toolName) => Path.Combine(DataRoot, toolName);
    public static string ApplicationDataDirectory => Path.Combine(DataRoot, "app");
}