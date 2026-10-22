using System.Runtime.InteropServices;

namespace Juju.Platform.Windows;

/// <summary>请求 Windows 11 为仍是原生 HWND 的自绘窗口提供系统圆角。</summary>
public static class WindowAppearance
{
    private const int WindowCornerPreference = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int Round = 2; // DWMWCP_ROUND；窗口管理器也可能因系统策略忽略此请求。

    public static void UseSystemCorners(IntPtr window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var preference = Round;
        _ = DwmSetWindowAttribute(window, WindowCornerPreference, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int size);
}
