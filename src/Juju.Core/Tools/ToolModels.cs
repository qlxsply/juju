namespace Juju.Core.Tools;

/// <summary>应用内工具的稳定标识。</summary>
public enum ToolId
{
    Json,
    Toto,
    EchoHttp,
    Kairos
}

/// <summary>工具后台运行能力。</summary>
public enum BackgroundCapability
{
    Unsupported,
    Optional,
    Required
}

/// <summary>工具需要的持久化介质。</summary>
public enum StorageCapability
{
    None,
    File,
    Database
}

/// <summary>跨工具可共享的能力描述；展示文案、图标和窗口规格归 Juju.App。</summary>
public sealed record ToolDescriptor(ToolId Id, BackgroundCapability Background, StorageCapability Storage);

/// <summary>提供稳定工具描述的只读注册表契约。</summary>
public interface IToolRegistry
{
    ToolDescriptor Get(ToolId id);
    IReadOnlyCollection<ToolDescriptor> All { get; }
}
