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
        var page = SettingsViewFactory.Page();
        var card = SettingsViewFactory.Group(page, "当前版本");
        SettingsViewFactory.Row(card, "juju Toolbox", "版本 " + version,
            new System.Windows.Controls.TextBlock { Text = "Windows x64", FontSize = 11,
                Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ToolboxMuted"] });
        View = page;
    }

    public Task SaveAsync() => Task.CompletedTask;
}
