namespace Juju.Core.Contracts.Platform;

public enum SystemLifecycleSignal
{
    SessionLocked,
    SessionUnlocked,
    Resumed,
    TimeChanged
}

/// <summary>平台无关的会话/电源/系统时间变化通知。</summary>
public interface ISystemLifecycleEvents
{
    event Action<SystemLifecycleSignal>? Changed;
}
