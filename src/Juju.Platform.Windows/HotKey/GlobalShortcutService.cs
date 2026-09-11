using System.Runtime.InteropServices;

namespace Juju.Platform.Windows.HotKey;

public sealed class GlobalShortcutService : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int InitialId = 0x4A55;
    private IntPtr _window;
    private int _id;

    public int LastError { get; private set; }
    public string Shortcut { get; private set; } = "Ctrl+Shift+Alt+Space";

    public bool Register(IntPtr window) => Register(window, Shortcut);

    // Register a candidate under a new id first, so an invalid or occupied shortcut
    // never drops the shortcut the user is already relying on.
    public bool Register(IntPtr window, string shortcut)
    {
        if (!TryParse(shortcut, out var modifiers, out var key)) { LastError = 87; return false; }
        var candidateId = _id == 0 ? InitialId : _id + 1;
        if (!RegisterHotKey(window, candidateId, modifiers, key)) { LastError = Marshal.GetLastWin32Error(); return false; }
        if (_id != 0) UnregisterHotKey(_window, _id);
        _window = window;
        _id = candidateId;
        Shortcut = shortcut;
        LastError = 0;
        return true;
    }

    public bool IsHotKeyMessage(int message, IntPtr wParam) => message == 0x0312 && wParam.ToInt32() == _id;
    public void Dispose() { if (_window != IntPtr.Zero && _id != 0) UnregisterHotKey(_window, _id); _id = 0; }

    private static bool TryParse(string shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else return false;
        }
        var value = parts[^1];
        if (value.Equals("Space", StringComparison.OrdinalIgnoreCase)) { key = 0x20; return modifiers != 0; }
        if (value.Length == 1 && char.IsLetterOrDigit(value[0])) { key = char.ToUpperInvariant(value[0]); return modifiers != 0; }
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
