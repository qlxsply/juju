using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Juju.Core.Storage;

namespace Juju.App.Bootstrap;

public sealed class WindowStateService(IAtomicFileWriter writer)
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "juju", "window-state.json");
    private Dictionary<string, WindowPlacement> _placements = [];

    public async Task LoadAsync(CancellationToken cancellationToken) => _placements = File.Exists(_path)
        ? JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(await File.ReadAllTextAsync(_path, cancellationToken)) ?? [] : [];

    public void Restore(Window window, string key)
    {
        if (!_placements.TryGetValue(key, out var state)) return;
        var area = Screen.AllScreens.Select(screen => screen.WorkingArea).FirstOrDefault(screen => screen.IntersectsWith(new System.Drawing.Rectangle((int)state.Left, (int)state.Top, (int)state.Width, (int)state.Height)));
        if (area == default) return;
        var visible = System.Drawing.Rectangle.Intersect(area, new System.Drawing.Rectangle((int)state.Left, (int)state.Top, (int)state.Width, (int)state.Height));
        if (visible.Width < 80 || visible.Height < 80) return;
        window.Left = state.Left; window.Top = state.Top; window.Width = Math.Max(window.MinWidth, state.Width); window.Height = Math.Max(window.MinHeight, state.Height);
        window.WindowState = state.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    public async Task CaptureAsync(IEnumerable<(string Key, Window? Window)> windows, CancellationToken cancellationToken)
    {
        foreach (var (key, window) in windows)
        {
            if (window is null) continue;
            _placements[key] = new(window.RestoreBounds.Left, window.RestoreBounds.Top, window.RestoreBounds.Width, window.RestoreBounds.Height, window.WindowState == WindowState.Maximized);
        }
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(_placements), cancellationToken);
    }

    private sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
}
