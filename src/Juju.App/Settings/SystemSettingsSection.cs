using System.Windows;
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
        var page = SettingsViewFactory.Page();
        var startupCard = SettingsViewFactory.Group(page, "启动与关闭");
        _startMinimized = SettingsViewFactory.Switch("启动时仅显示托盘图标", current.StartMinimized);
        SettingsViewFactory.Row(startupCard, "启动时仅显示托盘图标", "关闭后，下次启动将显示工具入口", _startMinimized);
        _startAtLogin = SettingsViewFactory.Switch("开机自动启动", current.StartAtLogin);
        SettingsViewFactory.Row(startupCard, "开机自动启动", "", _startAtLogin);
        _notifications = SettingsViewFactory.Switch("显示工具状态提示", current.ShowToolNotifications);
        SettingsViewFactory.Row(startupCard, "显示工具状态提示", "工具隐藏到后台时显示托盘提示", _notifications);
        _autoHide = SettingsViewFactory.Switch("点击入口以外区域时自动隐藏", current.AutoHideLauncher);
        SettingsViewFactory.Row(startupCard, "点击空白区域时自动隐藏应用", "", _autoHide);

        var behavior = SettingsViewFactory.Group(page, "窗口行为");
        _returnAfterClose = SettingsViewFactory.Switch("工具关闭后重新显示入口", current.ReturnToLauncherOnToolClose);
        SettingsViewFactory.Row(behavior, "工具关闭后重新显示入口", "", _returnAfterClose);
        _timeout = SettingsViewFactory.Input(current.LauncherTimeoutMilliseconds.ToString(), 100);
        SettingsViewFactory.Row(behavior, "主界面组合键超时（毫秒）", "按住唤醒组合键时等待修饰键松开的最长时间", _timeout);
        View = page;
    }

    public async Task SaveAsync()
    {
        if (!int.TryParse(_timeout.Text, out var timeout) || timeout is < 250 or > 30000)
            throw new InvalidOperationException("主界面超时必须在 250 到 30000 毫秒之间");
        var previous = _settings.Current;
        var value = previous with
        {
            StartAtLogin = _startAtLogin.IsChecked == true,
            StartMinimized = _startMinimized.IsChecked == true,
            ShowToolNotifications = _notifications.IsChecked == true,
            AutoHideLauncher = _autoHide.IsChecked == true,
            ReturnToLauncherOnToolClose = _returnAfterClose.IsChecked == true,
            LauncherTimeoutMilliseconds = timeout
        };
        await CommitAsync(previous, value);
    }

    public async Task SaveImmediateAsync(FrameworkElement source)
    {
        var previous = _settings.Current;
        if (ReferenceEquals(source, _timeout))
        {
            if (!int.TryParse(_timeout.Text, out var delay) || delay is < 250 or > 30000)
            {
                _timeout.Text = previous.LauncherTimeoutMilliseconds.ToString();
                throw new InvalidOperationException("主界面超时必须在 250 到 30000 毫秒之间。");
            }
            await CommitAsync(previous, previous with { LauncherTimeoutMilliseconds = delay });
            return;
        }
        var next = source switch
        {
            var control when ReferenceEquals(control, _startAtLogin) => previous with
                { StartAtLogin = _startAtLogin.IsChecked == true },
            var control when ReferenceEquals(control, _startMinimized) => previous with
                { StartMinimized = _startMinimized.IsChecked == true },
            var control when ReferenceEquals(control, _notifications) => previous with
                { ShowToolNotifications = _notifications.IsChecked == true },
            var control when ReferenceEquals(control, _autoHide) => previous with
                { AutoHideLauncher = _autoHide.IsChecked == true },
            var control when ReferenceEquals(control, _returnAfterClose) => previous with
                { ReturnToLauncherOnToolClose = _returnAfterClose.IsChecked == true },
            _ => previous
        };
        try { await CommitAsync(previous, next); }
        catch
        {
            _startAtLogin.IsChecked = previous.StartAtLogin;
            _startMinimized.IsChecked = previous.StartMinimized;
            _notifications.IsChecked = previous.ShowToolNotifications;
            _autoHide.IsChecked = previous.AutoHideLauncher;
            _returnAfterClose.IsChecked = previous.ReturnToLauncherOnToolClose;
            throw;
        }
    }

    private async Task CommitAsync(AppSettings previous, AppSettings next)
    {
        try
        {
            if (previous.StartAtLogin != next.StartAtLogin) _startup.SetEnabled(next.StartAtLogin);
            await _settings.SaveAsync(next);
            System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault()?.ApplySettings(next);
        }
        catch
        {
            if (previous.StartAtLogin != next.StartAtLogin) _startup.SetEnabled(previous.StartAtLogin);
            throw;
        }
    }

}
