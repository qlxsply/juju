using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Windows.Forms;
using Juju.Update;

namespace Juju.Launcher;

internal static class Program
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);
    private const int InstallerActivationFailed = 10;
    private const int InstallerCleanupIncomplete = 20;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        if ( args.Length >= 2 && string.Equals(args[0], "--restart-after-pid", StringComparison.OrdinalIgnoreCase) )
        {
            if ( !int.TryParse(args[1], out var processId) || processId <= 0 )
            {
                ShowError("无法安全重启 juju：应用进程标识无效。");
                return 1;
            }
            await WaitForProcessExitAsync(processId);
            args = args[2..];
        }

        if ( args.Length > 0 && string.Equals(args[0], "--installer-activate", StringComparison.OrdinalIgnoreCase) )
            return await RunInstallerActivationAsync(args);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        try
        {
            using var installLock = await InstallRootLock.AcquireAsync(root);
            var store = new VersionStateStore(root);
            var state = await store.ReadAsync(lockLease: installLock) ??
                        throw new InvalidDataException("juju 尚未完成安装：找不到活动版本记录。");

            if ( state.InstallerCleanupPending )
            {
                try
                {
                    VersionDirectoryManager.PruneForInstaller(root, state.ActiveVersion);
                    await store.CompleteInstallerCleanupAsync(state.ActiveVersion, lockLease: installLock);
                    state = await store.ReadAsync(lockLease: installLock) ?? state;
                }
                catch ( Exception ex )
                {
                    WriteDiagnostic(root, $"Installer activation already committed; cleanup retry failed: {ex}");
                    ShowError("juju 新版本已激活，但旧版本清理尚未完成。请检查 state\\installer-activation.log 并重试安装。");
                    return InstallerCleanupIncomplete;
                }
            }

            if ( state.PendingVersion is { } installerCandidate && state.InstallerManifestSha256 is { } installerHash )
            {
                installLock.Dispose();
                var activationResult = await RunInstallerActivationAsync([
                    "--installer-activate", "--version", installerCandidate, "--root", root,
                    "--manifest", Path.Combine(root, "staging", "installer-payload-manifest.json"),
                    "--manifest-sha256", installerHash
                ]);
                if ( activationResult != 0 )
                    return activationResult;

                using var resumedLock = await InstallRootLock.AcquireAsync(root);
                var resumedState = await store.ReadAsync(lockLease: resumedLock) ??
                                   throw new InvalidDataException("Installation state disappeared after recovery.");
                StartApplication(ResolveExecutable(root, resumedState.ActiveVersion), root, args, null,
                    resumedState.RuntimeFlavor).Dispose();
                return 0;
            }

            if ( state.PendingVersion is { } candidate )
            {
                var candidateProcess = await TryStartCandidateAsync(root, candidate, args, state.RuntimeFlavor);
                if ( candidateProcess is not null )
                {
                    try
                    {
                        await store.CommitPendingAsync(lockLease: installLock);
                        candidateProcess.Dispose();
                        return 0;
                    }
                    catch ( Exception ex )
                    {
                        StopCandidate(candidateProcess);
                        try
                        { await store.RollbackPendingAsync(lockLease: installLock); }
                        catch ( Exception ) { }
                        ShowError("新版本已启动，但无法提交版本状态；已尝试恢复旧版本。\n\n" + ex.Message);
                    }
                }
                else
                {
                    try
                    { await store.RollbackPendingAsync(lockLease: installLock); }
                    catch ( Exception ) { /* Continue with the unchanged active version if state writes fail. */ }
                    ShowError($"新版本 {candidate} 未能正常启动，已恢复到 {state.ActiveVersion}。");
                }
            }

            var current = await store.ReadAsync(lockLease: installLock) ?? state;
            try
            {
                StartApplication(ResolveExecutable(root, current.ActiveVersion), root, args, null,
                    current.RuntimeFlavor).Dispose();
            }
            catch ( Exception ex ) when ( current.PreviousVersion is not null )
            {
                var previous = current.PreviousVersion;
                StartApplication(ResolveExecutable(root, previous), root, args, null, current.RuntimeFlavor).Dispose();
                await store.WriteAsync(new VersionState(previous, RuntimeFlavor: current.RuntimeFlavor),
                    lockLease: installLock);
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

    private static async Task WaitForProcessExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch ( ArgumentException )
        {
            // The app exited before the stable launcher started waiting.
        }
    }

    private static async Task<int> RunInstallerActivationAsync(string[] args)
    {
        string root = AppContext.BaseDirectory;
        var phase = "argument validation";
        var version = "unknown";
        var runtimeFlavor = RuntimeFlavors.SelfContained;
        var committed = false;
        var candidateDeployed = false;
        string? finalVersionDirectory = null;
        string? displacedVersionDirectory = null;
        string? stageDirectory = null;
        InstallRootLock.Lease? installLock = null;
        VersionStateStore? store = null;
        var hadActiveVersion = false;
        var initialInstallPending = false;
        try
        {
            var options = ParseInstallerActivationArguments(args);
            version = options["version"];
            runtimeFlavor = options["runtime-flavor"];
            if ( !SemanticVersion.TryParse(version, out var requestedVersion) )
                throw new InvalidDataException("Installer supplied an unsupported stable version.");

            phase = "install root validation";
            root = ValidateInstallerRoot(options["root"]);
            installLock = await InstallRootLock.AcquireAsync(root, TimeSpan.FromMinutes(3));
            store = new VersionStateStore(root);
            var current = await store.ReadAsync(lockLease: installLock);
            hadActiveVersion = current is not null;
            if ( current is not null )
            {
                if ( !SemanticVersion.TryParse(current.ActiveVersion, out var currentVersion) ||
                     requestedVersion < currentVersion )
                    throw new InvalidOperationException($"Downgrade refused: active {current.ActiveVersion}, installer {version}.");
            }

            phase = "payload integrity verification";
            stageDirectory = Path.Combine(root, "versions", version + ".installer-stage");
            var manifestPath = options["manifest"];
            EnsurePathInsideRoot(root, manifestPath);
            finalVersionDirectory = Path.Combine(root, "versions", version);
            var hasStageDirectory = Directory.Exists(stageDirectory);
            var verificationDirectory = hasStageDirectory ? stageDirectory : finalVersionDirectory;
            if ( !Directory.Exists(verificationDirectory) )
                throw new DirectoryNotFoundException("Neither the staged payload nor a previously moved candidate exists.");
            await InstallerPayloadManifest.VerifyAsync(root, verificationDirectory, manifestPath,
                options["manifest-sha256"], version);

            if ( current is null )
            {
                phase = "initial install transaction persistence";
                current = await store.BeginInitialInstallerPendingAsync(version, options["manifest-sha256"], runtimeFlavor,
                    lockLease: installLock);
                hadActiveVersion = true;
                initialInstallPending = true;
            }
            else
                initialInstallPending = current.InstallerInitialInstallPending;

            phase = "candidate directory preparation";
            var versionsRoot = Path.Combine(root, "versions");
            InstallRootLock.EnsureNotReparsePoint(root, versionsRoot);
            Directory.CreateDirectory(versionsRoot);
            if ( current is not null )
            {
                phase = "pending version persistence";
                current = await store.BeginInstallerPendingAsync(version, options["manifest-sha256"], runtimeFlavor,
                    lockLease: installLock);
                displacedVersionDirectory = Path.Combine(versionsRoot, ".installer-backup-" + current.TransactionId);
            }
            if ( File.Exists(finalVersionDirectory) )
                throw new InvalidDataException("Candidate version path is a file.");
            if ( hasStageDirectory && Directory.Exists(finalVersionDirectory) )
            {
                InstallRootLock.EnsureNotReparsePoint(root, finalVersionDirectory);
                if ( displacedVersionDirectory is null )
                    displacedVersionDirectory = Path.Combine(versionsRoot, $".installer-backup-{Guid.NewGuid():N}");
                if ( Directory.Exists(displacedVersionDirectory) )
                    throw new IOException("An installer rollback directory already exists for this transaction.");
                Directory.Move(finalVersionDirectory, displacedVersionDirectory);
            }
            if ( hasStageDirectory )
            {
                Directory.Move(stageDirectory, finalVersionDirectory);
                candidateDeployed = true;
            }
            else
                candidateDeployed = true;

            phase = "real application health check";
            await ProbeApplicationAsync(root, finalVersionDirectory, runtimeFlavor);

            phase = "active version commit";
            await store.CommitInstallerPendingAsync(version, lockLease: installLock);
            committed = true;
            var committedState = await store.ReadAsync(lockLease: installLock) ??
                                 throw new InvalidDataException("Committed active version state could not be read.");
            if ( committedState.ActiveVersion != version || committedState.PendingVersion is not null )
                throw new InvalidDataException("Committed active version state does not match the candidate.");
            phase = "old version and backup cleanup";
            VersionDirectoryManager.PruneForInstaller(root, version);
            var verifiedState = await store.ReadAsync(lockLease: installLock) ??
                                throw new InvalidDataException("Active version state disappeared during cleanup.");
            if ( verifiedState.ActiveVersion != version )
                throw new InvalidDataException("Active version changed during cleanup.");
            await store.CompleteInstallerCleanupAsync(version, lockLease: installLock);
            if ( File.Exists(manifestPath) )
                File.Delete(manifestPath);
            AppendInstallerLog(root, "complete", version, "activation, health check, commit and cleanup succeeded");
            return 0;
        }
        catch ( Exception ex )
        {
            try
            {
                if ( committed )
                {
                    AppendInstallerLog(root, phase, version, "activation succeeded but cleanup is incomplete: " + ex);
                    return InstallerCleanupIncomplete;
                }

                if ( displacedVersionDirectory is not null && Directory.Exists(displacedVersionDirectory) &&
                     finalVersionDirectory is not null )
                {
                    if ( Directory.Exists(finalVersionDirectory) )
                    {
                        if ( !candidateDeployed )
                            throw new InvalidOperationException("Refusing to replace a directory that was not deployed by this transaction.");
                        VersionDirectoryManager.DeleteTreeWithoutFollowingLinks(finalVersionDirectory);
                    }
                    Directory.Move(displacedVersionDirectory, finalVersionDirectory);
                }
                else if ( candidateDeployed && finalVersionDirectory is not null && Directory.Exists(finalVersionDirectory) )
                {
                    if ( initialInstallPending || !hadActiveVersion || !string.Equals(Path.GetFileName(finalVersionDirectory),
                             (await store!.ReadAsync(lockLease: installLock!))?.ActiveVersion,
                             StringComparison.OrdinalIgnoreCase) )
                    {
                        VersionDirectoryManager.DeleteTreeWithoutFollowingLinks(finalVersionDirectory);
                    }
                }

                if ( store is not null && installLock is not null && hadActiveVersion )
                {
                    var state = await store.ReadAsync(lockLease: installLock);
                    if ( state?.InstallerInitialInstallPending == true )
                        await store.AbortInitialInstallerPendingAsync(lockLease: installLock);
                    else if ( state?.PendingVersion is not null )
                        await store.RollbackPendingAsync(lockLease: installLock);
                }

                AppendInstallerLog(root, phase, version, ex.ToString());
            }
            catch ( Exception logOrRecoveryError )
            {
                try { AppendInstallerLog(root, phase, version, ex + "\r\nRecovery/logging failure: " + logOrRecoveryError); }
                catch ( Exception ) { }
            }
            return InstallerActivationFailed;
        }
        finally
        {
            installLock?.Dispose();
        }
    }

    private static Dictionary<string, string> ParseInstallerActivationArguments(string[] args)
    {
        var allowed = new HashSet<string>(["version", "root", "manifest", "manifest-sha256", "runtime-flavor"],
            StringComparer.OrdinalIgnoreCase);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for ( var index = 1; index < args.Length; index++ )
        {
            if ( !args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length )
                throw new ArgumentException("Installer activation arguments must be --name value pairs.");
            var name = args[index][2..];
            if ( !allowed.Contains(name) || !options.TryAdd(name, args[++index]) ||
                 string.IsNullOrWhiteSpace(options[name]) )
                throw new ArgumentException($"Invalid or duplicate installer activation argument: {name}");
        }
        foreach ( var required in allowed )
            if ( !options.ContainsKey(required) )
                throw new ArgumentException($"Missing installer activation argument --{required}.");
        if ( !RuntimeFlavors.IsValid(options["runtime-flavor"]) )
            throw new ArgumentException("Invalid installer runtime flavor.");
        return options;
    }

    private static string ValidateInstallerRoot(string requestedRoot)
    {
        if ( !Path.IsPathFullyQualified(requestedRoot) )
            throw new InvalidDataException("Installer root must be an absolute path.");
        var requested = InstallRootLock.NormalizeRoot(requestedRoot);
        var actual = InstallRootLock.NormalizeRoot(AppContext.BaseDirectory);
        if ( !string.Equals(requested, actual, StringComparison.OrdinalIgnoreCase) )
            throw new InvalidDataException("Installer root does not match the Launcher's actual directory.");

        for ( var directory = new DirectoryInfo(actual); directory is not null; directory = directory.Parent )
        {
            if ( directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0 )
                throw new InvalidDataException($"Reparse point in installer root ancestry: {directory.FullName}");
        }
        return actual;
    }

    private static void EnsurePathInsideRoot(string root, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if ( !fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) )
            throw new InvalidDataException("Installer manifest path escapes the installation root.");
        InstallRootLock.EnsureNotReparsePoint(root, fullPath);
    }

    private static async Task ProbeApplicationAsync(string root, string versionDirectory, string runtimeFlavor)
    {
        var executable = Path.Combine(versionDirectory, "Juju.App.exe");
        var pipeName = "juju-installer-health-" + Guid.NewGuid().ToString("N");
        var healthDataRoot = Path.Combine(root, "staging", "health-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(healthDataRoot);
        InstallRootLock.EnsureNotReparsePoint(root, healthDataRoot);
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = versionDirectory,
            UseShellExecute = false
        };
        startInfo.Environment["JUJU_INSTALL_ROOT"] = root;
        startInfo.Environment["JUJU_HEALTH_CHECK_DATA_ROOT"] = healthDataRoot;
        startInfo.ArgumentList.Add("--installer-health-check");
        startInfo.ArgumentList.Add("--runtime-flavor");
        startInfo.ArgumentList.Add(runtimeFlavor);
        startInfo.ArgumentList.Add("--launcher-pipe");
        startInfo.ArgumentList.Add(pipeName);
        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start the installer health-check process.");

        try
        {
            var connectTask = pipe.WaitForConnectionAsync(timeout.Token);
            var exitTask = process.WaitForExitAsync(timeout.Token);
            if ( await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false) != connectTask )
                throw new InvalidOperationException($"Health-check process exited before READY (code {process.ExitCode}).");
            await connectTask.ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var ready = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if ( ready != "READY" )
                throw new InvalidDataException("Health-check process did not send READY.");
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token).ConfigureAwait(false);
            if ( process.HasExited )
                throw new InvalidOperationException($"Health-check process exited immediately after READY (code {process.ExitCode}).");
            await process.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if ( process.ExitCode != 0 )
                throw new InvalidOperationException($"Health-check process failed with exit code {process.ExitCode}.");
        }
        catch
        {
            StopCandidate(process);
            throw;
        }
        finally
        {
            if ( Directory.Exists(healthDataRoot) )
                VersionDirectoryManager.DeleteTreeWithoutFollowingLinks(healthDataRoot);
        }
    }

    private static void AppendInstallerLog(string root, string phase, string version, string message)
    {
        var stateDirectory = Path.Combine(root, "state");
        Directory.CreateDirectory(stateDirectory);
        InstallRootLock.EnsureNotReparsePoint(root, stateDirectory);
        var logPath = Path.Combine(stateDirectory, "installer-activation.log");
        InstallRootLock.EnsureNotReparsePoint(root, logPath);
        File.AppendAllText(logPath,
            $"[{DateTimeOffset.UtcNow:O}] phase={phase}; version={version}; {message}{Environment.NewLine}");
    }

    private static async Task<Process?> TryStartCandidateAsync(string root, string candidate, string[] originalArgs,
        string runtimeFlavor)
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
            process = StartApplication(executable, root, originalArgs, pipeName, runtimeFlavor);
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
        string? startupPipe, string runtimeFlavor)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        };
        start.Environment["JUJU_INSTALL_ROOT"] = root;
        start.ArgumentList.Add("--runtime-flavor");
        start.ArgumentList.Add(runtimeFlavor);
        for ( var index = 0; index < originalArgs.Length; index++ )
        {
            if ( string.Equals(originalArgs[index], "--launcher-pipe", StringComparison.OrdinalIgnoreCase) )
            {
                index++;
                continue;
            }
            if ( string.Equals(originalArgs[index], "--runtime-flavor", StringComparison.OrdinalIgnoreCase) )
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
