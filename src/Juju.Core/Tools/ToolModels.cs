namespace Juju.Core.Tools;

/// <summary>应用内工具的稳定标识；枚举比字符串更适合作为注册表键，类似 Java 的 enum 单例。</summary>
public enum ToolId
{
    Json,
    EchoHttp,
    Kairos
}

/// <summary>描述工具后台运行的能力约束，供运行时决定是否需要启动后台进程。</summary>
public enum BackgroundCapability
{
    Unsupported,
    Optional,
    Required
}

/// <summary>声明工具所需的持久化介质，用于在宿主层统一配置存储。</summary>
public enum StorageCapability
{
    None,
    File,
    Database
}

/// <summary>窗口在界面层的可见状态，与后台 <see cref="RuntimeState"/> 分离。</summary>
public enum UiState
{
    Closed,
    Opening,
    Visible,
    Minimized
}

/// <summary>工具后台运行时的生命周期状态，便于调用方表达启动和失败中的过渡状态。</summary>
public enum RuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

/// <summary>
/// 工具窗口的不可变规格。位置参数同时定义构造函数和只读属性；这相当于 Java record，
/// 编译器还会生成基于值的相等性。
/// </summary>
public sealed record WindowSpec(string Key, double MinWidth, double MinHeight);

/// <summary>聚合工具启动、窗口和存储配置的不可变描述对象。</summary>
public sealed record ToolDescriptor(
    ToolId Id,
    string Name,
    string LauncherKey,
    BackgroundCapability Background,
    WindowSpec Window,
    StorageCapability Storage);

/// <summary>提供工具描述的只读注册表契约，便于以替身实现替换静态配置。</summary>
public interface IToolRegistry
{
    /// <summary>按稳定标识取得描述；未知标识由实现按其集合语义处理。</summary>
    ToolDescriptor Get(ToolId id);

    /// <summary>当前注册的全部工具的只读视图。</summary>
    IReadOnlyCollection<ToolDescriptor> All { get; }
}

/// <summary>内置工具注册表；字典使按 <see cref="ToolId"/> 查找保持常数时间复杂度。</summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<ToolId, ToolDescriptor> _tools = new Dictionary<ToolId, ToolDescriptor>
    {
        [ToolId.Json] = new(ToolId.Json, "JSON", "1", BackgroundCapability.Unsupported, new("json", 900, 600),
            StorageCapability.File),
        [ToolId.EchoHttp] = new(ToolId.EchoHttp, "EchoHttp", "", BackgroundCapability.Unsupported,
            new("echo-http", 900, 600), StorageCapability.File),
        [ToolId.Kairos] = new(ToolId.Kairos, "Kairos", "", BackgroundCapability.Unsupported, new("kairos", 900, 600),
            StorageCapability.File)
    };

    /// <summary>返回快照数组，避免调用方通过集合视图影响内部字典。</summary>
    public IReadOnlyCollection<ToolDescriptor> All => _tools.Values.ToArray();

    /// <summary>委托给字典索引器读取工具描述。</summary>
    public ToolDescriptor Get(ToolId id) => _tools[id];
}