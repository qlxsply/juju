using System.Runtime.InteropServices;
using Toto.App.UI;

namespace Toto.App.Services;

/// <summary>创建隐藏原生窗口并注册 Windows 全局快捷键。</summary>
/// <remarks>该类型封装 Win32 句柄，必须调用 <see cref="Dispose"/> 解除注册并销毁窗口。</remarks>
internal sealed class GlobalHotkeyService : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 1;
    /// <summary>当已注册的全局快捷键被按下时触发。</summary>
    // C# event 用委托保存订阅者，并限制外部代码只能订阅或取消订阅，不能直接触发。
    public event Action? Pressed;
    /// <summary>创建隐藏窗口，使其能够接收 Windows 消息。</summary>
    public GlobalHotkeyService() => CreateHandle(new CreateParams());

    /// <summary>解析并注册快捷键；无效格式或系统注册失败时返回 <see langword="false"/>。</summary>
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

    /// <summary>处理窗口消息，并在收到注册的热键消息时通知订阅者。</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    /// <summary>注销快捷键并释放隐藏窗口句柄。</summary>
    public void Dispose()
    {
        UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }

    // DllImport 是 P/Invoke：C# 可直接声明并调用 Win32 DLL 导出函数，不同于 Java JNI 的原生桥接方式。
    /// <summary>调用 Win32 API 为指定窗口注册全局快捷键。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, uint vk);

    /// <summary>调用 Win32 API 取消指定窗口的全局快捷键。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
