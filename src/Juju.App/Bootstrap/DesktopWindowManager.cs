using Juju.App.Contracts.Windows;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Tools.Json.Documents;
using Juju.App.Settings;
using Juju.App.Views;
using Juju.Tools.Json.Settings;
using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Reminders;
using Toto.App;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Bootstrap;

// 窗口管理器是 App 层的 DI 适配器：延迟创建窗口、复用隐藏窗口，并协调主题和持久化位置。
public sealed class DesktopWindowManager : IAppWindowManager, IJsonToolWindow, ITotoToolWindow
{
    private readonly JsonDocumentService documents;
    private readonly ISettingsService settings;
    private readonly WindowStateService windowState;
    private readonly ThemeService themes;
    private readonly IJsonFileClipboard fileClipboard;
    private readonly IJsonToolSettingsService jsonSettings;
    private readonly SettingsDialogService dialogs;
    private readonly TotoToolHost toto;
    private readonly ITotoApplicationService totoApplication;
    private readonly IServiceProvider services;
    private LauncherWindow? _launcher;
    private SettingsWindow? _settings;
    private JsonToolWindow? _json;
    private TotoMainWindow? _toto;
    private readonly List<TotoReminderWindow> _reminders = [];
    private bool _shuttingDown;

    public DesktopWindowManager(JsonDocumentService documents, ISettingsService settings,
        WindowStateService windowState, ThemeService themes, IJsonFileClipboard fileClipboard,
        IJsonToolSettingsService jsonSettings, SettingsDialogService dialogs, TotoToolHost toto,
        ITotoApplicationService totoApplication,
        IServiceProvider services)
    {
        this.documents = documents;
        this.settings = settings;
        this.windowState = windowState;
        this.themes = themes;
        this.fileClipboard = fileClipboard;
        this.jsonSettings = jsonSettings;
        this.dialogs = dialogs;
        this.toto = toto;
        this.totoApplication = totoApplication;
        this.services = services;
        toto.DueReminders += ShowDueReminders;
        toto.ScheduledPopup += ShowScheduledPopup;
    }

    /// <summary>向唯一托盘报告工具隐藏事件，供设置中的状态提示开关控制气泡提示。</summary>
    public event Action<ToolId>? ToolHidden;

    // 启动时先创建并完成 HWND 相关初始化，再隐藏；这样全局热键可在首次唤起前注册。
    public void InitializeLauncher()
    {
        _launcher ??= new LauncherWindow(services.GetRequiredService<Juju.App.Tools.Contracts.IToolManager>(),
            this, settings, themes, totoApplication, jsonSettings);

        // Loaded 时 SizeToContent 已经算出真实高度；仅恢复拖动位置，不从旧配置覆盖自适应宽高。
        _launcher.Loaded += (_, _) => windowState.RestorePosition(_launcher, "launcher");
        _launcher.ApplySettings(settings.Current);
        _launcher.Show();
        if (settings.Current.StartMinimized) _launcher.Hide();
    }

    public bool TryRegisterToolHotkey(ToolId tool, string shortcut) =>
        _launcher?.TryRegisterToolHotkey(tool, shortcut) ?? false;

    public void ShowLauncher() => _launcher?.Toggle();

