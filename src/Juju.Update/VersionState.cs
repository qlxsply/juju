using System.Text.Json;

namespace Juju.Update;

public sealed record VersionState(string ActiveVersion, string? PendingVersion = null, string? PreviousVersion = null,
    string? TransactionId = null, bool InstallerCleanupPending = false, string? InstallerManifestSha256 = null,
    bool InstallerInitialInstallPending = false, string RuntimeFlavor = RuntimeFlavors.SelfContained);

public sealed class VersionStateStore(string installRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _installRoot = InstallRootLock.NormalizeRoot(installRoot);
    private readonly string _stateDirectory = Path.Combine(InstallRootLock.NormalizeRoot(installRoot), "state");
    private string StatePath => Path.Combine(_stateDirectory, "active.json");
    private string BackupPath => Path.Combine(_stateDirectory, "active.json.bak");

    public async Task<VersionState?> ReadAsync(CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        return await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false);
    }

    public async Task WriteAsync(VersionState state, CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        ValidateState(state);
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        _ = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false);
        await WriteUnderLockAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VersionState> InitializeActiveAsync(string version, CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        if ( !SemanticVersion.TryParse(version, out _) )
            throw new InvalidDataException("Initial active version is invalid.");
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        if ( await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) is not null )
            throw new InvalidOperationException("An active installation already exists.");
        var state = new VersionState(version);
        await WriteUnderLockAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public async Task<VersionState> InitializeInstallerActiveAsync(string version,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        if ( !SemanticVersion.TryParse(version, out _) )
            throw new InvalidDataException("Initial installer version is invalid.");
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        if ( await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) is not null )
            throw new InvalidOperationException("An active installation already exists.");
        var state = new VersionState(version, InstallerCleanupPending: true);
        await WriteUnderLockAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
    }

    public async Task<VersionState> BeginPendingAsync(string candidateVersion,
        string runtimeFlavor = RuntimeFlavors.SelfContained,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        if ( !SemanticVersion.TryParse(candidateVersion, out var candidate) || !RuntimeFlavors.IsValid(runtimeFlavor) )
            throw new InvalidDataException("Candidate version is invalid.");
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.PendingVersion is not null )
            throw new InvalidOperationException("A version activation transaction is already pending.");
        if ( current.RuntimeFlavor != runtimeFlavor )
            throw new InvalidOperationException("Updates cannot change the installed runtime flavor.");
        if ( !SemanticVersion.TryParse(current.ActiveVersion, out var active) || candidate <= active )
            throw new InvalidOperationException("Candidate version must be newer than the active version.");
        var pending = current with
        {
            PendingVersion = candidateVersion,
            PreviousVersion = current.ActiveVersion,
            TransactionId = Guid.NewGuid().ToString("N")
        };
        await WriteUnderLockAsync(pending, cancellationToken).ConfigureAwait(false);
        return pending;
    }

    /// <summary>Installer-only transition; permits same-version repair but never downgrade.</summary>
    public async Task<VersionState> BeginInstallerPendingAsync(string candidateVersion, string manifestSha256,
        string runtimeFlavor = RuntimeFlavors.SelfContained,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        if ( !SemanticVersion.TryParse(candidateVersion, out var candidate) || !ReleaseManifest.IsSha256(manifestSha256) ||
             !RuntimeFlavors.IsValid(runtimeFlavor) )
            throw new InvalidDataException("Installer version is invalid.");
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.PendingVersion is not null )
        {
            if ( current.PendingVersion == candidateVersion && current.InstallerManifestSha256 == manifestSha256 &&
                 current.RuntimeFlavor == runtimeFlavor )
                return current;
            throw new InvalidOperationException("Another version activation transaction is already pending.");
        }
        if ( current.RuntimeFlavor != runtimeFlavor )
            throw new InvalidOperationException("Installer upgrades cannot change the installed runtime flavor.");
        if ( !SemanticVersion.TryParse(current.ActiveVersion, out var active) || candidate < active )
            throw new InvalidOperationException("Installer downgrade is not allowed.");
        var pending = current with
        {
            PendingVersion = candidateVersion,
            PreviousVersion = current.ActiveVersion,
            TransactionId = Guid.NewGuid().ToString("N"),
            InstallerManifestSha256 = manifestSha256,
            RuntimeFlavor = runtimeFlavor
        };
        await WriteUnderLockAsync(pending, cancellationToken).ConfigureAwait(false);
        return pending;
    }

    public async Task<VersionState> BeginInitialInstallerPendingAsync(string candidateVersion, string manifestSha256,
        string runtimeFlavor = RuntimeFlavors.SelfContained,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        if ( !SemanticVersion.TryParse(candidateVersion, out _) || !ReleaseManifest.IsSha256(manifestSha256) ||
             !RuntimeFlavors.IsValid(runtimeFlavor) )
            throw new InvalidDataException("Initial installer transaction is invalid.");
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false);
        if ( current is not null )
        {
            if ( current.InstallerInitialInstallPending && current.PendingVersion == candidateVersion &&
                 current.InstallerManifestSha256 == manifestSha256 && current.RuntimeFlavor == runtimeFlavor )
                return current;
            throw new InvalidOperationException("An active or pending installation already exists.");
        }
        var pending = new VersionState(candidateVersion, PendingVersion: candidateVersion,
            TransactionId: Guid.NewGuid().ToString("N"), InstallerManifestSha256: manifestSha256,
            InstallerInitialInstallPending: true, RuntimeFlavor: runtimeFlavor);
        await WriteUnderLockAsync(pending, cancellationToken).ConfigureAwait(false);
        return pending;
    }

    public async Task<VersionState> CommitInstallerPendingAsync(string candidateVersion,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.PendingVersion != candidateVersion || string.IsNullOrWhiteSpace(current.TransactionId) ||
             !ReleaseManifest.IsSha256(current.InstallerManifestSha256 ?? "") )
            throw new InvalidOperationException("Installer candidate does not match the persisted pending transaction.");
        var committed = current with
        {
            ActiveVersion = candidateVersion,
            PendingVersion = null,
            PreviousVersion = null,
            TransactionId = null,
            InstallerCleanupPending = true,
            InstallerManifestSha256 = null,
            InstallerInitialInstallPending = false
        };
        await WriteUnderLockAsync(committed, cancellationToken).ConfigureAwait(false);
        return committed;
    }

    public async Task<VersionState> CompleteInstallerCleanupAsync(string activeVersion,
        CancellationToken cancellationToken = default, InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.ActiveVersion != activeVersion || current.PendingVersion is not null ||
             !current.InstallerCleanupPending )
            throw new InvalidOperationException("Installer cleanup does not match the committed active version.");
        var completed = current with { PreviousVersion = null, InstallerCleanupPending = false };
        if ( File.Exists(BackupPath) )
            File.Delete(BackupPath);
        await WriteUnderLockAsync(completed, cancellationToken, preserveBackup: false).ConfigureAwait(false);
        return completed;
    }

    public async Task<VersionState> CommitPendingAsync(CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.PendingVersion is null )
            throw new InvalidOperationException("There is no pending version to commit.");
        var committed = current with
        {
            ActiveVersion = current.PendingVersion,
            PreviousVersion = current.ActiveVersion,
            PendingVersion = null,
            TransactionId = null,
            InstallerManifestSha256 = null
        };
        await WriteUnderLockAsync(committed, cancellationToken).ConfigureAwait(false);
        return committed;
    }

    public async Task<VersionState> RollbackPendingAsync(CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                      throw new InvalidDataException("No active version is installed.");
        var rolledBack = current with { PendingVersion = null, TransactionId = null, InstallerManifestSha256 = null };
        await WriteUnderLockAsync(rolledBack, cancellationToken).ConfigureAwait(false);
        return rolledBack;
    }

    public async Task AbortInitialInstallerPendingAsync(CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var current = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false);
        if ( current is null )
            return;
        if ( !current.InstallerInitialInstallPending || current.PendingVersion is null )
            throw new InvalidOperationException("Refusing to remove state for a non-initial installation.");
        if ( File.Exists(StatePath) )
            File.Delete(StatePath);
        if ( File.Exists(BackupPath) )
            File.Delete(BackupPath);
    }

    public async Task DeleteBackupAsync(CancellationToken cancellationToken = default,
        InstallRootLock.Lease? lockLease = null)
    {
        using var ownedLock = await AcquireIfNeededAsync(lockLease, cancellationToken).ConfigureAwait(false);
        var state = await ReadUnderLockAsync(cancellationToken, repairFromBackup: true).ConfigureAwait(false) ??
                    throw new InvalidDataException("Cannot remove state backup without a recoverable active state.");
        if ( !Directory.Exists(Path.Combine(_installRoot, "versions", state.ActiveVersion)) )
            throw new InvalidDataException("Cannot remove state backup because the active version directory is missing.");
        if ( File.Exists(BackupPath) )
            File.Delete(BackupPath);
    }

    private async Task<InstallRootLock.Lease?> AcquireIfNeededAsync(InstallRootLock.Lease? lease,
        CancellationToken cancellationToken)
    {
        if ( lease is null )
            return await InstallRootLock.AcquireAsync(_installRoot, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if ( !lease.IsFor(_installRoot) )
            throw new InvalidOperationException("Version state lock belongs to a different install root.");
        return null;
    }

    private async Task<VersionState?> ReadUnderLockAsync(CancellationToken cancellationToken, bool repairFromBackup)
    {
        InstallRootLock.EnsureNotReparsePoint(_installRoot, _stateDirectory);
        InstallRootLock.EnsureNotReparsePoint(_installRoot, StatePath);
        InstallRootLock.EnsureNotReparsePoint(_installRoot, BackupPath);
        if ( !File.Exists(StatePath) )
        {
            if ( !File.Exists(BackupPath) )
                return null;
            var recovered = await ReadStateFileAsync(BackupPath, cancellationToken).ConfigureAwait(false);
            EnsureRecoverableVersionDirectory(recovered);
            if ( repairFromBackup )
                await RestorePrimaryFromBackupAsync(cancellationToken).ConfigureAwait(false);
            return recovered;
        }

        try
        {
            var primary = await ReadStateFileAsync(StatePath, cancellationToken).ConfigureAwait(false);
            EnsureRecoverableVersionDirectory(primary);
            return primary;
        }
        catch ( Exception primaryError ) when ( primaryError is JsonException or InvalidDataException or IOException )
        {
            if ( !File.Exists(BackupPath) )
                throw;
            try
            {
                var recovered = await ReadStateFileAsync(BackupPath, cancellationToken).ConfigureAwait(false);
                EnsureRecoverableVersionDirectory(recovered);
                if ( repairFromBackup )
                    await RestorePrimaryFromBackupAsync(cancellationToken).ConfigureAwait(false);
                return recovered;
            }
            catch ( Exception backupError ) when ( backupError is JsonException or InvalidDataException or IOException )
            {
                throw new InvalidDataException("Active version state and its backup are invalid.", primaryError);
            }
        }
    }

    private void EnsureRecoverableVersionDirectory(VersionState state)
    {
        var versionsDirectory = Path.Combine(_installRoot, "versions");
        if ( state.PendingVersion is null && Directory.Exists(versionsDirectory) &&
             !Directory.Exists(Path.Combine(versionsDirectory, state.ActiveVersion)) )
            throw new InvalidDataException("Backup points to an application version that has already been removed.");
    }

    private async Task WriteUnderLockAsync(VersionState state, CancellationToken cancellationToken,
        bool preserveBackup = true)
    {
        ValidateState(state);
        Directory.CreateDirectory(_stateDirectory);
        InstallRootLock.EnsureNotReparsePoint(_installRoot, _stateDirectory);
        InstallRootLock.EnsureNotReparsePoint(_installRoot, StatePath);
        InstallRootLock.EnsureNotReparsePoint(_installRoot, BackupPath);
        var temporary = Path.Combine(_stateDirectory, $"active.{Guid.NewGuid():N}.tmp");
        var backupTemporary = Path.Combine(_stateDirectory, $"active.{Guid.NewGuid():N}.bak.tmp");
        try
        {
            await using ( var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough) )
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if ( File.Exists(StatePath) && preserveBackup )
            {
                _ = await ReadStateFileAsync(StatePath, cancellationToken).ConfigureAwait(false);
                File.Copy(StatePath, backupTemporary);
                if ( File.Exists(BackupPath) )
                    File.Replace(backupTemporary, BackupPath, null, ignoreMetadataErrors: true);
                else
                    File.Move(backupTemporary, BackupPath);
            }
            else if ( File.Exists(StatePath) )
                _ = await ReadStateFileAsync(StatePath, cancellationToken).ConfigureAwait(false);
            if ( File.Exists(StatePath) )
                File.Replace(temporary, StatePath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporary, StatePath);
        }
        finally
        {
            if ( File.Exists(temporary) )
                File.Delete(temporary);
            if ( File.Exists(backupTemporary) )
                File.Delete(backupTemporary);
        }
    }

    private async Task RestorePrimaryFromBackupAsync(CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(_stateDirectory, $"active.{Guid.NewGuid():N}.recover.tmp");
        try
        {
            await using ( var source = new FileStream(BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             4096, FileOptions.Asynchronous | FileOptions.SequentialScan) )
            await using ( var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough) )
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
            }
            if ( File.Exists(StatePath) )
                File.Replace(temporary, StatePath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporary, StatePath);
        }
        finally
        {
            if ( File.Exists(temporary) )
                File.Delete(temporary);
        }
    }

    private static void ValidateState(VersionState state)
    {
        if ( !SemanticVersion.TryParse(state.ActiveVersion, out _) ||
             (state.PendingVersion is not null && !SemanticVersion.TryParse(state.PendingVersion, out _)) ||
             (state.PreviousVersion is not null && !SemanticVersion.TryParse(state.PreviousVersion, out _)) ||
             (state.PendingVersion is not null &&
              (string.IsNullOrWhiteSpace(state.TransactionId) || !Guid.TryParseExact(state.TransactionId, "N", out _))) ||
              (state.InstallerManifestSha256 is not null && !ReleaseManifest.IsSha256(state.InstallerManifestSha256)) ||
             !RuntimeFlavors.IsValid(state.RuntimeFlavor) ||
             (state.InstallerInitialInstallPending &&
              (state.PendingVersion is null || state.PendingVersion != state.ActiveVersion ||
               state.InstallerManifestSha256 is null)) )
            throw new InvalidDataException("Version state contains an invalid version or transaction.");
    }

    private static async Task<VersionState> ReadStateFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<VersionState>(stream, JsonOptions, cancellationToken);
        if ( state is null )
            throw new InvalidDataException("Active version state is invalid.");
        ValidateState(state);
        return state;
    }

}

