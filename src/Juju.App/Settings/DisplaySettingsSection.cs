using System.Windows;
using System.Windows.Controls;
using Juju.App.Bootstrap;
using Juju.App.Views;
using Juju.Core.Settings;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Juju.App.Settings;

/// <summary>外观偏好作用于 WPF 主题、Monaco 编辑器和 Launcher 行密度。</summary>
public sealed class DisplaySettingsSection : ISettingsSection
{
    private readonly ISettingsService _settings;
    private readonly ThemeService _themes;
    private readonly ComboBox _theme = new() { MinWidth = 145, Height = 32 };
    private readonly ComboBox _density = new() { MinWidth = 145, Height = 32 };
    public string Id => "display";
    public string DisplayName => "显示设置";
    public FrameworkElement View { get; }

    public DisplaySettingsSection(ISettingsService settings, ThemeService themes)
    {
        _settings = settings;
        _themes = themes;
        var page = SettingsViewFactory.Page("APPLICATION / DISPLAY", "显示设置",
            "调整应用主题和工具入口列表的密度。窗口位置会独立记忆。");
        var card = SettingsViewFactory.Group(page, "外观与布局");
        _theme.Items.Add("跟随系统"); _theme.Items.Add("浅色"); _theme.Items.Add("深色");
        _theme.SelectedIndex = (int)settings.Current.Theme;
        SettingsViewFactory.Row(card, "应用主题", "影响宿主主题资源与 JSON 编辑器。", _theme);
        _density.Items.Add("舒适"); _density.Items.Add("紧凑");
        _density.SelectedIndex = settings.Current.CompactLauncher ? 1 : 0;
        SettingsViewFactory.Row(card, "工具列表密度", "控制 Launcher 中每个工具入口的行高。", _density);
        SettingsViewFactory.Note(page, "窗口位置与大小会在正常退出时保存；下次启动时重新还原。");
        View = page;
    }

    public async Task SaveAsync()
    {
        var value = _settings.Current with
        {
            Theme = (ThemePreference)Math.Max(0, _theme.SelectedIndex),
            CompactLauncher = _density.SelectedIndex == 1
        };
        await _settings.SaveAsync(value);
        _themes.Apply(value.Theme);
        System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault()?.ApplySettings(value);
    }
}
