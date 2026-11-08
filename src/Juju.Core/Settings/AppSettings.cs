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
    string LeaderShortcut = "Ctrl+Alt+Space",
    int LauncherTimeoutMilliseconds = 2000,
    ThemePreference Theme = ThemePreference.System,
    bool StartMinimized = false,
    bool ShowToolNotifications = true,
    bool AutoHideLauncher = true,
    bool ReturnToLauncherOnToolClose = false,
    bool CompactLauncher = false)
{
    /// <summary>Launcher 内每个工具独有的单键，旧配置缺少时使用编译期常量的默认值。</summary>
    public Dictionary<string, string> ToolKeys { get; init; } = Juju.Core.Tools.ToolLaunchKeys.Defaults();
    /// <summary>尚未启用专属配置文件的占位工具的可选直达热键，默认均为 None。</summary>
    public Dictionary<string, string> ToolGlobalShortcuts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>已保存但待应用的数据根路径；正常关机时只更新安装目录中的根目录指针。</summary>
    public string? PendingDataRoot { get; init; }
    /// <summary>按构造函数默认值创建完整设置。</summary>
    public static AppSettings CreateDefault() => new();
}
