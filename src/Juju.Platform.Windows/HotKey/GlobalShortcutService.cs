using System.Runtime.InteropServices;

namespace Juju.Platform.Windows.HotKey;

public sealed class GlobalShortcutService : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int Id = 0x4A55;
    private IntPtr _window;

    public bool Register(IntPtr window)
    {
        _window = window;
        return RegisterHotKey(window, Id, ModAlt | ModControl | ModShift, 0x20);
    }

    public bool IsHotKeyMessage(int message, IntPtr wParam) => message == 0x0312 && wParam.ToInt32() == Id;
    public void Dispose() { if (_window != IntPtr.Zero) UnregisterHotKey(_window, Id); }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
