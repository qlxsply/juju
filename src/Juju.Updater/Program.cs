using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Juju.Update;

namespace Juju.Updater;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var options = ParseArguments(args);
        var root = Path.GetFullPath(Required(options, "root"));
        var package = Path.GetFullPath(Required(options, "package"));
        var manifestPath = Path.GetFullPath(Required(options, "manifest"));
        var gatePath = Path.GetFullPath(Required(options, "gate"));
        var version = Required(options, "version");
        if ( !int.TryParse(Required(options, "pid"), out var processId) )
            throw new ArgumentException("Invalid application process ID.");

        int? launcherProcessId = null;
        InstallRootLock.Lease? installLock = null;
        try
        {
            installLock = await InstallRootLock.AcquireAsync(root, TimeSpan.FromSeconds(1));

            await WaitForApplicationExitAsync(processId);
            var continueSignal = await File.ReadAllTextAsync(gatePath);
            if ( continueSignal != "GO" )
                throw new InvalidOperationException("应用未确认安全关闭，已取消版本切换。");
            var manifest = ReleaseManifest.Deserialize(await File.ReadAllBytesAsync(manifestPath));
            manifest.Validate(version);
            var launcher = Path.Combine(root, "Juju.Launcher.exe");
            if ( !File.Exists(launcher) )
                throw new FileNotFoundException("稳定启动程序不存在。", launcher);

            var launcherVersion = FileVersionInfo.GetVersionInfo(launcher).ProductVersion?.Split('+')[0];
            if ( launcherVersion is null || !SemanticVersion.TryParse(launcherVersion, out _) )
                throw new InvalidDataException("无法验证 Launcher 版本号。");
            var stage = await VersionPackageStager.StageAsync(package, root, manifest, launcherVersion);
            var finalVersion = Path.Combine(root, "versions", version);
            Directory.CreateDirectory(Path.GetDirectoryName(finalVersion)!);
            if ( Directory.Exists(finalVersion) )
            {
                var installed = await new VersionStateStore(root).ReadAsync();
                if ( installed is not null &&
                     (installed.ActiveVersion == version || installed.PreviousVersion == version ||
                      installed.PendingVersion == version) )
                    throw new IOException("该版本已被活动状态引用，拒绝覆盖。");
                VersionDirectoryManager.DeleteTreeWithoutFollowingLinks(finalVersion);
            }
            Directory.Move(stage, finalVersion);

            var store = new VersionStateStore(root);
            await store.BeginPendingAsync(version, lockLease: installLock);
            installLock.Dispose();
            installLock = null;
            using var launcherProcess = Process.Start(new ProcessStartInfo(launcher)
            {
                WorkingDirectory = root,
                UseShellExecute = true
            }) ?? throw new InvalidOperationException("无法启动稳定启动程序。");
            launcherProcessId = launcherProcess.Id;
            await launcherProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            if ( launcherProcess.ExitCode == 0 )
            {
                var current = await store.ReadAsync();
                if ( current is not null )
                    VersionDirectoryManager.Prune(root, current);
                TryDelete(package);
                TryDelete(manifestPath);
                TryDelete(gatePath);
            }
            return launcherProcess.ExitCode;
        }
        catch ( Exception ex )
        {
            WriteDiagnostic(root, ex);
            ShowError("juju 更新失败，正在尝试启动当前版本。\n\n" + ex.Message);
            if ( launcherProcessId is null || !IsProcessRunning(launcherProcessId.Value) )
                TryLaunchCurrent(root);
            return 1;
        }
        finally
        {
            installLock?.Dispose();
            ScheduleTemporaryHelperCleanup();
        }
    }

    private static async Task WaitForApplicationExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch ( ArgumentException )
        {
            // The app exited before the helper began waiting.
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for ( var index = 0; index < args.Length; index++ )
        {
            if ( !args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length )
                throw new ArgumentException("Updater arguments must be --name value pairs.");
            result.Add(args[index][2..], args[++index]);
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing updater argument --{key}.");

    private static void TryLaunchCurrent(string root)
    {
        try
        {
            var launcher = Path.Combine(root, "Juju.Launcher.exe");
            if ( File.Exists(launcher) )
                Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = root, UseShellExecute = true });
        }
        catch ( Exception ) { }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        { return !Process.GetProcessById(processId).HasExited; }
        catch ( ArgumentException ) { return false; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if ( File.Exists(path) )
                File.Delete(path);
        }
        catch ( IOException ) { }
        catch ( UnauthorizedAccessException ) { }
    }

    private static void WriteDiagnostic(string root, Exception error)
    {
        try
        {
            var state = Path.Combine(root, "state");
            Directory.CreateDirectory(state);
            File.AppendAllText(Path.Combine(state, "update-errors.log"),
                $"[{DateTimeOffset.UtcNow:O}] {error}\r\n\r\n");
        }
        catch ( Exception ) { }
    }

    private static void ScheduleTemporaryHelperCleanup()
    {
        var executable = Environment.ProcessPath;
        if ( string.IsNullOrWhiteSpace(executable) )
            return;
        var directory = Path.GetDirectoryName(executable);
        var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "juju-updater"));
        if ( string.IsNullOrWhiteSpace(directory) ||
             !Path.GetFullPath(directory).StartsWith(tempRoot + Path.DirectorySeparatorChar,
                 StringComparison.OrdinalIgnoreCase) )
            return;

        try
        {
            foreach ( var file in Directory.EnumerateFiles(directory) )
                MoveFileEx(file, null, MoveFileDelayUntilReboot);
            MoveFileEx(directory, null, MoveFileDelayUntilReboot);
        }
        catch ( Exception ) { }
    }

    private const uint MoveFileDelayUntilReboot = 0x4;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);

    private static void ShowError(string message) =>
        MessageBox.Show(message, "juju 更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
