namespace Juju.Core.Tools;

public enum ToolId
{
    Json,
    EchoHttp,
    Kairos
}

public enum BackgroundCapability
{
    Unsupported,
    Optional,
    Required
}

public enum StorageCapability
{
    None,
    File,
    Database
}

public enum UiState
{
    Closed,
    Opening,
    Visible,
    Minimized
}

public enum RuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed
}

public sealed record WindowSpec(string Key, double MinWidth, double MinHeight);

public sealed record ToolDescriptor(
    ToolId Id,
    string Name,
    string LauncherKey,
    BackgroundCapability Background,
    WindowSpec Window,
    StorageCapability Storage);

public interface IToolRegistry
{
    ToolDescriptor Get(ToolId id);
    IReadOnlyCollection<ToolDescriptor> All { get; }
}

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

    public IReadOnlyCollection<ToolDescriptor> All => _tools.Values.ToArray();
    public ToolDescriptor Get(ToolId id) => _tools[id];
}