using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;

namespace Juju.App.Tools.Placeholders;

public sealed class PlaceholderToolModule(ToolId id, string name, string description, string glyph,
    string glyphColor, double glyphFontSize, char defaultKey, ISettingsSection settings) : IToolModule
{
    public ToolDescriptor Descriptor { get; } = new(id, BackgroundCapability.Unsupported, StorageCapability.File);
    public LauncherToolMetadata Launcher { get; } = new(id, id == ToolId.EchoHttp ? 2 : 3,
        name, description, $"打开 {name}", null, glyph, glyphColor, glyphFontSize,
        defaultKey.ToString(), false);

    public IReadOnlyCollection<ISettingsSection> SettingsSections { get; } = [settings];
    public ToolReadiness CheckReadiness() =>
        new(false, $"{id} 正在开发中。", ToolOpenMessageKind.Information, "juju");
    public void Open() { }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
