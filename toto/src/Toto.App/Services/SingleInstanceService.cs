namespace Toto.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string Name = "Local\\toto.instance.3A44A2C6-9357-45CB-A8B1-9247AE39E43B";
    private readonly Mutex mutex; private readonly EventWaitHandle showEvent; private RegisteredWaitHandle? wait;
    public event Action? ShowRequested;
    private SingleInstanceService(Mutex mutex, EventWaitHandle showEvent) { this.mutex = mutex; this.showEvent = showEvent; wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, false); }
    public static bool TryCreate(out SingleInstanceService? service)
    {
        var mutex = new Mutex(true, Name, out var first); var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".show");
        if (!first) { signal.Set(); signal.Dispose(); mutex.Dispose(); service = null; return false; }
        service = new SingleInstanceService(mutex, signal); return true;
    }
    public void Dispose() { wait?.Unregister(null); showEvent.Dispose(); mutex.ReleaseMutex(); mutex.Dispose(); }
}
