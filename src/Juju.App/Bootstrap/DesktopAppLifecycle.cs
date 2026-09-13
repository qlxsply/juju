using Juju.Core.App;
using Juju.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Juju.App.Bootstrap;

public sealed class DesktopAppLifecycle(
    IServiceProvider services,
    DesktopWindowManager windows,
    IRuntimeManager runtimes,
    ISettingsService settings,
    WindowStateService states,
    TrayService tray,
    ILogger<DesktopAppLifecycle> log) : IAppLifecycle
{
    private int _stopping;

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        try
        {
            log.LogInformation("Application shutdown started");
            await windows.ShutdownAsync(states, cancellationToken);
            await settings.SaveAsync(settings.Current, cancellationToken);
            await runtimes.StopAllAsync(cancellationToken);
            tray.Dispose();
            if (services is IAsyncDisposable asyncServices) await asyncServices.DisposeAsync();
            else if (services is IDisposable disposable) disposable.Dispose();
            log.LogInformation("Application shutdown completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Application shutdown failed");
        }
    }
}