namespace Toto.App;

internal sealed class AppPaths
{
    public string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toto");
    public string DatabasePath => Path.Combine(DataDirectory, "toto.db");
    public string ConfigPath => Path.Combine(DataDirectory, "config.ini");
    public string ActiveCsvPath => Path.Combine(DataDirectory, "toto_ing.csv");
    public string HistoryCsvPath => Path.Combine(DataDirectory, "toto_end.csv");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");
    public string BackupDirectory => Path.Combine(DataDirectory, "legacy_backup");
}
