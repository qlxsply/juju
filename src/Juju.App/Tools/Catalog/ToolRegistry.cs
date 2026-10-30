using Juju.App.Tools.Contracts;
using Juju.Core.Tools;

namespace Juju.App.Tools.Catalog;

/// <summary>由已注册模块构建的桌面目录；共享 Core 注册表只暴露工具能力描述。</summary>
public sealed class ToolRegistry : IToolCatalog, IToolRegistry
{
    private readonly IReadOnlyDictionary<ToolId, LauncherToolMetadata> _launcher;
    private readonly IReadOnlyDictionary<ToolId, ToolDescriptor> _descriptors;

    public ToolRegistry(IEnumerable<IToolModule> modules)
    {
        var entries = modules.OrderBy(module => module.Launcher.Order).ToArray();
        if (entries.Select(module => module.Descriptor.Id).Distinct().Count() != entries.Length)
            throw new InvalidOperationException("每个 ToolId 只能注册一个工具模块。");
        if (entries.Any(module => module.Descriptor.Id != module.Launcher.Id))
            throw new InvalidOperationException("工具模块的描述 ID 与 Launcher 元数据 ID 必须一致。");
        if (entries.Select(module => module.Launcher.Order).Distinct().Count() != entries.Length)
            throw new InvalidOperationException("工具目录中的显示顺序不能重复。");

        All = Array.AsReadOnly(entries.Select(module => module.Launcher).ToArray());
        _launcher = entries.ToDictionary(module => module.Descriptor.Id, module => module.Launcher);
        _descriptors = entries.ToDictionary(module => module.Descriptor.Id, module => module.Descriptor);
    }

    public IReadOnlyList<LauncherToolMetadata> All { get; }
    IReadOnlyCollection<ToolDescriptor> IToolRegistry.All => Array.AsReadOnly(_descriptors.Values.ToArray());

    public LauncherToolMetadata Get(ToolId id) => _launcher[id];
    public ToolDescriptor GetDescriptor(ToolId id) => _descriptors[id];
    ToolDescriptor IToolRegistry.Get(ToolId id) => GetDescriptor(id);
}
