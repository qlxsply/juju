namespace Toto.App.Services;

/// <summary>使用命名互斥体确保仅有一个应用实例，并让后续实例请求前台显示。</summary>
/// <remarks>实现 <see cref="IDisposable"/> 的资源应由调用方使用 <c>using</c> 或在关闭时调用 <see cref="Dispose"/>，类似 Java 的 try-with-resources。</remarks>
internal sealed class SingleInstanceService : IDisposable
{
    private const string Name = "Local\\toto.instance.3A44A2C6-9357-45CB-A8B1-9247AE39E43B";
    private readonly Mutex mutex;
    private readonly EventWaitHandle showEvent;
    private readonly RegisteredWaitHandle? wait;
    private Action? showHandler;
    private int showPending;

    /// <summary>初始化首个实例持有的同步对象，并注册显示请求回调。</summary>
    private SingleInstanceService(Mutex mutex, EventWaitHandle showEvent)
    {
        this.mutex = mutex;
        this.showEvent = showEvent;
        // 线程池等待回调不是 UI 线程；处理程序须自行保证其线程安全或切换线程。
        wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => RequestShow(), null, Timeout.Infinite,
            false);
    }

    /// <summary>尝试成为首个实例；若已有实例，则向其发送显示信号并返回 <see langword="false"/>。</summary>
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

    /// <summary>设置处理显示请求的委托，并立即处理在设置前收到的请求。</summary>
    // Action 是 C# 委托类型，类似 Java 函数式接口的单方法回调，但可直接调用并组合。
    public void SetShowHandler(Action handler)
    {
        showHandler = handler;
        if (Interlocked.Exchange(ref showPending, 0) == 1) handler();
    }

    /// <summary>在收到命名事件信号时调用处理程序，或记录一个待处理请求。</summary>
    private void RequestShow()
    {
        var handler = showHandler;
        if (handler is null) Interlocked.Exchange(ref showPending, 1);
        else handler();
    }

    /// <summary>注销等待回调并释放命名同步对象。</summary>
    public void Dispose()
    {
        wait?.Unregister(null);
        showEvent.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
