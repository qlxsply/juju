using Juju.Core.Tools;

namespace Juju.Core.App;

/// <summary>应用关闭流程的异步契约；取消可由宿主在关闭超时时发出。</summary>
public interface IAppLifecycle
{
    /// <summary>停止应用拥有的资源和后台工作。</summary>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>将工具打开请求路由到窗口与运行时协调器的抽象。</summary>
public interface IToolManager
{
    /// <summary>请求打开指定工具。</summary>
    void Open(ToolId tool);
}

/// <summary>跟踪并控制工具后台运行状态的抽象。</summary>
public interface IRuntimeManager
{
    /// <summary>读取工具当前后台状态。</summary>
    RuntimeState GetState(ToolId tool);

    /// <summary>异步启动支持后台运行的工具。</summary>
    Task StartAsync(ToolId tool, CancellationToken cancellationToken = default);

    /// <summary>异步停止全部后台工具。</summary>
    Task StopAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>应用窗口操作的 UI 边界，核心层无需引用具体窗口框架。</summary>
public interface IWindowManager
{
    void ShowLauncher();
    void ShowSettings();
    void ShowTool(ToolId tool);
    void CloseAllTools();
}

/// <summary>基于注册表初始化状态字典的轻量运行时管理器。</summary>
public sealed class RuntimeManager(IToolRegistry registry) : IRuntimeManager
{
    private readonly Dictionary<ToolId, RuntimeState> _states =
        registry.All.ToDictionary(tool => tool.Id, _ => RuntimeState.Stopped);

    /// <summary>返回字典中保存的状态；未注册工具会按字典规则抛出异常。</summary>
    public RuntimeState GetState(ToolId tool) => _states[tool];

    /// <summary>不支持后台的工具无需启动；当前实现的启动为已完成任务，保留异步扩展点。</summary>
    public Task StartAsync(ToolId tool, CancellationToken cancellationToken = default)
    {
        if (registry.Get(tool).Background == BackgroundCapability.Unsupported) return Task.CompletedTask;
        _states[tool] = RuntimeState.Running;
        return Task.CompletedTask;
    }

    /// <summary>将全部已注册工具恢复为停止状态。</summary>
    public Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var tool in _states.Keys) _states[tool] = RuntimeState.Stopped;
        return Task.CompletedTask;
    }
}

/// <summary>打开工具时先满足必要后台依赖，再显示对应窗口的协调器。</summary>
public sealed class ToolManager(IToolRegistry registry, IWindowManager windows, IRuntimeManager runtimes) : IToolManager
{
    /// <summary>
    /// 对必需后台工具触发启动后立即显示窗口。丢弃任务表示启动并不阻塞 UI；真正的异步实现
    /// 应在其自身边界处理失败，以免未观察异常。
    /// </summary>
    public void Open(ToolId tool)
    {
        var descriptor = registry.Get(tool);
        if (descriptor.Background == BackgroundCapability.Required) _ = runtimes.StartAsync(tool);
        windows.ShowTool(tool);
    }
}