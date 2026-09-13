using Juju.Core.App;
using Juju.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Juju.App.Bootstrap;

// 关停协调器从 DI 获取所有需要释放的应用服务，避免 App 直接了解每个基础设施的细节。
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

    // Interlocked 使重复退出请求（托盘、窗口、系统关机）只执行一次，无需在 UI 线程加锁。
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
            switch (services)
            {
                // 优先异步释放 DI 容器，以便其内部资源完成异步清理；否则退回标准 IDisposable 模式。
                case IAsyncDisposable asyncServices:
                    await asyncServices.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }

            log.LogInformation("Application shutdown completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Application shutdown failed");
        }
    }
}