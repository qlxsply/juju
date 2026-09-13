using Microsoft.Win32;

namespace Juju.Platform.Windows.Startup;

public interface IStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

public sealed class StartupService : IStartupService
{
    private const string ValueName = "juju";
    private const string KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    public bool IsEnabled() =>
        Registry.CurrentUser.OpenSubKey(KeyPath, writable: false)?.GetValue(ValueName) is not null;

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}