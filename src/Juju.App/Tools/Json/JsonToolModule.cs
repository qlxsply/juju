using Juju.App.Contracts.Windows;
using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;

namespace Juju.App.Tools.Json;

public sealed class JsonToolModule(IJsonWindowFactory window, JsonSettingsSection settings) : IToolModule
{
    public ToolDescriptor Descriptor { get; } = new(ToolId.Json, BackgroundCapability.Unsupported,
        StorageCapability.File);

    public LauncherToolMetadata Launcher { get; } = new(ToolId.Json, 0, "JSON 格式化",
        "格式化和压缩 JSON 数据", "打开 JSON 工具", "JsonToolIcon", null, "#6189DF", 24,
        ToolLaunchKeys.Json.ToString(), true);

    public IReadOnlyCollection<ISettingsSection> SettingsSections { get; } = [settings];
    public ToolReadiness CheckReadiness() => new(true);
    public void Open() => window.ShowJson();
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
