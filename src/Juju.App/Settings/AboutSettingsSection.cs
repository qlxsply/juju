using System.Windows;

namespace Juju.App.Settings;

/// <summary>版本页保持原型的导航层级，但只显示当前实际支持的信息。</summary>
public sealed class AboutSettingsSection : ISettingsSection
{
    public string Id => "about";
    public string DisplayName => "更新与版本";
    public FrameworkElement View { get; }

    public AboutSettingsSection()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "未知";
        var page = SettingsViewFactory.Page("APPLICATION / UPDATES", "更新与版本",
            "查看当前安装的 Juju 版本及发布位置。");
        var card = SettingsViewFactory.Group(page, "当前版本");
        SettingsViewFactory.Row(card, "juju Toolbox", "版本 " + version,
            new System.Windows.Controls.TextBlock { Text = "Windows x64", FontSize = 11,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ToolboxMuted"] });
        SettingsViewFactory.Note(page, "当前项目没有在线更新服务；发布新版本后请使用安装包更新。");
        View = page;
    }

    public Task SaveAsync() => Task.CompletedTask;
}
