using System.Windows;
using Juju.App.Bootstrap;
using Juju.Core.Settings;
using Juju.Platform.Windows.Startup;

namespace Juju.App.Settings;

public sealed class SystemSettingsSection : ISettingsSection
{
    private readonly System.Windows.Controls.CheckBox _startAtLogin = new() { Content = "开机自动启动" };
    private readonly System.Windows.Controls.TextBox _shortcut = new();
    private readonly System.Windows.Controls.TextBox _timeout = new();
    private readonly System.Windows.Controls.ComboBox _theme = new();
    public string Id => "system";
    public string DisplayName => "系统";
    public FrameworkElement View { get; } = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

    private readonly ISettingsService _settings;
    private readonly ThemeService _themes;
    private readonly IStartupService _startup;

    public async Task SaveAsync()
    {
        var value = _settings.Current with
        {
            StartAtLogin = _startAtLogin.IsChecked == true,
            LeaderShortcut = _shortcut.Text.Trim(),
            LauncherTimeoutMilliseconds = ReadNumber(_timeout, 250, 30000),
            Theme = (ThemePreference)Math.Max(0, _theme.SelectedIndex)
        };
        _startup.SetEnabled(value.StartAtLogin);
        await _settings.SaveAsync(value);
        _themes.Apply(value.Theme);
    }

    public SystemSettingsSection(ISettingsService settings, ThemeService themes, IStartupService startup)
    {
        _settings = settings;
        _themes = themes;
        _startup = startup;
        var panel = (System.Windows.Controls.StackPanel)View;
        _theme.Items.Add("跟随系统");
        _theme.Items.Add("浅色");
        _theme.Items.Add("深色");
        panel.Children.Add(_startAtLogin);
        AddField(panel, "全局快捷键", _shortcut);
        AddField(panel, "Launcher 超时（毫秒）", _timeout);
        AddField(panel, "主题", _theme);
        var current = _settings.Current;
        _startAtLogin.IsChecked = current.StartAtLogin;
        _shortcut.Text = current.LeaderShortcut;
        _timeout.Text = current.LauncherTimeoutMilliseconds.ToString();
        _theme.SelectedIndex = (int)current.Theme;
    }

    private static void AddField(System.Windows.Controls.Panel panel, string label,
        System.Windows.Controls.Control control)
    {
        panel.Children.Add(new System.Windows.Controls.TextBlock { Margin = new Thickness(0, 14, 0, 3), Text = label });
        panel.Children.Add(control);
    }

    private static int ReadNumber(System.Windows.Controls.TextBox box, int minimum, int maximum) =>
        int.TryParse(box.Text, out var value) && value >= minimum && value <= maximum
            ? value
            : throw new InvalidOperationException($"{box.Text} 不是有效范围内的数字。");
}