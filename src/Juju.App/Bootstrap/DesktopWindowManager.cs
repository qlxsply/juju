using Juju.Core.App;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.Tools.Json.Documents;
using Juju.Platform.Windows.Startup;

namespace Juju.App.Bootstrap;

public sealed class DesktopWindowManager(JsonDocumentService documents, ISettingsService settings, DataRootService roots, IStorageManager storage, WindowStateService windowState, ThemeService themes, IJsonFileClipboard fileClipboard, IStartupService startup) : IWindowManager
{
    private LauncherWindow? _launcher;
    private SettingsWindow? _settings;
    private JsonToolWindow? _json;

    public void InitializeLauncher()
    {
        _launcher ??= new LauncherWindow(this, settings, themes);
        windowState.Restore(_launcher, "launcher");
        _launcher.ApplySettings(settings.Current);
        _launcher.Show();
        _launcher.Hide();
    }

    public void ShowLauncher() => _launcher?.Toggle();
    public void ShowSettings()
    {
        _settings ??= new SettingsWindow(settings, new DataRootConfigurationService(roots, storage, settings), themes, _launcher!, startup);
        windowState.Restore(_settings, "settings");
        Show(_settings);
    }

    public void ShowTool(ToolId tool)
    {
        if (tool != ToolId.Json) return;
        if (_json is null)
        {
            _json = new JsonToolWindow(documents, settings) { FileClipboard = fileClipboard };
            windowState.Restore(_json, "json");
            themes.Changed += _json.ApplyTheme;
            _json.ApplyTheme(themes.Current);
        }
        Show(_json);
    }

    public void CloseAllTools() { _json?.Close(); _settings?.Close(); }

    public async Task ShutdownAsync(WindowStateService states, CancellationToken cancellationToken)
    {
        await states.CaptureAsync([("launcher", _launcher), ("settings", _settings), ("json", _json)], cancellationToken);
        if (_json is not null) await _json.ShutdownAsync();
        _settings?.Close();
        _launcher?.Close();
    }

    private static void Show(System.Windows.Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == System.Windows.WindowState.Minimized) window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }
}
