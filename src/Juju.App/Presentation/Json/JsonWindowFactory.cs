using System.Windows;
using Juju.App.Bootstrap;
using Juju.App.Contracts.Windows;
using Juju.App.Settings;
using Juju.App.Tools.Json;
using Juju.App.Views;
using Juju.Core.Tools;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Settings;

namespace Juju.App.Presentation.Json;

/// <summary>独占 JSON 窗口的惰性创建、隐藏复用、关闭和关停刷新。</summary>
public sealed class JsonWindowFactory : IJsonWindowFactory
{
    private readonly JsonDocumentService _documents;
    private readonly IJsonToolSettingsService _settings;
    private readonly WindowStateService _windowState;
    private readonly ThemeService _themes;
    private readonly IJsonFileClipboard _fileClipboard;
    private JsonToolWindow? _window;

    public JsonWindowFactory(JsonDocumentService documents, IJsonToolSettingsService settings,
        WindowStateService windowState, ThemeService themes, IJsonFileClipboard fileClipboard)
    {
        _documents = documents;
        _settings = settings;
        _windowState = windowState;
        _themes = themes;
        _fileClipboard = fileClipboard;
    }

    public Window? MainWindow => _window;
    public event Action? Hidden;

    public void ShowJson()
    {
        if (_window is null)
        {
            _window = new JsonToolWindow(_documents, _settings) { FileClipboard = _fileClipboard };
            var window = _window;
            if (_settings.Current.RememberWindowPlacement) _windowState.Restore(window, "json");
            _themes.Changed += window.ApplyTheme;
            window.ApplyTheme(_themes.Current);
            window.Closing += (_, _) =>
            {
                if (_settings.Current.RememberWindowPlacement) _windowState.Remember(window, "json");
            };
            window.Closed += (_, _) =>
            {
                _themes.Changed -= window.ApplyTheme;
                if (ReferenceEquals(_window, window)) _window = null;
            };
            window.IsVisibleChanged += (_, _) =>
            {
                if (!window.IsVisible) Hidden?.Invoke();
            };
        }

        if (!_window.IsVisible && _settings.Current.OpenMaximized)
            _window.WindowState = WindowState.Maximized;
        Show(_window);
        _window.FocusForShortcuts();
    }

    public void Close() => _window?.Close();

    public async Task ShutdownAsync()
    {
        if (_window is not null) await _window.ShutdownAsync();
    }

    private static void Show(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
