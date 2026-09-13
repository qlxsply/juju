namespace Juju.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public sealed record AppSettings(
    bool StartAtLogin = false,
    string LeaderShortcut = "Ctrl+Shift+Alt+Space",
    int LauncherTimeoutMilliseconds = 2000,
    ThemePreference Theme = ThemePreference.System)
{
    public static AppSettings CreateDefault() => new();
}