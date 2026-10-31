using Juju.Core.Contracts.Platform;
using Microsoft.Win32;

namespace Juju.Platform.Windows.Lifecycle;

/// <summary>把 Windows 会话、电源和时间事件转换为平台无关生命周期信号。</summary>
public sealed class WindowsSystemLifecycleEvents : ISystemLifecycleEvents, IDisposable
{
    private bool _disposed;

    public event Action<SystemLifecycleSignal>? Changed;

    public WindowsSystemLifecycleEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs args)
    {
        if (args.Reason == SessionSwitchReason.SessionLock)
            Changed?.Invoke(SystemLifecycleSignal.SessionLocked);
        else if (args.Reason == SessionSwitchReason.SessionUnlock)
            Changed?.Invoke(SystemLifecycleSignal.SessionUnlocked);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Resume) Changed?.Invoke(SystemLifecycleSignal.Resumed);
    }

    private void OnTimeChanged(object? sender, EventArgs args) => Changed?.Invoke(SystemLifecycleSignal.TimeChanged);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
    }
}
