namespace Toto.App.Services;

/// <summary>管理当前用户启动文件夹中的 Windows 快捷方式。</summary>
internal sealed class StartupService
{
    private static readonly string ShortcutPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "toto.lnk");

    /// <summary>启用或禁用开机自启动；操作失败时返回 <see langword="false"/> 而不向调用方抛出异常。</summary>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                return true;
            }

            var shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell is null) return false;
            dynamic instance = Activator.CreateInstance(shell)!;
            var shortcut = instance.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = Application.ExecutablePath;
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.Description = "toto 待办提醒工具";
            shortcut.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
