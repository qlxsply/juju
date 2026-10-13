using System.Windows;
using Juju.App.Bootstrap;
using Juju.Core.Settings;
using Juju.Platform.Windows.Startup;
using Juju.App.Views;

namespace Juju.App.Settings;

// 设置节由 DI 作为 ISettingsSection 的一个实现收集，并在 SettingsWindow 中动态组合。
public sealed class SystemSettingsSection : ISettingsSection
{
    private readonly System.Windows.Controls.CheckBox _startAtLogin = new() { Content = "开机自动启动" };
    private readonly System.Windows.Controls.TextBox _shortcut = new();
    private readonly System.Windows.Controls.TextBox _jsonShortcut = new();
    private readonly System.Windows.Controls.TextBox _timeout = new();
    private readonly System.Windows.Controls.ComboBox _theme = new();
    public string Id => "system";
    public string DisplayName => "系统";
    public FrameworkElement View { get; } = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

    private readonly ISettingsService _settings;
    private readonly ThemeService _themes;
    private readonly IStartupService _startup;

    // with 为 record 的非破坏性复制：保留未编辑字段，只创建包含新值的 AppSettings 实例。
    public async Task SaveAsync()
    {
        var previous = _settings.Current;
        var value = previous with
        {
            StartAtLogin = _startAtLogin.IsChecked == true,
            LeaderShortcut = _shortcut.Text.Trim(),
            JsonShortcut = _jsonShortcut.Text.Trim(),
            LauncherTimeoutMilliseconds = ReadNumber(_timeout, 250, 30000),
            Theme = (ThemePreference)Math.Max(0, _theme.SelectedIndex)
        };
        var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
        // 先向 Windows 注册候选组合键；失败时保持当前配置和旧的全局热键。
        if (launcher is not null && !launcher.ApplySettings(value))
        {
            launcher.ApplySettings(previous);
            throw new InvalidOperationException("全局快捷键无效或已被占用，设置未保存。");
        }
        try
        {
            _startup.SetEnabled(value.StartAtLogin);
            await _settings.SaveAsync(value);
            _themes.Apply(value.Theme);
        }
        catch
        {
            launcher?.ApplySettings(previous);
            throw;
        }
    }

    // 构造函数注入使此 UI 适配器依赖抽象服务，而不是自行创建配置、主题或注册表访问对象。
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
        AddField(panel, "JSON 工具全局快捷键", _jsonShortcut);
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = "Toto 的全局快捷键在 Toto 工具自己的设置窗口中修改。"
        });
        AddField(panel, "Launcher 超时（毫秒）", _timeout);
        AddField(panel, "主题", _theme);
        var current = _settings.Current;
        _startAtLogin.IsChecked = current.StartAtLogin;
        _shortcut.Text = current.LeaderShortcut;
        _jsonShortcut.Text = current.JsonShortcut;
        _timeout.Text = current.LauncherTimeoutMilliseconds.ToString();
        _theme.SelectedIndex = (int)current.Theme;
    }

    // 此页面以代码生成控件而非 XAML；仍使用标准 WPF 视觉树与布局容器。
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
