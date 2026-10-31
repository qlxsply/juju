using Juju.App.Contracts.Windows;
using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Juju.Tools.Toto.Application.Reminders;

namespace Juju.App.Tools.Toto;

public sealed class TotoToolModule(ITotoWindowFactory window, TotoReminderRuntime runtime,
    IServiceProvider services) : IToolModule
{
    public ToolDescriptor Descriptor { get; } = new(ToolId.Toto, BackgroundCapability.Required,
        StorageCapability.File);

    public LauncherToolMetadata Launcher { get; } = new(ToolId.Toto, 1, "Toto 日程提醒",
        "事项管理与到期自动提醒", "打开 Toto 日程提醒", "TotoToolIcon", null, "#409C95", 24,
        ToolLaunchKeys.Toto.ToString(), true);

    // Build the WPF settings view on first settings-window access, after the required background module starts.
    public IReadOnlyCollection<ISettingsSection> SettingsSections =>
        [services.GetRequiredService<TotoSettingsSection>()];

    public ToolReadiness CheckReadiness() => runtime.EnsureReady()
        ? new(true)
        : new(false, $"节假日日历尚未就绪，Toto 将继续重试：\n{runtime.InitializationError}",
            MessageTitle: "Toto");

    public void Open() => window.ShowToto();
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return runtime.StartAsync(cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        runtime.Dispose();
        return Task.CompletedTask;
    }
}
