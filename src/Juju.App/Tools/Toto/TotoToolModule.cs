using Juju.App.Contracts.Windows;
using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;
using Toto.App;

namespace Juju.App.Tools.Toto;

public sealed class TotoToolModule(ITotoToolWindow window, TotoToolHost runtime,
    TotoSettingsSection settings) : IToolModule
{
    public ToolDescriptor Descriptor { get; } = new(ToolId.Toto, BackgroundCapability.Required,
        StorageCapability.File);

    public LauncherToolMetadata Launcher { get; } = new(ToolId.Toto, 1, "Toto 日程提醒",
        "事项管理与到期自动提醒", "打开 Toto 日程提醒", "TotoToolIcon", null, "#409C95", 24,
        ToolLaunchKeys.Toto.ToString(), true);

    public IReadOnlyCollection<ISettingsSection> SettingsSections { get; } = [settings];

    public ToolReadiness CheckReadiness() => runtime.EnsureReady()
        ? new(true)
        : new(false, $"节假日日历尚未就绪，Toto 将继续重试：\n{runtime.InitializationError}",
            MessageTitle: "Toto");

    public void Open() => window.ShowToto();
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runtime.EnsureReady();
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        runtime.Dispose();
        return Task.CompletedTask;
    }
}
