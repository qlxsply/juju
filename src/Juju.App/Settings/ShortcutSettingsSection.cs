using System.Windows;
using Juju.App.Views;
using Juju.Core.Settings;
using Toto.App;

namespace Juju.App.Settings;

/// <summary>把原型的应用热键与各工具直达热键放在一页，实际注册由 Launcher 的 Win32 HWND 承担。</summary>
public sealed class ShortcutSettingsSection : ISettingsSection
{
    private readonly ISettingsService _settings;
    private readonly System.Windows.Controls.TextBox _launcher;
    private readonly System.Windows.Controls.TextBox _json;
    public string Id => "shortcut";
    public string DisplayName => "快捷键";
    public FrameworkElement View { get; }

    public ShortcutSettingsSection(ISettingsService settings, TotoToolHost toto)
    {
        _settings = settings;
        var page = SettingsViewFactory.Page("APPLICATION / SHORTCUT", "快捷键",
            "入口热键显示工具列表；工具专属热键无需经过入口，可直接打开对应窗口。");
        var entry = SettingsViewFactory.Group(page, "工具箱热键");
        _launcher = SettingsViewFactory.Input(settings.Current.LeaderShortcut);
        SettingsViewFactory.Row(entry, "唤出 / 收起工具入口", "全局有效，默认 Ctrl+Shift+Alt+Space。", _launcher);
        var tools = SettingsViewFactory.Group(page, "工具直达热键");
        _json = SettingsViewFactory.Input(settings.Current.JsonShortcut);
        SettingsViewFactory.Row(tools, "JSON 格式化", "直接打开 JSON 编辑窗口，默认 Ctrl+Alt+J。", _json);
        SettingsViewFactory.Row(tools, "Toto 日程提醒", "当前 " + toto.Hotkey + "；在 Toto 设置中修改。",
            SettingsViewFactory.SoftButton("打开 Toto 设置", (_, _) => toto.ShowSettings()));
        SettingsViewFactory.Note(page,
            "工具列表可见且未打开设置时，按 J / T 可快速启动 JSON / Toto；单键不会在其他窗口全局生效。");
        View = page;
    }

    public async Task SaveAsync()
    {
        var previous = _settings.Current;
        var value = previous with { LeaderShortcut = _launcher.Text.Trim(), JsonShortcut = _json.Text.Trim() };
        var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
        // Win32 注册成功后才落盘；避免用户输入被占用的组合键后重启丢失旧热键。
        if (launcher is not null && !launcher.ApplySettings(value))
        {
            launcher.ApplySettings(previous);
            throw new InvalidOperationException("热键格式无效或已被其他程序占用。");
        }
        try { await _settings.SaveAsync(value); }
        catch
        {
            launcher?.ApplySettings(previous);
            throw;
        }
    }
}
