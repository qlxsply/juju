using Juju.App.Contracts.Windows;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;

namespace Juju.App.Tools.Routing;

/// <summary>将所有 Launcher、单键和直达热键请求路由到对应工具模块。</summary>
public sealed class ToolManager : IToolManager
{
    private readonly IReadOnlyDictionary<ToolId, IToolModule> _modules;
    private readonly IReadOnlyList<IToolModule> _orderedModules;
    private readonly IAppWindowManager _windows;
    private readonly IToolOpenFeedback _feedback;
    private readonly List<IToolModule> _started = [];
    private bool _stopping;

    public ToolManager(IEnumerable<IToolModule> modules, IToolCatalog catalog,
        IAppWindowManager windows, IToolOpenFeedback feedback)
    {
        var registered = modules.OrderBy(module => module.Launcher.Order).ToArray();
        if (registered.Select(module => module.Descriptor.Id).Distinct().Count() != registered.Length)
            throw new InvalidOperationException("每个 ToolId 只能注册一个工具模块。");
        _modules = registered.ToDictionary(module => module.Descriptor.Id);
        _orderedModules = Array.AsReadOnly(registered);
        Catalog = catalog;
        _windows = windows;
        _feedback = feedback;
    }

    public IToolCatalog Catalog { get; }
    public IReadOnlyCollection<ISettingsSection> SettingsSections =>
        _orderedModules.SelectMany(module => module.SettingsSections).ToArray();

    public void Open(ToolId tool)
    {
        var module = _modules[tool];
        if (module.Launcher.HideLauncherOnOpen) _windows.PrepareToolWindowOpen();
        var readiness = module.CheckReadiness();
        if (!readiness.IsReady)
        {
            if (!string.IsNullOrWhiteSpace(readiness.Message))
                _feedback.Show(readiness.MessageTitle ?? module.Launcher.Name,
                    readiness.Message, readiness.MessageKind);
            return;
        }

        module.Open();
    }

    /// <summary>启动声明为 Required 的后台模块；同一实例重复调用不会重复启动。</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_stopping) throw new InvalidOperationException("工具运行时已经停止，不能重新启动。");
        if (_started.Count != 0) return;

        try
        {
            foreach (var module in _orderedModules.Where(module =>
                         module.Descriptor.Background == BackgroundCapability.Required))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await module.StartAsync(cancellationToken);
                _started.Add(module);
            }
        }
        catch
        {
            await StopStartedAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        if (_stopping) return;
        _stopping = true;
        await StopStartedAsync(cancellationToken);
    }

    private async Task StopStartedAsync(CancellationToken cancellationToken)
    {
        List<Exception>? errors = null;
        foreach (var module in _started.AsEnumerable().Reverse().ToArray())
        {
            try
            {
                await module.StopAsync(cancellationToken);
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
            finally
            {
                _started.Remove(module);
            }
        }

        if (errors is not null) throw new AggregateException("一个或多个工具模块停止失败。", errors);
    }
}