    // SettingsWindow 首次请求时才构造，DI 注入的多个设置节也只在此时创建。
    public void ShowSettings()
    {
        if (_launcher is { IsVisible: false }) _launcher.Toggle();
        _launcher?.SetSettingsOpen(true);
        if (_settings is null)
        {
            var sections = services.GetServices<ISettingsSection>()
                .Concat(services.GetRequiredService<Juju.App.Tools.Contracts.IToolManager>().SettingsSections);
            _settings = new SettingsWindow(sections, dialogs);
            var hasSavedPosition = windowState.HasPlacement("settings");
            windowState.Restore(_settings, "settings");
            // 原型将设置覆盖在 Launcher 右侧；Owner 令两个原生窗口保持同一前后层级。
            if (_launcher is not null)
            {
                _settings.Owner = _launcher;
                var bounds = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point((int)_launcher.Left, (int)_launcher.Top)).WorkingArea;
                if (!hasSavedPosition)
                {
                    _settings.Left = Math.Clamp(_launcher.Left + _launcher.Width - 45, bounds.Left,
                        Math.Max(bounds.Left, bounds.Right - _settings.Width));
                    _settings.Top = Math.Clamp(_launcher.Top - 35, bounds.Top,
                        Math.Max(bounds.Top, bounds.Bottom - _settings.Height));
                }
            }
            _settings.IsVisibleChanged += (_, _) =>
            {
                if (!_settings.IsVisible) _launcher?.SetSettingsOpen(false);
            };
        }
        _settings.NavigateToGeneral();
        Show(_settings);
    }

    public void PrepareToolWindowOpen()
    {
        if (_settings?.IsVisible == true) _settings.Hide();
        _launcher?.SetSettingsOpen(false);
        if (_launcher?.IsVisible == true) _launcher.Hide();
    }

    public void ShowToto()
    {
        if (_toto is null)
        {
            _toto = new TotoMainWindow(totoApplication, ShowHistory);
            var window = _toto;
            if (windowState.HasPlacement("toto")) windowState.Restore(window, "toto");
            window.Closed += (_, _) => { if (ReferenceEquals(_toto, window)) _toto = null; };
            window.IsVisibleChanged += (_, _) =>
            {
                if (!window.IsVisible) OnToolHidden(ToolId.Toto);
            };
        }
        _toto.RefreshItems();
        Show(_toto);
        _toto.FocusItems();
    }

    // JSON 工具惰性创建。主题事件订阅只发生一次，因此不会随重复打开累计处理器。
    public void ShowJson()
    {
        if (_json is null)
        {
            _json = new JsonToolWindow(documents, jsonSettings) { FileClipboard = fileClipboard };
            var window = _json;
            if (jsonSettings.Current.RememberWindowPlacement) windowState.Restore(window, "json");
            themes.Changed += window.ApplyTheme;
            window.ApplyTheme(themes.Current);
            window.Closing += (_, _) =>
            {
                if (jsonSettings.Current.RememberWindowPlacement) windowState.Remember(window, "json");
            };
            window.Closed += (_, _) =>
            {
                themes.Changed -= window.ApplyTheme;
                if (ReferenceEquals(_json, window)) _json = null;
            };
            window.IsVisibleChanged += (_, _) =>
            {
                if (!window.IsVisible) OnToolHidden(ToolId.Json);
            };
        }

        if (!_json.IsVisible && jsonSettings.Current.OpenMaximized)
            _json.WindowState = System.Windows.WindowState.Maximized;
        Show(_json);
        _json.FocusForShortcuts();
    }

    public void CloseAllTools()
    {
        _json?.Close();
        if (_toto is not null) { _toto.AllowShutdown(); _toto.Close(); }
        foreach (var reminder in _reminders.ToArray()) reminder.Close();
        _settings?.Close();
    }

    private void ShowHistory()
    {
        if (_toto is { IsVisible: true } owner)
            new TotoHistoryWindow(totoApplication) { Owner = owner }.ShowDialog();
    }

    private void ShowDueReminders(IReadOnlyList<TotoItemSnapshot> due) =>
        ShowReminder(new TotoReminderWindow("Toto 提醒", due));

    private void ShowScheduledPopup(TotoScheduledPopupKind kind)
    {
        var pending = totoApplication.GetActiveItems();
        if (kind == TotoScheduledPopupKind.WorkStart)
        {
            ShowReminder(new TotoReminderWindow("Toto - 今日待办盘点", pending, highlightTodayGreen: true));
            return;
        }

        var completed = totoApplication.GetCompletedItems(DateOnly.FromDateTime(DateTime.Today));
        ShowReminder(new TotoReminderWindow("Toto - 今日完成复盘", pending,
            completedItems: completed, highlightTodayRed: true));
    }

    private void ShowReminder(TotoReminderWindow window)
    {
        if (_shuttingDown) return;
        _reminders.Add(window);
        window.Closed += (_, _) => { _reminders.Remove(window); _toto?.RefreshItems(); };
        window.Show();
        window.Activate();
        _toto?.RefreshItems();
    }

    private void OnToolHidden(ToolId tool)
    {
        if (_shuttingDown) return;
        ToolHidden?.Invoke(tool);
        if (settings.Current.ReturnToLauncherOnToolClose) ShowLauncherIfHidden();
    }

    private void ShowLauncherIfHidden()
    {
        if (_launcher is { IsVisible: false }) _launcher.Toggle();
    }

    // 关闭顺序先捕获 RestoreBounds，再让工具刷新内容并关闭；用户点击关闭时的“隐藏”策略在这里被显式绕过。
    public async Task ShutdownAsync(WindowStateService states, CancellationToken cancellationToken)
    {
        _shuttingDown = true;
        await states.CaptureAsync([("launcher", _launcher), ("settings", _settings),
                ("json", jsonSettings.Current.RememberWindowPlacement ? _json : null), ("toto", _toto)],
            cancellationToken);
        if (_json is not null) await _json.ShutdownAsync();
        foreach (var reminder in _reminders.ToArray()) reminder.Close();
        if (_toto is not null) { _toto.AllowShutdown(); _toto.Close(); }
        if (_settings is not null)
        {
            _settings.AllowClose = true;
            _settings.Close();
        }

        _launcher?.Close();
    }

    // WPF Hide 后再次 Show 不会重新构造窗口；最小化窗口还需要恢复后才可获得焦点。
    private static void Show(System.Windows.Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized)
            window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }
}
