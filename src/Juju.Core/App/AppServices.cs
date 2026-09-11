using Juju.Core.Tools;

namespace Juju.Core.App;

public interface IAppLifecycle { Task ShutdownAsync(CancellationToken cancellationToken = default); }
public interface IToolManager { void Open(ToolId tool); }
public interface IRuntimeManager { RuntimeState GetState(ToolId tool); Task StartAsync(ToolId tool, CancellationToken cancellationToken = default); Task StopAllAsync(CancellationToken cancellationToken = default); }
public interface IWindowManager { void ShowLauncher(); void ShowSettings(); void ShowTool(ToolId tool); void CloseAllTools(); }

public sealed class RuntimeManager(IToolRegistry registry) : IRuntimeManager
{
    private readonly Dictionary<ToolId, RuntimeState> _states = registry.All.ToDictionary(tool => tool.Id, _ => RuntimeState.Stopped);
    public RuntimeState GetState(ToolId tool) => _states[tool];
    public Task StartAsync(ToolId tool, CancellationToken cancellationToken = default)
    {
        if (registry.Get(tool).Background == BackgroundCapability.Unsupported) return Task.CompletedTask;
        _states[tool] = RuntimeState.Running;
        return Task.CompletedTask;
    }
    public Task StopAllAsync(CancellationToken cancellationToken = default) { foreach (var tool in _states.Keys) _states[tool] = RuntimeState.Stopped; return Task.CompletedTask; }
}

public sealed class ToolManager(IToolRegistry registry, IWindowManager windows, IRuntimeManager runtimes) : IToolManager
{
    public void Open(ToolId tool)
    {
        var descriptor = registry.Get(tool);
        if (descriptor.Background == BackgroundCapability.Required) _ = runtimes.StartAsync(tool);
        windows.ShowTool(tool);
    }
}
