using System.IO.Pipes;
using System.Text;
using System.IO;
using System.Windows;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.App;
using Juju.Core.Tools;
using Juju.Core.Logging;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Storage;
using Juju.App.Bootstrap;
using Juju.App.Services;
using Juju.Platform.Windows.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Juju.App;

public partial class App : System.Windows.Application
{
    private const string InstanceName = "juju-single-instance-v1";
    private Mutex? _mutex;
    private CancellationTokenSource? _shutdown;
    private ServiceProvider? _services;
    private IWindowManager? _windows;
    private IToolManager? _tools;
    private IAppLifecycle? _lifecycle;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, InstanceName, out var firstInstance);
        if (!firstInstance) { await ActivateExistingInstanceAsync(); Shutdown(); return; }
        _shutdown = new CancellationTokenSource();
        var writer = new AtomicFileWriter();
        var settings = new SettingsService(writer);
        await settings.LoadAsync(_shutdown.Token);
        await new DataRootService(writer).EnsureInitializedAsync(settings.Current.DataRoot, _shutdown.Token);
        var roots = new DataRootService(writer);
        var storage = new StorageManager(roots, writer);
        await storage.InitializeAsync(settings.Current.DataRoot, _shutdown.Token);
        var documents = new JsonDocumentService(settings.Current.DataRoot, new JsonMetadataStore(settings.Current.DataRoot, writer), writer, new DocumentRevisionService());
        var services = new ServiceCollection();
        services.AddSingleton<IAtomicFileWriter>(writer);
        services.AddSingleton<ISettingsService>(settings);
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
        services.AddSingleton<IWindowManager>(provider => provider.GetRequiredService<DesktopWindowManager>());
        services.AddSingleton<IToolManager, ToolManager>();
        services.AddLogging(builder => builder.AddProvider(new RollingFileLoggerProvider(Path.Combine(ApplicationPaths.ApplicationDataDirectory, "logs"))));
        services.AddSingleton<TrayService>(provider => new TrayService(provider.GetRequiredService<IWindowManager>(), provider.GetRequiredService<IToolManager>(), () => _ = ExitAsync()));
        services.AddSingleton<IAppLifecycle, DesktopAppLifecycle>();
        _services = services.BuildServiceProvider();
        _windows = _services.GetRequiredService<IWindowManager>();
        _tools = _services.GetRequiredService<IToolManager>();
        _lifecycle = _services.GetRequiredService<IAppLifecycle>();
        await _services.GetRequiredService<WindowStateService>().LoadAsync(_shutdown.Token);
        _services.GetRequiredService<ThemeService>().Apply(settings.Current.Theme);
        _services.GetRequiredService<DesktopWindowManager>().InitializeLauncher();
        _ = _services.GetRequiredService<TrayService>();
        _ = ListenForActivationAsync(_shutdown.Token);
    }

    private async Task ExitAsync()
    {
        if (_shutdown is null) return;
        await _lifecycle!.ShutdownAsync(_shutdown.Token);
        _shutdown.Cancel();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel();
        _mutex?.ReleaseMutex(); _mutex?.Dispose();
        base.OnExit(e);
    }

    private async Task ListenForActivationAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                if (await reader.ReadLineAsync(cancellationToken) == "ActivateLauncher") Dispatcher.Invoke(() => _windows?.ShowLauncher());
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task ActivateExistingInstanceAsync()
    {
        try { await using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out, PipeOptions.Asynchronous); await pipe.ConnectAsync(1000); await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync("ActivateLauncher"); } catch (IOException) { }
    }
}
