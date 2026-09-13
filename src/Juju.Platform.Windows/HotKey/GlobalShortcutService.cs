using System.Runtime.InteropServices;

namespace Juju.Platform.Windows.HotKey;

/// <summary>
/// Windows 全局快捷键的生命周期包装器。通过 P/Invoke 调用 user32.dll；这类似 Java 的 JNA/JNI
/// 边界，因此 Win32 错误码必须在失败后立即读取。
/// </summary>
public sealed partial class GlobalShortcutService : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const int InitialId = 0x4A55;
    private IntPtr _window;
    private int _id;

    /// <summary>最近一次注册失败的 Win32 错误码；成功时重置为零。</summary>
    public int LastError { get; private set; }

    /// <summary>当前已注册的规范化快捷键文本。</summary>
    public string Shortcut { get; private set; } = "Ctrl+Shift+Alt+Space";

    /// <summary>以当前 <see cref="Shortcut"/> 注册指定窗口。</summary>
    public bool Register(IntPtr window) => Register(window, Shortcut);

    /// <summary>
    /// 解析并注册候选快捷键。先以新 ID 注册成功，再注销旧 ID，因而无效或被占用的组合键
    /// 不会使用户当前仍依赖的快捷键失效。
    /// </summary>
    public bool Register(IntPtr window, string shortcut)
    {
        if (!TryParse(shortcut, out var modifiers, out var key))
        {
            LastError = 87;
            return false;
        }

        if (_window == window && _id != 0 && string.Equals(Shortcut, shortcut, StringComparison.OrdinalIgnoreCase))
        {
            LastError = 0;
            return true;
        }

        var candidateId = _id == 0 ? InitialId : _id + 1;
        if (!RegisterHotKey(window, candidateId, modifiers, key))
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        if (_id != 0) UnregisterHotKey(_window, _id);
        _window = window;
        _id = candidateId;
        Shortcut = shortcut;
        LastError = 0;
        return true;
    }

    /// <summary>判断窗口消息是否为本实例当前 ID 的 <c>WM_HOTKEY</c>（0x0312）。</summary>
    public bool IsHotKeyMessage(int message, IntPtr wParam) => message == 0x0312 && wParam.ToInt32() == _id;

    /// <summary>注销当前快捷键；同步 Win32 资源无需 <see cref="IAsyncDisposable"/>。</summary>
    public void Dispose()
    {
        if (_window != IntPtr.Zero && _id != 0) UnregisterHotKey(_window, _id);
        _id = 0;
    }

    /// <summary>把用户文本转换为 Win32 修饰键位掩码和虚拟键码，只接受已有的受控格式。</summary>
    private static bool TryParse(string shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
            else return false;
        }

        var value = parts[^1];
        if (value.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            key = 0x20;
            return modifiers != 0;
        }

        if (value.Length == 1 && char.IsLetterOrDigit(value[0]))
        {
            key = char.ToUpperInvariant(value[0]);
            return modifiers != 0;
        }

        return false;
    }

    /// <summary>
    /// P/Invoke 声明将托管参数编组为 user32 的 RegisterHotKey 调用。
    /// <c>SetLastError = true</c> 让 <see cref="Marshal.GetLastWin32Error"/> 能读取本次调用的失败原因。
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary>对应 Win32 UnregisterHotKey，用窗口句柄和注册 ID 释放系统级登记。</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}