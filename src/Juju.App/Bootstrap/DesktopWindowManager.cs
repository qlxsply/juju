using Juju.Core.App;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Tools.Json.Documents;
using Juju.App.Settings;
using Juju.App.Views;
using Juju.Tools.Json.Settings;

namespace Juju.App.Bootstrap;

// 窗口管理器是 App 层的 DI 适配器：延迟创建窗口、复用隐藏窗口，并协调主题和持久化位置。
public sealed class DesktopWindowManager(
    JsonDocumentService documents,
    ISettingsService settings,
    WindowStateService windowState,
    ThemeService themes,
    IJsonFileClipboard fileClipboard,
    IJsonToolSettingsService jsonSettings,
    IEnumerable<ISettingsSection> settingsSections) : IWindowManager
{
    private LauncherWindow? _launcher;
    private SettingsWindow? _settings;
    private JsonToolWindow? _json;

    // 启动时先创建并完成 HWND 相关初始化，再隐藏；这样全局热键可在首次唤起前注册。
    public void InitializeLauncher()
    {
        _launcher ??= new LauncherWindow(this, settings, themes);
        windowState.Restore(_launcher, "launcher");
        _launcher.ApplySettings(settings.Current);
        _launcher.Show();
        _launcher.Hide();
    }

    public void ShowLauncher() => _launcher?.Toggle();

    // SettingsWindow 首次请求时才构造，DI 注入的多个设置节也只在此时创建。
    public void ShowSettings()
    {
        _settings ??= new SettingsWindow(settingsSections);
        windowState.Restore(_settings, "settings");
        Show(_settings);
    }

    // JSON 工具惰性创建。主题事件订阅只发生一次，因此不会随重复打开累计处理器。
    public void ShowTool(ToolId tool)
    {
        if (tool != ToolId.Json) return;
        if (_json is null)
        {
            _json = new JsonToolWindow(documents, jsonSettings) { FileClipboard = fileClipboard };
            windowState.Restore(_json, "json");
            themes.Changed += _json.ApplyTheme;
            _json.ApplyTheme(themes.Current);
        }

        Show(_json);
    }

    public void CloseAllTools()
    {
        _json?.Close();
        _settings?.Close();
    }

    // 关闭顺序先捕获 RestoreBounds，再让工具刷新内容并关闭；用户点击关闭时的“隐藏”策略在这里被显式绕过。
    public async Task ShutdownAsync(WindowStateService states, CancellationToken cancellationToken)
    {
        await states.CaptureAsync([("launcher", _launcher), ("settings", _settings), ("json", _json)],
            cancellationToken);
        if (_json is not null) await _json.ShutdownAsync();
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