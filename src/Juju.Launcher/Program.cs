using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;
using Juju.Update;

namespace Juju.Launcher;

internal static class Program
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        try
        {
            var store = new VersionStateStore(root);
            var state = await store.ReadAsync() ??
                        throw new InvalidDataException("juju 尚未完成安装：找不到活动版本记录。");

            if ( state.PendingVersion is { } candidate )
            {
                var candidateProcess = await TryStartCandidateAsync(root, candidate, args);
                if ( candidateProcess is not null )
                {
                    try
                    {
                        await store.CommitPendingAsync();
                        candidateProcess.Dispose();
                        return 0;
                    }
                    catch ( Exception ex )
                    {
                        StopCandidate(candidateProcess);
                        try
                        { await store.RollbackPendingAsync(); }
                        catch ( Exception ) { }
                        ShowError("新版本已启动，但无法提交版本状态；已尝试恢复旧版本。\n\n" + ex.Message);
                    }
                }
                else
                {
                    try
                    { await store.RollbackPendingAsync(); }
                    catch ( Exception ) { /* Continue with the unchanged active version if state writes fail. */ }
                    ShowError($"新版本 {candidate} 未能正常启动，已恢复到 {state.ActiveVersion}。");
                }
            }

            var current = await store.ReadAsync() ?? state;
            try
            {
                StartApplication(ResolveExecutable(root, current.ActiveVersion), root, args, null).Dispose();
            }
            catch ( Exception ex ) when ( current.PreviousVersion is not null )
            {
                var previous = current.PreviousVersion;
                StartApplication(ResolveExecutable(root, previous), root, args, null).Dispose();
                await store.WriteAsync(new VersionState(previous));
                ShowError($"当前版本 {current.ActiveVersion} 不可用，已启动回退版本 {previous}。\n\n{ex.Message}");
            }
            return 0;
        }
        catch ( Exception ex )
        {
            ShowError("juju 无法启动。\n\n" + ex.Message);
            return 1;
        }
    }

    private static async Task<Process?> TryStartCandidateAsync(string root, string candidate, string[] originalArgs)
    {
        string executable;
        try
        { executable = ResolveExecutable(root, candidate); }
        catch ( Exception ex ) when ( ex is IOException or InvalidDataException or UnauthorizedAccessException )
        {
            WriteDiagnostic(root, $"Candidate {candidate} could not be resolved: {ex}");
            return null;
        }
        var pipeName = "juju-startup-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        using var timeout = new CancellationTokenSource(StartupTimeout);
        Process? process = null;
        var keepProcess = false;
        try
        {
            process = StartApplication(executable, root, originalArgs, pipeName);
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync();
            if ( await Task.WhenAny(connected, exited) != connected )
            {
                WriteDiagnostic(root, $"Candidate {candidate} exited before readiness with code {process.ExitCode}.");
                timeout.Cancel();
                try
                { await connected; }
                catch ( OperationCanceledException ) { }
                catch ( IOException ) { }
                return null;
            }
            await connected;
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var ready = await reader.ReadLineAsync(timeout.Token);
            if ( ready != "READY" )
            {
                WriteDiagnostic(root, $"Candidate {candidate} did not send READY on the startup pipe.");
                return null;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            keepProcess = !process.HasExited;
            if ( !keepProcess )
                WriteDiagnostic(root, $"Candidate {candidate} exited immediately after readiness with code {process.ExitCode}.");
            return keepProcess ? process : null;
        }
        catch ( OperationCanceledException ex )
        {
            WriteDiagnostic(root, $"Candidate {candidate} startup confirmation timed out or was cancelled: {ex.Message}");
            return null;
        }
        catch ( Exception ex )
        {
            WriteDiagnostic(root, $"Candidate {candidate} failed during startup confirmation: {ex}");
            return null;
        }
        finally
        {
            if ( !keepProcess && process is { HasExited: false } )
                StopCandidate(process);
            if ( !keepProcess )
                process?.Dispose();
        }
    }

    private static void StopCandidate(Process process)
    {
        if ( !process.HasExited )
        {
            try
            { process.Kill(entireProcessTree: true); }
            catch ( InvalidOperationException ) { }
            catch ( System.ComponentModel.Win32Exception ) { }
            try
            { process.WaitForExit(); }
            catch ( InvalidOperationException ) { }
        }
        process.Dispose();
    }

    private static Process StartApplication(string executable, string root, string[] originalArgs,
        string? startupPipe)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        };
        start.Environment["JUJU_INSTALL_ROOT"] = root;
        for ( var index = 0; index < originalArgs.Length; index++ )
        {
            if ( string.Equals(originalArgs[index], "--launcher-pipe", StringComparison.OrdinalIgnoreCase) )
            {
                index++;
                continue;
            }
            start.ArgumentList.Add(originalArgs[index]);
        }
        if ( startupPipe is not null )
        {
            start.ArgumentList.Add("--launcher-pipe");
            start.ArgumentList.Add(startupPipe);
        }
        return Process.Start(start) ?? throw new InvalidOperationException("无法启动 juju 应用进程。");
    }

    private static string ResolveExecutable(string root, string version)
    {
        if ( !SemanticVersion.TryParse(version, out _) )
            throw new InvalidDataException("活动版本记录包含非法版本号。");
        var versionsRoot = Path.GetFullPath(Path.Combine(root, "versions"));
        var directory = Path.GetFullPath(Path.Combine(versionsRoot, version));
        if ( !directory.StartsWith(versionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) )
            throw new InvalidDataException("版本路径越出安装目录。");
        var executable = Path.Combine(directory, "Juju.App.exe");
        if ( !File.Exists(executable) )
            throw new FileNotFoundException("活动版本的 Juju.App.exe 不存在。", executable);
        return executable;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "juju", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static void WriteDiagnostic(string root, string message)
    {
        try
        {
            var stateDirectory = Path.Combine(root, "state");
            Directory.CreateDirectory(stateDirectory);
            File.AppendAllText(Path.Combine(stateDirectory, "launcher-startup.log"),
                $"[{DateTimeOffset.UtcNow:O}] {message}\r\n");
        }
        catch ( Exception ) { }
    }
}
