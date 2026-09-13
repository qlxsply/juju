using System.IO;
using System.Text.Json;
using System.Windows;
using Juju.Core.Storage;

namespace Juju.App.Bootstrap;

// 主构造函数注入原子写入器；窗口坐标仍属于 App 层，因为它直接依赖 WPF Window。
public sealed class WindowStateService(IAtomicFileWriter writer)
{
    private readonly string _path = Path.Combine(ApplicationPaths.ApplicationDataDirectory, "window-state.json");
    private Dictionary<string, WindowPlacement> _placements = [];

    // 启动期间异步反序列化；缺失或空文件映射为空字典，首次运行无需特殊分支。
    public async Task LoadAsync(CancellationToken cancellationToken) => _placements = File.Exists(_path)
        ? JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(
            await File.ReadAllTextAsync(_path, cancellationToken)) ?? []
        : [];

    // 恢复前验证保存的矩形仍与任一工作区相交，避免显示器移除后窗口出现在屏幕外。
    public void Restore(Window window, string key)
    {
        if (!_placements.TryGetValue(key, out var state)) return;
        var area = Screen.AllScreens.Select(screen => screen.WorkingArea).FirstOrDefault(screen =>
            screen.IntersectsWith(new Rectangle((int)state.Left, (int)state.Top, (int)state.Width,
                (int)state.Height)));
        if (area == default) return;
        var visible = Rectangle.Intersect(area,
            new Rectangle((int)state.Left, (int)state.Top, (int)state.Width, (int)state.Height));
        if (visible.Width < 80 || visible.Height < 80) return;
        window.Left = state.Left;
        window.Top = state.Top;
        window.Width = Math.Max(window.MinWidth, state.Width);
        window.Height = Math.Max(window.MinHeight, state.Height);
        window.WindowState = state.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    // 值元组使调用点能紧凑地传递窗口键和值；相当于 Java 中临时的 Pair 记录。
    public async Task CaptureAsync(IEnumerable<(string Key, Window? Window)> windows,
        CancellationToken cancellationToken)
    {
        foreach (var (key, window) in windows)
        {
            if (window is null) continue;
            _placements[key] = new WindowPlacement(window.RestoreBounds.Left, window.RestoreBounds.Top,
                window.RestoreBounds.Width,
                window.RestoreBounds.Height, window.WindowState == WindowState.Maximized);
        }

        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(_placements), cancellationToken);
    }

    // record 是序列化用的不可变数据载体，自动提供值相等性，语义接近 Java record。
    private sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
}