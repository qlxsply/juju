namespace Juju.Core.Settings;

public enum ThemePreference { System, Light, Dark }

public sealed record AppSettings(
    string DataRoot,
    bool StartAtLogin = false,
    string LeaderShortcut = "Ctrl+Shift+Alt+Space",
    int LauncherTimeoutMilliseconds = 2000,
    ThemePreference Theme = ThemePreference.System,
    int AutosaveDelayMilliseconds = 800,
    int JsonIndentSize = 2)
{
    public static AppSettings CreateDefault() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".juju"));
}
