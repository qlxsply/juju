using System.Runtime.InteropServices;
using Toto.App.UI;

namespace Toto.App.Services;

internal sealed class GlobalHotkeyService : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 1;
    public event Action? Pressed;
    public GlobalHotkeyService() => CreateHandle(new CreateParams());

    public bool Register(string hotkey)
    {
        UnregisterHotKey(Handle, Id);
        if (!Hotkey.TryParse(hotkey, out var keys)) return false;
        var modifiers = 0;
        if ((keys & Keys.Control) != 0) modifiers |= 2;
        if ((keys & Keys.Alt) != 0) modifiers |= 1;
        if ((keys & Keys.Shift) != 0) modifiers |= 4;
        if ((keys & Keys.LWin) != 0 || (keys & Keys.RWin) != 0) modifiers |= 8;
        return RegisterHotKey(Handle, Id, modifiers, (uint)(keys & Keys.KeyCode));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}