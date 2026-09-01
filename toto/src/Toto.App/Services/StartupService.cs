namespace Toto.App.Services;

internal sealed class StartupService
{
    private static readonly string ShortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "toto.lnk");
    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled) { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); return true; }
            var shell = Type.GetTypeFromProgID("WScript.Shell"); if (shell is null) return false; dynamic instance = Activator.CreateInstance(shell)!; dynamic shortcut = instance.CreateShortcut(ShortcutPath); shortcut.TargetPath = Application.ExecutablePath; shortcut.WorkingDirectory = AppContext.BaseDirectory; shortcut.Description = "toto 待办提醒工具"; shortcut.Save(); return true;
        }
        catch { return false; }
    }
}
