using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using Juju.App.Bootstrap;
using Juju.App.Contracts.Bootstrap;
using Juju.App.Contracts.Windows;
using Juju.App.Presentation.Json;
using Juju.App.Presentation.Toto;
using Juju.App.Services;
using Juju.App.Settings;
using Juju.App.Tools.Catalog;
using Juju.App.Tools.Contracts;
using Juju.App.Tools.Json;
using Juju.App.Tools.Placeholders;
using Juju.App.Tools.Routing;
using Juju.App.Tools.Toto;
using Juju.App.Views;
using Juju.Core.Contracts.Platform;
using Juju.Core.Logging;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.Platform.Windows.Dialogs;
using Juju.Platform.Windows.Lifecycle;
using Juju.Platform.Windows.Shell;
using Juju.Platform.Windows.Startup;
using Juju.Platform.Windows.Windowing;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Settings;
using Juju.Tools.Json.Storage;
using Juju.Tools.Toto.Application;
using Juju.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace Juju.App;

// XAML 编译器会从 App.xaml 生成同名的另一部分 partial 类，并在其中提供资源、入口配置等成员。
// 与 JavaFX 的 Application 类似，WPF 由框架创建 App 实例，再依次调用下面的生命周期方法。
public partial class App
{
    private string _instanceName = "";
    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _shutdown;
    private ServiceProvider? _services;
    private IAppWindowManager? _windows;
    private IToolManager? _tools;
    private IAppLifecycle? _lifecycle;
    private GitHubUpdateService? _updateService;
    private bool _installerHealthCheck;

    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            LogStartupFailure(eventArgs.Exception);
            if ( _installerHealthCheck )
            {
                eventArgs.Handled = true;
                Environment.ExitCode = 1;
                Shutdown(1);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if ( eventArgs.ExceptionObject is Exception exception )
                LogStartupFailure(exception);
        };
    }

    // WPF 启动回调必须保持 void 签名，因此 async void 只用于此类框架事件入口；异常不能由调用方 await。
    // 初始化顺序刻意是：单实例检查 -> 可持久化服务加载 -> DI 容器构建 -> UI 服务启动。
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _installerHealthCheck = e.Args.Any(argument => string.Equals(argument, "--installer-health-check",
            StringComparison.OrdinalIgnoreCase));
        if ( _installerHealthCheck && string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("JUJU_HEALTH_CHECK_DATA_ROOT")) )
        {
            Environment.ExitCode = 1;
            Shutdown(1);
            return;
        }
        // 命名 Mutex 由操作系统跨进程共享，作用相当于 Java 中基于文件锁实现的单实例守卫。
        _instanceName = GetInstanceName(ApplicationPaths.InstallDirectory);
        _mutex = new Mutex(true, _instanceName, out var firstInstance);
        _ownsMutex = firstInstance;
        if ( !firstInstance )
        {
            await ActivateExistingInstanceAsync(_instanceName, e.Args);
            Shutdown();
            return;
        }

        if ( !_installerHealthCheck )
            WriteInstanceMarker(ApplicationPaths.InstallDirectory, _instanceName);

        _shutdown = new CancellationTokenSource();
        var writer = new AtomicFileWriter();
        _updateService = new GitHubUpdateService(ApplicationPaths.InstallDirectory);
        await new DataRootService(writer).EnsureInitializedAsync(ApplicationPaths.DataRoot, _shutdown.Token);
        var settings = new SettingsService(writer);
        await settings.LoadAsync(_shutdown.Token);
        if ( Array.Exists(e.Args, argument => string.Equals(argument, "--show-launcher",
                StringComparison.OrdinalIgnoreCase)) && settings.Current.StartMinimized )
            await settings.SaveAsync(settings.Current with { StartMinimized = false }, _shutdown.Token);
        ApplicationPaths.ConfigureToolDirectories(settings.Current.ToolDataDirectories);
        Directory.CreateDirectory(Path.Combine(ApplicationPaths.ToolDataDirectory("json"), "documents"));
        Directory.CreateDirectory(Path.Combine(ApplicationPaths.ToolDataDirectory("json"), ".trash"));
        var roots = new DataRootService(writer);
        var storage = new StorageManager(roots, writer);
        await storage.InitializeAsync(ApplicationPaths.DataRoot, _shutdown.Token);
        var jsonSettings = new JsonToolSettingsService(
            Path.Combine(ApplicationPaths.ToolDataDirectory("json"), "settings.json"), writer);
        await jsonSettings.LoadAsync(_shutdown.Token);
        var documents = new JsonDocumentService(ApplicationPaths.ToolDataDirectory("json"),
            new JsonMetadataStore(ApplicationPaths.ToolDataDirectory("json"), writer), writer,
            new DocumentRevisionService());
        // Composition Root：唯一集中组装依赖的地方。界面和业务代码只声明构造函数依赖，
        // 而不自行 new 基础设施对象，类似 Java 的 Spring 容器配置，但这里是显式注册。
        var services = new ServiceCollection();
        services.AddSingleton<IAtomicFileWriter>(writer);
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton(_updateService);
        services.AddSingleton<IJsonToolSettingsService>(jsonSettings);
        services.AddSingleton<ISettingsSection, SystemSettingsSection>();
        services.AddSingleton<ISettingsSection>(provider => new ShortcutSettingsSection(
            provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<SettingsDialogService>(),
            provider.GetRequiredService<IJsonToolSettingsService>(),
            provider.GetRequiredService<ITotoApplicationService>()));
        services.AddSingleton<JsonSettingsSection>(provider => new JsonSettingsSection(
            provider.GetRequiredService<IJsonToolSettingsService>(), provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<DataDirectoryChangeService>(),
            (tool, shortcut) => provider.GetRequiredService<DesktopWindowManager>().TryRegisterToolHotkey(tool, shortcut),
            provider.GetRequiredService<SettingsDialogService>(),
            provider.GetRequiredService<ITotoApplicationService>()));
        services.AddSingleton<ISettingsSection>(provider => new StorageSettingsSection(
            provider.GetRequiredService<DataDirectoryChangeService>(),
            provider.GetRequiredService<SettingsDialogService>(), provider.GetRequiredService<ISettingsService>()));
        services.AddSingleton<ISettingsSection, DisplaySettingsSection>();
        services.AddSingleton<ISettingsSection>(_ => new AboutSettingsSection(_updateService, BeginUpdateAsync));
        services.AddSingleton(roots);
        services.AddSingleton<DataDirectoryChangeService>();
        services.AddSingleton<IStorageManager>(storage);
        services.AddSingleton(documents);
        services.AddTotoApplicationServices(ApplicationPaths.ToolDataDirectory("toto"));
        services.AddSingleton<DesktopWindowManager>();
        services.AddSingleton<WindowStateService>();
        services.AddSingleton<SettingsDialogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IJsonFileClipboard, JsonFileClipboard>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<ISystemLifecycleEvents, WindowsSystemLifecycleEvents>();
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();
        services.AddSingleton<IWorkAreaProvider, WindowsWorkAreaProvider>();
        services.AddSingleton<ITrayIconService>(_ =>
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/juju.ico"))!.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return new NotifyIconService(buffer.ToArray());
        });
        // 所有窗口端口共享一个适配器实例，避免重复创建工具窗口缓存。
        services.AddSingleton<IAppWindowManager>(provider => provider.GetRequiredService<DesktopWindowManager>());
        services.AddSingleton<JsonWindowFactory>();
        services.AddSingleton<IJsonWindowFactory>(provider => provider.GetRequiredService<JsonWindowFactory>());
        services.AddSingleton<TotoWindowFactory>();
        services.AddSingleton<ITotoWindowFactory>(provider => provider.GetRequiredService<TotoWindowFactory>());
        services.AddJsonToolModule();
        services.AddTotoToolModule();
        services.AddPlaceholderToolModule(ToolId.EchoHttp, "EchoHttp", "HTTP 工具 · 正在开发中",
            "⇄", "#DF9952", 24, ToolLaunchKeys.EchoHttp);
        services.AddPlaceholderToolModule(ToolId.Kairos, "Kairos", "时间工具 · 正在开发中",
            "◷", "#6B66DC", 26, ToolLaunchKeys.Kairos);
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<IToolCatalog>(provider => provider.GetRequiredService<ToolRegistry>());
        services.AddSingleton<IToolRegistry>(provider => provider.GetRequiredService<ToolRegistry>());
        services.AddSingleton<IToolOpenFeedback, ToolOpenFeedback>();
        services.AddSingleton<IToolManager, ToolManager>();
        services.AddLogging(builder =>
            builder.AddProvider(
                new RollingFileLoggerProvider(Path.Combine(ApplicationPaths.ApplicationDataDirectory, "logs"))));
        // 托盘菜单只能接收同步 Action；丢弃 Task 是有意的 fire-and-forget 适配，
        // ExitAsync 自己负责完整关闭流程，而非在 UI 回调中阻塞线程。
        services.AddSingleton<TrayController>(provider => new TrayController(
            provider.GetRequiredService<ITrayIconService>(), provider.GetRequiredService<IAppWindowManager>(),
            () => _ = ExitAsync(), provider.GetRequiredService<GitHubUpdateService>(),
            typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"));
        services.AddSingleton<IAppLifecycle, DesktopAppLifecycle>();
        _services = services.BuildServiceProvider();
        _services.GetRequiredService<DataDirectoryChangeService>().LoadPending(
            settings.Current.PendingDataRoot, settings.Current.PendingToolDataDirectories);
        _windows = _services.GetRequiredService<IAppWindowManager>();
        _tools = _services.GetRequiredService<IToolManager>();
        if ( !_installerHealthCheck )
            _lifecycle = _services.GetRequiredService<IAppLifecycle>();
        await _services.GetRequiredService<WindowStateService>().LoadAsync(_shutdown.Token);
        _services.GetRequiredService<ThemeService>().Apply(settings.Current.Theme);
        await _tools.StartAsync(_shutdown.Token);
        if ( !_installerHealthCheck )
        {
            _services.GetRequiredService<DesktopWindowManager>().InitializeLauncher();
            _ = _services.GetRequiredService<TrayController>();
        }
        // 后台管道监听与 UI 生命周期并行运行；其取消令牌由退出流程统一触发。
        if ( !_installerHealthCheck )
            _ = ListenForActivationAsync(_instanceName, _shutdown.Token);
        if ( Array.Exists(e.Args, argument => string.Equals(argument, "--shutdown",
                StringComparison.OrdinalIgnoreCase)) )
        {
            await ExitAsync();
            return;
        }
        if ( _installerHealthCheck )
        {
            await SignalLauncherReadyAsync(e.Args, _shutdown.Token, required: true);
            await Task.Delay(TimeSpan.FromSeconds(2), _shutdown.Token);
            await _tools.StopAllAsync(_shutdown.Token);
            if ( _services is IAsyncDisposable asyncServices )
                await asyncServices.DisposeAsync();
            else
                _services.Dispose();
            _shutdown.Cancel();
            Shutdown(0);
            return;
        }

        await SignalLauncherReadyAsync(e.Args, _shutdown.Token);
        _ = CheckForUpdatesAtStartupAsync(_updateService, typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            _shutdown.Token);
    }

    // 托盘等多条路径都可请求退出；实际幂等性由 IAppLifecycle 实现保证。
    private async Task ExitAsync()
    {
        if ( _shutdown is null )
            return;
        if ( !await _lifecycle!.ShutdownAsync(_shutdown.Token) )
        {
            MessageBox.Show("应用未能完成安全保存和关闭。请解决保存问题后重试。", "juju",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _shutdown.Cancel();
        Shutdown();
    }

    private async Task CheckForUpdatesAtStartupAsync(GitHubUpdateService updates, string currentVersion,
        CancellationToken cancellationToken)
    {
        try
        { await updates.CheckAsync(currentVersion, manual: false, cancellationToken); }
        catch ( OperationCanceledException ) { }
        catch ( Exception ) { /* Update availability never blocks normal application startup. */ }
    }

    private async Task BeginUpdateAsync(PreparedUpdate update)
    {
        if ( _shutdown is null || _lifecycle is null )
            throw new InvalidOperationException("应用尚未完成启动。");

        var root = ApplicationPaths.InstallDirectory;
        var installedUpdater = Path.Combine(root, "Juju.Updater.exe");
        if ( !File.Exists(installedUpdater) )
            throw new FileNotFoundException("更新辅助程序不存在，请重新安装 juju。", installedUpdater);

        var helperDirectory = Path.Combine(Path.GetTempPath(), "juju-updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperDirectory);
        var helperPath = Path.Combine(helperDirectory, "Juju.Updater.exe");
        var gatePath = Path.Combine(helperDirectory, "continue-update");
        File.Copy(installedUpdater, helperPath);
        await File.WriteAllTextAsync(gatePath, "WAIT", _shutdown.Token);

        var start = new ProcessStartInfo(helperPath) { WorkingDirectory = helperDirectory, UseShellExecute = false };
        AddArgument(start, "root", root);
        AddArgument(start, "package", update.PackagePath);
        AddArgument(start, "manifest", update.ManifestPath);
        AddArgument(start, "version", update.Manifest.Version);
        AddArgument(start, "pid", Environment.ProcessId.ToString());
        AddArgument(start, "gate", gatePath);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("无法启动更新辅助程序。");

        if ( !await _lifecycle.ShutdownAsync(_shutdown.Token) )
        {
            try
            { helper.Kill(entireProcessTree: true); }
            catch ( InvalidOperationException ) { }
            throw new InvalidOperationException("当前数据未能安全保存，更新已取消，应用仍保持运行。");
        }

        try
        {
            await using var gate = new FileStream(gatePath, FileMode.Create, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough);
            var bytes = Encoding.UTF8.GetBytes("GO");
            await gate.WriteAsync(bytes);
            await gate.FlushAsync();
            gate.Flush(flushToDisk: true);
        }
        catch ( Exception ex )
        {
            MessageBox.Show("应用已完成安全关闭，但更新启动标记写入失败。更新辅助程序将恢复当前版本。\n\n" + ex.Message,
                "juju 更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _shutdown.Cancel();
        Shutdown();
    }

    private static void AddArgument(ProcessStartInfo process, string name, string value)
    {
        process.ArgumentList.Add("--" + name);
        process.ArgumentList.Add(value);
    }

    // 无论正常退出还是启动失败后的 Shutdown，WPF 都会进入此处释放进程级资源。
    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel();
        if ( _ownsMutex )
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // 首实例作为命名管道服务端，后续进程只发送一条激活消息后退出。
    private async Task ListenForActivationAsync(string instanceName, CancellationToken cancellationToken)
    {
        while ( !cancellationToken.IsCancellationRequested )
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(instanceName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                // 管道 I/O 不保证在 WPF Dispatcher 线程上完成；修改窗口必须切回 UI 线程。
                var command = await reader.ReadLineAsync(cancellationToken);
                if ( command == "ActivateLauncher" )
                    Dispatcher.Invoke(() => _windows?.ShowLauncher());
                else if ( command == "Shutdown" )
                    _ = Dispatcher.BeginInvoke(new Action(() => _ = ExitAsync()));
            }
            catch ( OperationCanceledException )
            {
                return;
            }
        }
    }

    // 第二个进程不构建 DI 或窗口，仅尝试通知已运行的首实例；连接竞态失败时静默结束。
    private static async Task ActivateExistingInstanceAsync(string instanceName, IReadOnlyList<string> args)
    {
        try
        {
            await using var pipe =
                new NamedPipeClientStream(".", instanceName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(1000);
            await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            var command = args.Any(argument => string.Equals(argument, "--shutdown", StringComparison.OrdinalIgnoreCase))
                ? "Shutdown"
                : "ActivateLauncher";
            await writer.WriteLineAsync(command);
        }
        catch ( IOException )
        {
        }
    }

    private static string GetInstanceName(string installRoot)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot)).ToUpperInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return "juju-single-instance-" + hash[..24];
    }

    private static void WriteInstanceMarker(string installRoot, string instanceName)
    {
        try
        {
            var stateDirectory = Path.Combine(installRoot, "state");
            Directory.CreateDirectory(stateDirectory);
            File.WriteAllText(Path.Combine(stateDirectory, "instance-name.txt"), instanceName, Encoding.ASCII);
        }
        catch ( Exception ex )
        {
            LogStartupFailure(ex);
        }
    }

    private static void LogStartupFailure(Exception exception)
    {
        try
        {
            var state = Path.Combine(ApplicationPaths.InstallDirectory, "state");
            Directory.CreateDirectory(state);
            File.AppendAllText(Path.Combine(state, "startup-errors.log"),
                $"[{DateTimeOffset.UtcNow:O}] {exception}\r\n\r\n");
        }
        catch ( Exception ) { }
    }

    private static async Task SignalLauncherReadyAsync(IReadOnlyList<string> args, CancellationToken cancellationToken,
        bool required = false)
    {
        for ( var index = 0; index + 1 < args.Count; index++ )
        {
            if ( !string.Equals(args[index], "--launcher-pipe", StringComparison.OrdinalIgnoreCase) )
                continue;
            try
            {
                await using var pipe = new NamedPipeClientStream(".", args[index + 1], PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(15_000, cancellationToken);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync("READY");
                return;
            }
            catch ( Exception ex ) when ( ex is IOException or TimeoutException or OperationCanceledException )
            {
                if ( required )
                    throw new InvalidOperationException("无法向安装器健康检查管道报告 READY。", ex);
                // Launcher independently times out and rolls back if this process cannot confirm readiness.
            }

            return;
        }
        if ( required )
            throw new InvalidOperationException("健康检查缺少 Launcher 管道参数。");
    }
}
