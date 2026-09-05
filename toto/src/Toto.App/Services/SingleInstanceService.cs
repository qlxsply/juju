namespace Toto.App.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private const string Name = "Local\\toto.instance.3A44A2C6-9357-45CB-A8B1-9247AE39E43B";
    private readonly Mutex mutex;
    private readonly EventWaitHandle showEvent;
    private readonly RegisteredWaitHandle? wait;
    private Action? showHandler;
    private int showPending;

    private SingleInstanceService(Mutex mutex, EventWaitHandle showEvent)
    {
        this.mutex = mutex;
        this.showEvent = showEvent;
        wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => RequestShow(), null, Timeout.Infinite,
            false);
    }

    public static bool TryCreate(out SingleInstanceService? service)
    {
        var mutex = new Mutex(true, Name, out var first);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".show");
        if (!first)
        {
            signal.Set();
            signal.Dispose();
            mutex.Dispose();
            service = null;
            return false;
        }

        service = new SingleInstanceService(mutex, signal);
        return true;
    }

    public void SetShowHandler(Action handler)
    {
        showHandler = handler;
        if (Interlocked.Exchange(ref showPending, 0) == 1) handler();
    }

    private void RequestShow()
    {
        var handler = showHandler;
        if (handler is null) Interlocked.Exchange(ref showPending, 1);
        else handler();
    }

    public void Dispose()
    {
        wait?.Unregister(null);
        showEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}