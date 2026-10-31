using System.IO;
using System.Text.Json;
using System.Windows;
using Juju.Core.Contracts.Platform;
using Juju.Core.Storage;

namespace Juju.App.Bootstrap;

// 主构造函数注入原子写入器；窗口坐标仍属于 App 层，因为它直接依赖 WPF Window。
public sealed class WindowStateService(IAtomicFileWriter writer, IWorkAreaProvider workAreas)
{
    private readonly string _path = Path.Combine(ApplicationPaths.ApplicationDataDirectory, "window-state.json");
    private Dictionary<string, WindowPlacement> _placements = [];

    // 启动期间异步反序列化；缺失或空文件映射为空字典，首次运行无需特殊分支。
    public async Task LoadAsync(CancellationToken cancellationToken) => _placements = File.Exists(_path)
        ? JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(
            await File.ReadAllTextAsync(_path, cancellationToken)) ?? []
        : [];

    public bool HasPlacement(string key) => _placements.ContainsKey(key);

    // Launcher 的宽高随工具列表布局而定，只恢复曾拖动的位置；旧版宽高不能覆盖新设计。
    public void RestorePosition(Window window, string key)
    {
        if (!_placements.TryGetValue(key, out var state)) return;
        var bounds = new Rect((int)state.Left, (int)state.Top,
            Math.Max(1, (int)window.ActualWidth), Math.Max(1, (int)window.ActualHeight));
        var area = FindIntersectingWorkArea(bounds);
        if (area.Width <= 0 || area.Height <= 0) return;
        window.Left = Math.Clamp(state.Left, area.Left, Math.Max(area.Left, area.Right - window.ActualWidth));
        window.Top = Math.Clamp(state.Top, area.Top, Math.Max(area.Top, area.Bottom - window.ActualHeight));
    }

    // 恢复前验证保存的矩形仍与任一工作区相交，避免显示器移除后窗口出现在屏幕外。
    public void Restore(Window window, string key)
    {
        if (!_placements.TryGetValue(key, out var state)) return;
        var bounds = new Rect((int)state.Left, (int)state.Top, (int)state.Width, (int)state.Height);
        var area = FindIntersectingWorkArea(bounds);
        if (area.Width <= 0 || area.Height <= 0) return;
        bounds.Intersect(area);
        if (bounds.Width < 80 || bounds.Height < 80) return;
        window.Left = state.Left;
        window.Top = state.Top;
        // 普通窗口仍恢复宽高；若它指定 Min/Max 则遵守约束（Launcher 只调用 RestorePosition）。
        window.Width = Math.Clamp(state.Width, window.MinWidth, window.MaxWidth);
        window.Height = Math.Clamp(state.Height, window.MinHeight, window.MaxHeight);
        window.WindowState = state.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    // WPF 窗口真正关闭前先保留其几何信息；适用于 JSON 设置为“不复用窗口”的情况。
    public void Remember(Window window, string key)
    {
        // 关闭后 Window 可能不再可读，先在 Closing 事件中把 RestoreBounds 捕获到内存。
        var bounds = window.RestoreBounds;
        _placements[key] = new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            window.WindowState == WindowState.Maximized);
    }

    // 值元组使调用点能紧凑地传递窗口键和值；相当于 Java 中临时的 Pair 记录。
    public async Task CaptureAsync(IEnumerable<(string Key, Window? Window)> windows,
        CancellationToken cancellationToken)
    {
        foreach (var (key, window) in windows)
        {
            if (window is null) continue;
            Remember(window, key);
        }

        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(_placements), cancellationToken);
    }

    private Rect FindIntersectingWorkArea(Rect bounds)
    {
        foreach (var workArea in workAreas.GetWorkAreas())
        {
            var area = new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
            if (area.IntersectsWith(bounds)) return area;
        }
        return Rect.Empty;
    }

    // record 是序列化用的不可变数据载体，自动提供值相等性，语义接近 Java record。
    private sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
}
