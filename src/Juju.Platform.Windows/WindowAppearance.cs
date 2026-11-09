using System.Runtime.InteropServices;

namespace Juju.Platform.Windows;

/// <summary>请求 Windows 11 为仍是原生 HWND 的自绘窗口提供系统圆角。</summary>
public static class WindowAppearance
{
    private const int WindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int WindowTransitionsForcedDisabled = 3; // DWMWA_TRANSITIONS_FORCEDISABLED
    private const int Round = 2; // DWMWCP_ROUND；窗口管理器也可能因系统策略忽略此请求。
    private const uint MonitorDefaultToNearest = 2;

    public static void UseSystemCorners(IntPtr window)
    {
        if ( !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) )
            return;
        var preference = Round;
        _ = DwmSetWindowAttribute(window, WindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>禁用单个窗口的 DWM 打开/关闭过渡动画。</summary>
    public static void DisableWindowTransitions(IntPtr window)
    {
        if ( !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || window == IntPtr.Zero )
            return;
        var disabled = 1;
        _ = DwmSetWindowAttribute(window, WindowTransitionsForcedDisabled, ref disabled, sizeof(int));
    }

    /// <summary>把自定义无边框窗口的最大化边界限制在所在显示器的工作区（任务栏之外）。</summary>
    public static void ApplyWorkAreaMaximizedBounds(IntPtr window, IntPtr minMaxInfoPointer)
    {
        if ( !OperatingSystem.IsWindows() || window == IntPtr.Zero || minMaxInfoPointer == IntPtr.Zero )
            return;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        if ( monitor == IntPtr.Zero )
            return;
        var information = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if ( !GetMonitorInfo(monitor, ref information) )
            return;

        var bounds = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        bounds.MaxPosition.X = information.WorkArea.Left - information.MonitorArea.Left;
        bounds.MaxPosition.Y = information.WorkArea.Top - information.MonitorArea.Top;
        bounds.MaxSize.X = information.WorkArea.Right - information.WorkArea.Left;
        bounds.MaxSize.Y = information.WorkArea.Bottom - information.WorkArea.Top;
        Marshal.StructureToPtr(bounds, minMaxInfoPointer, fDeleteOld: false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int size);
}
