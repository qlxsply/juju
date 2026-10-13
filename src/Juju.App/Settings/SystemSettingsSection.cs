using System.Windows;
using Juju.App.Bootstrap;
using Juju.App.Views;
using Juju.Core.Settings;
using Juju.Platform.Windows.Startup;

namespace Juju.App.Settings;

/// <summary>原型“应用设置 / 常用设置”对应的真实宿主行为，而不是仅改变 UI 的演示开关。</summary>
public sealed class SystemSettingsSection : ISettingsSection
{
    private readonly ISettingsService _settings;
    private readonly IStartupService _startup;
    private readonly System.Windows.Controls.CheckBox _startAtLogin;
    private readonly System.Windows.Controls.CheckBox _startMinimized;
    private readonly System.Windows.Controls.CheckBox _notifications;
    private readonly System.Windows.Controls.CheckBox _autoHide;
    private readonly System.Windows.Controls.CheckBox _returnAfterClose;
    private readonly System.Windows.Controls.TextBox _timeout;

    public string Id => "system";
    public string DisplayName => "常用设置";
    public FrameworkElement View { get; }

    public SystemSettingsSection(ISettingsService settings, IStartupService startup)
    {
        _settings = settings;
        _startup = startup;
        var current = settings.Current;
        var page = SettingsViewFactory.Page("APPLICATION / GENERAL", "常用设置",
            "配置工具箱的启动与驻留行为。这里的选项只影响 juju 主程序。");
        var startupCard = SettingsViewFactory.Group(page, "启动与关闭");
        _startMinimized = SettingsViewFactory.Switch("启动时仅显示托盘图标", current.StartMinimized);
        SettingsViewFactory.Row(startupCard, "启动时仅显示托盘图标", "关闭后，下次启动将显示工具入口。", _startMinimized);
        _startAtLogin = SettingsViewFactory.Switch("登录 Windows 后自动启动", current.StartAtLogin);
        SettingsViewFactory.Row(startupCard, "登录 Windows 后自动启动", "只创建 Juju 的启动项，Toto 不再单独启动。", _startAtLogin);
        _notifications = SettingsViewFactory.Switch("显示工具状态提示", current.ShowToolNotifications);
        SettingsViewFactory.Row(startupCard, "显示工具状态提示", "JSON 或 Toto 隐藏到后台时显示托盘提示。", _notifications);
        _autoHide = SettingsViewFactory.Switch("点击入口以外区域时自动隐藏", current.AutoHideLauncher);
        SettingsViewFactory.Row(startupCard, "点击工具入口以外的区域时自动隐藏", "不会退出 Juju 或停止 Toto 提醒。", _autoHide);

        var behavior = SettingsViewFactory.Group(page, "窗口行为");
        _returnAfterClose = SettingsViewFactory.Switch("工具关闭后重新显示入口", current.ReturnToLauncherOnToolClose);
        SettingsViewFactory.Row(behavior, "工具窗口关闭后自动重新显示入口", "JSON/Toto 窗口隐藏后回到工具列表。", _returnAfterClose);
        _timeout = SettingsViewFactory.Input(current.LauncherTimeoutMilliseconds.ToString(), 100);
        SettingsViewFactory.Row(behavior, "Launcher 组合键超时（毫秒）", "按住唤醒组合键时等待修饰键松开的最长时间。", _timeout);
        SettingsViewFactory.Note(page, "常驻托盘与单实例由 Juju 管理；Toto 到期提醒在主窗口隐藏时仍正常运行。");
        View = page;
    }

    public async Task SaveAsync()
    {
        if (!int.TryParse(_timeout.Text, out var timeout) || timeout is < 250 or > 30000)
            throw new InvalidOperationException("Launcher 超时必须在 250 到 30000 毫秒之间。");
        var value = _settings.Current with
        {
            StartAtLogin = _startAtLogin.IsChecked == true,
            StartMinimized = _startMinimized.IsChecked == true,
            ShowToolNotifications = _notifications.IsChecked == true,
            AutoHideLauncher = _autoHide.IsChecked == true,
            ReturnToLauncherOnToolClose = _returnAfterClose.IsChecked == true,
            LauncherTimeoutMilliseconds = timeout
        };
        _startup.SetEnabled(value.StartAtLogin);
        await _settings.SaveAsync(value);
        System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault()?.ApplySettings(value);
    }
}
