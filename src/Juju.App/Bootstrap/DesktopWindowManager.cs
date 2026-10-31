using Juju.App.Contracts.Windows;
using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.App.Views;
using Juju.Core.Contracts.Platform;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;
using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Reminders;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Bootstrap;

/// <summary>协调 Launcher、设置及 App 生命周期；工具窗口由 Presentation factories 管理。</summary>
public sealed class DesktopWindowManager : IAppWindowManager
{
    private readonly ISettingsService _settings;
    private readonly WindowStateService _windowState;
    private readonly ThemeService _themes;
    private readonly SettingsDialogService _dialogs;
    private readonly IWorkAreaProvider _workAreas;
    private readonly TotoReminderRuntime _totoRuntime;
    private readonly ITotoApplicationService _totoApplication;
    private readonly IJsonToolSettingsService _jsonSettings;
    private readonly IJsonWindowFactory _jsonWindows;
    private readonly ITotoWindowFactory _totoWindows;
    private readonly IServiceProvider _services;
    private LauncherWindow? _launcher;
    private SettingsWindow? _settingsWindow;
    private readonly List<TotoReminderWindow> _reminders = [];
    private bool _shuttingDown;

    public DesktopWindowManager(ISettingsService settings, WindowStateService windowState, ThemeService themes,
        SettingsDialogService dialogs, IWorkAreaProvider workAreas, TotoReminderRuntime totoRuntime,
        ITotoApplicationService totoApplication, IJsonToolSettingsService jsonSettings,
        IJsonWindowFactory jsonWindows, ITotoWindowFactory totoWindows, IServiceProvider services)
    {
        _settings = settings;
        _windowState = windowState;
        _themes = themes;
        _dialogs = dialogs;
        _workAreas = workAreas;
        _totoRuntime = totoRuntime;
        _totoApplication = totoApplication;
        _jsonSettings = jsonSettings;
        _jsonWindows = jsonWindows;
        _totoWindows = totoWindows;
        _services = services;
        _jsonWindows.Hidden += OnJsonHidden;
        _totoWindows.Hidden += OnTotoHidden;
        _totoRuntime.DueReminders += OnDueReminders;
        _totoRuntime.ScheduledPopup += OnScheduledPopup;
    }

    /// <summary>向唯一托盘报告工具隐藏事件，供设置中的状态提示开关控制气泡提示。</summary>
    public event Action<ToolId>? ToolHidden;

    // 启动时先创建并完成 HWND 相关初始化，再隐藏；这样全局热键可在首次唤起前注册。
    public void InitializeLauncher()
    {
        _launcher ??= new LauncherWindow(_services.GetRequiredService<IToolManager>(), this,
            _settings, _themes, _totoApplication, _jsonSettings);
        _launcher.Loaded += (_, _) => _windowState.RestorePosition(_launcher, "launcher");
        _launcher.ApplySettings(_settings.Current);
        _launcher.Show();
        if (_settings.Current.StartMinimized) _launcher.Hide();
    }

    public bool TryRegisterToolHotkey(ToolId tool, string shortcut) =>
        _launcher?.TryRegisterToolHotkey(tool, shortcut) ?? false;

    public void ShowLauncher() => _launcher?.Toggle();

    public void ShowSettings()
    {
        if (_launcher is { IsVisible: false }) _launcher.Toggle();
        _launcher?.SetSettingsOpen(true);
        if (_settingsWindow is null)
        {
            var sections = _services.GetServices<ISettingsSection>()
                .Concat(_services.GetRequiredService<IToolManager>().SettingsSections);
            _settingsWindow = new SettingsWindow(sections, _dialogs);
            var hasSavedPosition = _windowState.HasPlacement("settings");
            _windowState.Restore(_settingsWindow, "settings");
            if (_launcher is not null)
            {
                _settingsWindow.Owner = _launcher;
                var bounds = _workAreas.GetWorkArea(_launcher.Left, _launcher.Top);
                if (!hasSavedPosition)
                {
                    _settingsWindow.Left = Math.Clamp(_launcher.Left + _launcher.Width - 45, bounds.Left,
                        Math.Max(bounds.Left, bounds.Right - _settingsWindow.Width));
                    _settingsWindow.Top = Math.Clamp(_launcher.Top - 35, bounds.Top,
                        Math.Max(bounds.Top, bounds.Bottom - _settingsWindow.Height));
                }
            }
            _settingsWindow.IsVisibleChanged += (_, _) =>
            {
                if (!_settingsWindow.IsVisible) _launcher?.SetSettingsOpen(false);
            };
        }
        _settingsWindow.NavigateToGeneral();
        Show(_settingsWindow);
    }

