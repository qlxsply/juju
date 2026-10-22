using System.Windows;
using Juju.App.Views;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;
using Toto.App;

namespace Juju.App.Settings;

/// <summary>应用级页面只配置工具箱入口；工具直达键由各工具设置页分别管理。</summary>
public sealed class ShortcutSettingsSection : ISettingsSection
{
    private readonly ISettingsService _settings;
    private readonly SettingsDialogService _dialogs;
    private readonly IJsonToolSettingsService _json;
    private readonly TotoToolHost _toto;
    private readonly Action<string> _render;
    public string Id => "shortcut";
    public string DisplayName => "快捷键";
    public FrameworkElement View { get; }

    public ShortcutSettingsSection(ISettingsService settings, SettingsDialogService dialogs,
        IJsonToolSettingsService json, TotoToolHost toto)
    {
        _settings = settings;
        _dialogs = dialogs;
        _json = json;
        _toto = toto;
        var page = SettingsViewFactory.Page();
        _render = SettingsViewFactory.ShortcutGroup(page, "全局快捷键",
            [("launcher", "显示 / 隐藏主界面", settings.Current.LeaderShortcut, (Action)EditLauncherHotkey)])[
            "launcher"];
        View = page;
    }

    private void EditLauncherHotkey() => _dialogs.EditShortcut("编辑快捷键 · 显示 / 隐藏主界面",
        _settings.Current.LeaderShortcut, allowNone: false, allowSingle: false, allowSequence: false,
        validate: value =>
        {
            try
            {
                GlobalHotkeyPolicy.EnsureDistinct(value,
                    new[] { _json.Current.GlobalShortcut, _toto.Hotkey }
                        .Concat(_settings.Current.ToolGlobalShortcuts.Values).ToArray());
                return null;
            }
            catch (InvalidOperationException ex) { return ex.Message; }
        }, commit: SaveLauncherHotkeyAsync, defaultValue: "Ctrl+Alt+Space");

    private async Task SaveLauncherHotkeyAsync(string shortcut)
    {
        var previous = _settings.Current;
        var value = previous with { LeaderShortcut = GlobalHotkeyPolicy.Normalize(shortcut) };
        var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
        // Win32 注册成功后才落盘；避免用户输入被占用的组合键后重启丢失旧热键。
        if (launcher is not null && !launcher.ApplySettings(value))
        {
            launcher.ApplySettings(previous);
            throw new InvalidOperationException("热键格式无效或已被其他程序占用");
        }
        try { await _settings.SaveAsync(value); }
        catch
        {
            launcher?.ApplySettings(previous);
            throw;
        }
        _render(value.LeaderShortcut);
    }

    public Task SaveAsync() => Task.CompletedTask;
}
