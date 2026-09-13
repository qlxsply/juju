using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using Juju.App.Bootstrap;
using Juju.App.Services;
using Juju.App.Settings;
using Juju.App.Views;
using Juju.Core.App;
using Juju.Core.Logging;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.Platform.Windows.Startup;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Settings;
using Juju.Tools.Json.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace Juju.App;

// XAML 编译器会从 App.xaml 生成同名的另一部分 partial 类，并在其中提供资源、入口配置等成员。
// 与 JavaFX 的 Application 类似，WPF 由框架创建 App 实例，再依次调用下面的生命周期方法。
public partial class App
{
    private const string InstanceName = "juju-single-instance-v1";
    private Mutex? _mutex;
    private CancellationTokenSource? _shutdown;
    private ServiceProvider? _services;
    private IWindowManager? _windows;
    private IToolManager? _tools;
    private IAppLifecycle? _lifecycle;

    // WPF 启动回调必须保持 void 签名，因此 async void 只用于此类框架事件入口；异常不能由调用方 await。
    // 初始化顺序刻意是：单实例检查 -> 可持久化服务加载 -> DI 容器构建 -> UI 服务启动。
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 命名 Mutex 由操作系统跨进程共享，作用相当于 Java 中基于文件锁实现的单实例守卫。
        _mutex = new Mutex(true, InstanceName, out var firstInstance);
        if (!firstInstance)
        {
            await ActivateExistingInstanceAsync();
            Shutdown();
            return;
        }

        _shutdown = new CancellationTokenSource();
        var writer = new AtomicFileWriter();
        var settings = new SettingsService(writer);
        await settings.LoadAsync(_shutdown.Token);
        await new DataRootService(writer).EnsureInitializedAsync(ApplicationPaths.DataRoot, _shutdown.Token);
        var roots = new DataRootService(writer);
        var storage = new StorageManager(roots, writer);
        await storage.InitializeAsync(ApplicationPaths.DataRoot, _shutdown.Token);
        var jsonSettings = new JsonToolSettingsService(writer);
        await jsonSettings.LoadAsync(_shutdown.Token);
        var documents = new JsonDocumentService(ApplicationPaths.DataRoot,
            new JsonMetadataStore(ApplicationPaths.DataRoot, writer), writer, new DocumentRevisionService());
        // Composition Root：唯一集中组装依赖的地方。界面和业务代码只声明构造函数依赖，
        // 而不自行 new 基础设施对象，类似 Java 的 Spring 容器配置，但这里是显式注册。
        var services = new ServiceCollection();
        services.AddSingleton<IAtomicFileWriter>(writer);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton<IJsonToolSettingsService>(jsonSettings);
        services.AddSingleton<ISettingsSection, SystemSettingsSection>();
        services.AddSingleton<ISettingsSection, JsonSettingsSection>();
        services.AddSingleton(roots);
        services.AddSingleton<IStorageManager>(storage);
        services.AddSingleton(documents);
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IRuntimeManager, RuntimeManager>();
        services.AddSingleton<DesktopWindowManager>();
        services.AddSingleton<WindowStateService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IJsonFileClipboard, JsonFileClipboard>();
        services.AddSingleton<IStartupService, StartupService>();
        // 以接口暴露同一个具体单例，避免 IWindowManager 与 DesktopWindowManager 各创建一份窗口缓存。
        services.AddSingleton<IWindowManager>(provider => provider.GetRequiredService<DesktopWindowManager>());
        services.AddSingleton<IToolManager, ToolManager>();
        services.AddLogging(builder =>
            builder.AddProvider(
                new RollingFileLoggerProvider(Path.Combine(ApplicationPaths.ApplicationDataDirectory, "logs"))));
        // 托盘菜单只能接收同步 Action；丢弃 Task 是有意的 fire-and-forget 适配，
        // ExitAsync 自己负责完整关闭流程，而非在 UI 回调中阻塞线程。
        services.AddSingleton<TrayService>(provider => new TrayService(provider.GetRequiredService<IWindowManager>(),
            provider.GetRequiredService<IToolManager>(), () => _ = ExitAsync()));
        services.AddSingleton<IAppLifecycle, DesktopAppLifecycle>();
        _services = services.BuildServiceProvider();
        _windows = _services.GetRequiredService<IWindowManager>();
        _tools = _services.GetRequiredService<IToolManager>();
        _lifecycle = _services.GetRequiredService<IAppLifecycle>();
        await _services.GetRequiredService<WindowStateService>().LoadAsync(_shutdown.Token);
        _services.GetRequiredService<ThemeService>().Apply(settings.Current.Theme);
        _services.GetRequiredService<DesktopWindowManager>().InitializeLauncher();
        _ = _services.GetRequiredService<TrayService>();
        // 后台管道监听与 UI 生命周期并行运行；其取消令牌由退出流程统一触发。
        _ = ListenForActivationAsync(_shutdown.Token);
    }

    // 托盘等多条路径都可请求退出；实际幂等性由 IAppLifecycle 实现保证。
    private async Task ExitAsync()
    {
        if (_shutdown is null) return;
        await _lifecycle!.ShutdownAsync(_shutdown.Token);
        _shutdown.Cancel();
        Shutdown();
    }

    // 无论正常退出还是启动失败后的 Shutdown，WPF 都会进入此处释放进程级资源。
    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // 首实例作为命名管道服务端，后续进程只发送一条激活消息后退出。
    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                // 管道 I/O 不保证在 WPF Dispatcher 线程上完成；修改窗口必须切回 UI 线程。
                if (await reader.ReadLineAsync(cancellationToken) == "ActivateLauncher")
                    Dispatcher.Invoke(() => _windows?.ShowLauncher());
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // 第二个进程不构建 DI 或窗口，仅尝试通知已运行的首实例；连接竞态失败时静默结束。
    private static async Task ActivateExistingInstanceAsync()
    {
        try
        {
            await using var pipe =
                new NamedPipeClientStream(".", InstanceName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("ActivateLauncher");
        }
        catch (IOException)
        {
        }
    }
}