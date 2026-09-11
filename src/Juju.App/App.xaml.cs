using System.IO.Pipes;
using System.Text;
using System.IO;
using System.Windows;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Storage;

namespace Juju.App;

public partial class App : System.Windows.Application
{
    private const string InstanceName = "juju-single-instance-v1";
    private Mutex? _mutex;
    private CancellationTokenSource? _shutdown;
    private LauncherWindow? _launcher;
    private JsonDocumentService? _documents;
    private System.Windows.Forms.NotifyIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, InstanceName, out var firstInstance);
        if (!firstInstance) { await ActivateExistingInstanceAsync(); Shutdown(); return; }
        _shutdown = new CancellationTokenSource();
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "juju");
        Directory.CreateDirectory(localRoot);
        var settings = AppSettings.CreateDefault();
        var writer = new AtomicFileWriter();
        await new DataRootService(writer).EnsureInitializedAsync(settings.DataRoot, _shutdown.Token);
        _documents = new JsonDocumentService(settings.DataRoot, new JsonMetadataStore(settings.DataRoot, writer), writer, new DocumentRevisionService());
        _launcher = new LauncherWindow(OpenJson, OpenSettings);
        _launcher.Show();
        _launcher.Hide();
        CreateTray();
        _ = ListenForActivationAsync(_shutdown.Token);
    }

    private void CreateTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 Launcher", null, (_, _) => _launcher?.Toggle());
        menu.Items.Add("JSON", null, (_, _) => OpenJson());
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("退出 juju", null, (_, _) => Shutdown());
        _tray = new System.Windows.Forms.NotifyIcon { Text = "juju", Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => _launcher?.Toggle();
    }

    private void OpenJson() { if (_documents is not null) JsonToolWindow.ShowOrActivate(_documents); }
    private void OpenSettings() => SettingsWindow.ShowOrActivate();

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown?.Cancel(); _tray?.Dispose(); _mutex?.ReleaseMutex(); _mutex?.Dispose();
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
                if (await reader.ReadLineAsync(cancellationToken) == "ActivateLauncher") Dispatcher.Invoke(() => _launcher?.Toggle());
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task ActivateExistingInstanceAsync()
    {
        try { await using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out, PipeOptions.Asynchronous); await pipe.ConnectAsync(1000); await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true }; await writer.WriteLineAsync("ActivateLauncher"); } catch (IOException) { }
    }
}