public enum UpdatePhase
{
    Idle,
    Checking,
    UpdateAvailable,
    Downloading,
    Verifying,
    Staging,
    AwaitingUserConfirmation,
    PreparingShutdown,
    Applying,
    StartingCandidate,
    RollingBack,
    Completed,
    Cancelled,
    Failed
}

public static class UpdatePhaseTransitions
{
    private static readonly IReadOnlyDictionary<UpdatePhase, UpdatePhase[]> Allowed = new Dictionary<UpdatePhase, UpdatePhase[]>
    {
        [UpdatePhase.Idle] = [UpdatePhase.Checking],
        [UpdatePhase.Checking] = [UpdatePhase.UpdateAvailable, UpdatePhase.Completed, UpdatePhase.Failed, UpdatePhase.Cancelled],
        [UpdatePhase.UpdateAvailable] = [UpdatePhase.Downloading, UpdatePhase.Cancelled],
        [UpdatePhase.Downloading] = [UpdatePhase.Verifying, UpdatePhase.Cancelled, UpdatePhase.Failed],
        [UpdatePhase.Verifying] = [UpdatePhase.Staging, UpdatePhase.Cancelled, UpdatePhase.Failed],
        [UpdatePhase.Staging] = [UpdatePhase.AwaitingUserConfirmation, UpdatePhase.Cancelled, UpdatePhase.Failed],
        [UpdatePhase.AwaitingUserConfirmation] = [UpdatePhase.PreparingShutdown, UpdatePhase.Cancelled],
        [UpdatePhase.PreparingShutdown] = [UpdatePhase.Applying, UpdatePhase.Cancelled, UpdatePhase.Failed],
        [UpdatePhase.Applying] = [UpdatePhase.StartingCandidate, UpdatePhase.Failed],
        [UpdatePhase.StartingCandidate] = [UpdatePhase.Completed, UpdatePhase.RollingBack, UpdatePhase.Failed],
        [UpdatePhase.RollingBack] = [UpdatePhase.Failed, UpdatePhase.Completed],
        [UpdatePhase.Completed] = [UpdatePhase.Checking],
        [UpdatePhase.Cancelled] = [UpdatePhase.Checking],
        [UpdatePhase.Failed] = [UpdatePhase.Checking]
    };

    public static bool CanTransition(UpdatePhase current, UpdatePhase next) =>
        Allowed.TryGetValue(current, out var allowed) && allowed.Contains(next);

    public static UpdatePhase Transition(UpdatePhase current, UpdatePhase next) => CanTransition(current, next)
        ? next
        : throw new InvalidOperationException($"Invalid update state transition: {current} -> {next}.");
}