    public void PrepareToolWindowOpen()
    {
        if (_settingsWindow?.IsVisible == true) _settingsWindow.Hide();
        _launcher?.SetSettingsOpen(false);
        if (_launcher?.IsVisible == true) _launcher.Hide();
    }

    public void CloseAllTools()
    {
        _jsonWindows.Close();
        _totoWindows.CloseForShutdown();
        foreach (var reminder in _reminders.ToArray()) reminder.Close();
        _settingsWindow?.Close();
    }

    private void OnJsonHidden() => OnToolHidden(ToolId.Json);
    private void OnTotoHidden() => OnToolHidden(ToolId.Toto);

    private void OnDueReminders(IReadOnlyList<TotoItemSnapshot> due) =>
        DispatchToUi(() => ShowReminder(new TotoReminderWindow("Toto 提醒", due)));

    private void OnScheduledPopup(TotoScheduledPopupKind kind) => DispatchToUi(() =>
    {
        var pending = _totoApplication.GetActiveItems();
        if (kind == TotoScheduledPopupKind.WorkStart)
        {
            ShowReminder(new TotoReminderWindow("Toto - 今日待办盘点", pending, highlightTodayGreen: true));
            return;
        }

        var completed = _totoApplication.GetCompletedItems(DateOnly.FromDateTime(DateTime.Today));
        ShowReminder(new TotoReminderWindow("Toto - 今日完成复盘", pending,
            completedItems: completed, highlightTodayRed: true));
    });

    private static void DispatchToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private void ShowReminder(TotoReminderWindow window)
    {
        if (_shuttingDown) return;
        _reminders.Add(window);
        window.Closed += (_, _) => { _reminders.Remove(window); _totoWindows.RefreshMain(); };
        window.Show();
        window.Activate();
        _totoWindows.RefreshMain();
    }

    private void OnToolHidden(ToolId tool)
    {
        if (_shuttingDown) return;
        ToolHidden?.Invoke(tool);
        if (_settings.Current.ReturnToLauncherOnToolClose) ShowLauncherIfHidden();
    }

    private void ShowLauncherIfHidden()
    {
        if (_launcher is { IsVisible: false }) _launcher.Toggle();
    }

    public async Task ShutdownAsync(WindowStateService states, CancellationToken cancellationToken)
    {
        _shuttingDown = true;
        _jsonWindows.Hidden -= OnJsonHidden;
        _totoWindows.Hidden -= OnTotoHidden;
        _totoRuntime.DueReminders -= OnDueReminders;
        _totoRuntime.ScheduledPopup -= OnScheduledPopup;
        await states.CaptureAsync([("launcher", _launcher), ("settings", _settingsWindow),
                ("json", _jsonSettings.Current.RememberWindowPlacement ? _jsonWindows.MainWindow : null),
                ("toto", _totoWindows.MainWindow)], cancellationToken);
        await _jsonWindows.ShutdownAsync();
        foreach (var reminder in _reminders.ToArray()) reminder.Close();
        _totoWindows.CloseForShutdown();
        if (_settingsWindow is not null)
        {
            _settingsWindow.AllowClose = true;
            _settingsWindow.Close();
        }
        _launcher?.Close();
    }

    private static void Show(System.Windows.Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }
}
