namespace Juju.Core.Settings;

/// <summary>界面主题选择；<see cref="System"/> 表示交由操作系统主题决定。</summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// 宿主应用的不可变设置快照。带默认值的主构造函数兼作反序列化模型；语义接近 Java record，
/// 修改单项时可使用 <c>with</c> 创建副本而非修改原对象。
/// </summary>
public sealed record AppSettings(
    bool StartAtLogin = false,
    string LeaderShortcut = "Ctrl+Shift+Alt+Space",
    int LauncherTimeoutMilliseconds = 2000,
    ThemePreference Theme = ThemePreference.System)
{
    /// <summary>按构造函数默认值创建完整设置。</summary>
    public static AppSettings CreateDefault() => new();
}