using Juju.Core.App;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Tools.Json.Documents;
using Juju.App.Settings;
using Juju.App.Views;
using Juju.Tools.Json.Settings;
using Toto.App;
using Toto.App.Domain;
using Microsoft.Extensions.DependencyInjection;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Bootstrap;

// 窗口管理器是 App 层的 DI 适配器：延迟创建窗口、复用隐藏窗口，并协调主题和持久化位置。
public sealed class DesktopWindowManager : IWindowManager
{
    private readonly JsonDocumentService documents;
    private readonly ISettingsService settings;
    private readonly WindowStateService windowState;
    private readonly ThemeService themes;
    private readonly IJsonFileClipboard fileClipboard;
    private readonly IJsonToolSettingsService jsonSettings;
    private readonly SettingsDialogService dialogs;
    private readonly TotoToolHost toto;
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
        this.services = services;
        toto.DueReminders += ShowDueReminders;
        toto.ScheduledPopup += ShowScheduledPopup;
    }

    /// <summary>向唯一托盘报告工具隐藏事件，供设置中的状态提示开关控制气泡提示。</summary>
    public event Action<ToolId>? ToolHidden;

    // 启动时先创建并完成 HWND 相关初始化，再隐藏；这样全局热键可在首次唤起前注册。
    public void InitializeLauncher()
    {
        _launcher ??= new LauncherWindow(this, settings, themes, toto, jsonSettings);

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
            _settings = new SettingsWindow(services.GetServices<ISettingsSection>(), dialogs);
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

    public void ShowToolSettings(ToolId tool)
    {
        ShowSettings();
        _settings?.NavigateToTool(tool switch
        {
            ToolId.Json => "json", ToolId.Toto => "toto", ToolId.EchoHttp => "echo-http", _ => "kairos"
        });
    }

    // JSON 工具惰性创建。主题事件订阅只发生一次，因此不会随重复打开累计处理器。
    public void ShowTool(ToolId tool)
    {
        if (tool is ToolId.Json or ToolId.Toto)
        {
            // 与 HTML openTool 一致：打开真实工具时退出设置叠层并收起入口。
            if (_settings?.IsVisible == true) _settings.Hide();
            _launcher?.SetSettingsOpen(false);
            if (_launcher?.IsVisible == true) _launcher.Hide();
        }
        if (tool == ToolId.Toto)
        {
            if (!toto.EnsureReady())
            {
                MessageBox.Show($"节假日日历尚未就绪，Toto 将继续重试：\n{toto.InitializationError}",
                    "Toto", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }
            if (_toto is null)
            {
                _toto = new TotoMainWindow(toto, ShowHistory);
                var window = _toto;
                if (windowState.HasPlacement("toto")) windowState.Restore(window, "toto");
                else RestoreLegacyTotoBounds(window);
                window.Closed += (_, _) => { if (ReferenceEquals(_toto, window)) _toto = null; };
                window.IsVisibleChanged += (_, _) =>
                {
                    if (!window.IsVisible) OnToolHidden(ToolId.Toto);
                };
            }
            _toto.RefreshItems();
            Show(_toto);
            _toto.FocusItems();
            return;
        }
        if (tool != ToolId.Json) return;
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
            new TotoHistoryWindow(toto) { Owner = owner }.ShowDialog();
    }

    private void ShowDueReminders(IReadOnlyList<TodoItem> due) =>
        ShowReminder(new TotoReminderWindow("Toto 提醒", due));

    private void ShowScheduledPopup(ScheduledPopupKind kind)
    {
        var pending = toto.Items.GetActive();
        if (kind == ScheduledPopupKind.WorkStart)
        {
            ShowReminder(new TotoReminderWindow("Toto - 今日待办盘点", pending, highlightTodayGreen: true));
            return;
        }

        var today = DateTime.Today;
        var criteria = new QueryCriteria
        {
            Status = ItemStatus.Completed, EndedFrom = today, EndedTo = today.AddDays(1)
        };
        var firstPage = toto.Items.GetHistory(criteria, 1, 500);
        var completed = firstPage.Items.ToList();
        for (var page = 2; completed.Count < firstPage.TotalCount; page++)
            completed.AddRange(toto.Items.GetHistory(criteria, page, 500).Items);
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

    private void RestoreLegacyTotoBounds(TotoMainWindow window)
    {
        if (toto.Settings.LoadWindowBounds("main") is not { } saved) return;
        var area = System.Windows.Forms.Screen.FromRectangle(saved).WorkingArea;
        if (!area.IntersectsWith(saved)) return;
        window.Width = Math.Clamp(saved.Width, window.MinWidth, area.Width);
        window.Height = Math.Clamp(saved.Height, window.MinHeight, area.Height);
        window.Left = Math.Clamp(saved.Left, area.Left, Math.Max(area.Left, area.Right - window.Width));
        window.Top = Math.Clamp(saved.Top, area.Top, Math.Max(area.Top, area.Bottom - window.Height));
        window.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
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
