using Microsoft.Win32;

namespace Juju.Platform.Windows.Startup;

/// <summary>管理当前 Windows 用户登录后自动启动的注册表项。</summary>
public interface IStartupService
{
    /// <summary>检查 Run 键中是否存在本应用值。</summary>
    bool IsEnabled();

    /// <summary>创建或删除本应用的 Run 值。</summary>
    void SetEnabled(bool enabled);
}

/// <summary>
/// 基于 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> 的自启动实现。
/// 使用 CurrentUser 不需要管理员权限，并且只影响当前用户。
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string ValueName = "juju";
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>以只读方式打开键；键或值不存在时返回 false。</summary>
    public bool IsEnabled() =>
        Registry.CurrentUser.OpenSubKey(KeyPath, writable: false)?.GetValue(ValueName) is not null;

    /// <summary>
    /// 启用时将可执行路径以引号写入注册表，以处理包含空格的安装路径；禁用时静默删除缺失值。
    /// <c>using var</c> 确保 RegistryKey 句柄及时释放，对应 Java try-with-resources。
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}