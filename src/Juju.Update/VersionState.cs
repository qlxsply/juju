using System.Text.Json;

namespace Juju.Update;

public sealed record VersionState(string ActiveVersion, string? PendingVersion = null, string? PreviousVersion = null,
    string? TransactionId = null);

public sealed class VersionStateStore(string installRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _stateDirectory = Path.Combine(Path.GetFullPath(installRoot), "state");
    private string StatePath => Path.Combine(_stateDirectory, "active.json");
    private string BackupPath => Path.Combine(_stateDirectory, "active.json.bak");

    public async Task<VersionState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if ( !File.Exists(StatePath) )
            return File.Exists(BackupPath)
                ? await ReadStateFileAsync(BackupPath, cancellationToken)
                : null;
        try
        {
            return await ReadStateFileAsync(StatePath, cancellationToken);
        }
        catch ( Exception primaryError ) when ( primaryError is JsonException or InvalidDataException or IOException )
        {
            if ( !File.Exists(BackupPath) )
                throw;
            try
            { return await ReadStateFileAsync(BackupPath, cancellationToken); }
            catch ( Exception backupError ) when ( backupError is JsonException or InvalidDataException or IOException )
            {
                throw new InvalidDataException("Active version state and its backup are invalid.", primaryError);
            }
        }
    }

    public async Task WriteAsync(VersionState state, CancellationToken cancellationToken = default)
    {
        if ( !SemanticVersion.TryParse(state.ActiveVersion, out _) ||
             (state.PendingVersion is not null && !SemanticVersion.TryParse(state.PendingVersion, out _)) ||
             (state.PreviousVersion is not null && !SemanticVersion.TryParse(state.PreviousVersion, out _)) )
            throw new InvalidDataException("Version state contains an invalid version.");
        Directory.CreateDirectory(_stateDirectory);
        var temporary = Path.Combine(_stateDirectory, $"active.{Guid.NewGuid():N}.tmp");
        var backupTemporary = Path.Combine(_stateDirectory, $"active.{Guid.NewGuid():N}.bak.tmp");
        try
        {
            if ( File.Exists(StatePath) )
            {
                File.Copy(StatePath, backupTemporary);
                if ( File.Exists(BackupPath) )
                    File.Replace(backupTemporary, BackupPath, null, ignoreMetadataErrors: true);
                else
                    File.Move(backupTemporary, BackupPath);
            }
            await using ( var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough) )
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
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
            if ( File.Exists(backupTemporary) )
                File.Delete(backupTemporary);
        }
    }

    private static async Task<VersionState> ReadStateFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<VersionState>(stream, JsonOptions, cancellationToken);
        if ( state is null || !SemanticVersion.TryParse(state.ActiveVersion, out _) ||
             (state.PendingVersion is not null && !SemanticVersion.TryParse(state.PendingVersion, out _)) ||
             (state.PreviousVersion is not null && !SemanticVersion.TryParse(state.PreviousVersion, out _)) )
            throw new InvalidDataException("Active version state is invalid.");
        return state;
    }

    public async Task<VersionState> BeginPendingAsync(string candidateVersion,
        CancellationToken cancellationToken = default)
    {
        if ( !SemanticVersion.TryParse(candidateVersion, out _) )
            throw new InvalidDataException("Candidate version is invalid.");
        var current = await ReadAsync(cancellationToken) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( !SemanticVersion.TryParse(current.ActiveVersion, out var active) ||
             !SemanticVersion.TryParse(candidateVersion, out var candidate) || candidate <= active )
            throw new InvalidOperationException("Candidate version must be newer than the active version.");
        var pending = current with
        {
            PendingVersion = candidateVersion,
            PreviousVersion = current.ActiveVersion,
            TransactionId = Guid.NewGuid().ToString("N")
        };
        await WriteAsync(pending, cancellationToken);
        return pending;
    }

    public async Task<VersionState> CommitPendingAsync(CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(cancellationToken) ??
                      throw new InvalidDataException("No active version is installed.");
        if ( current.PendingVersion is null )
            throw new InvalidOperationException("There is no pending version to commit.");
        var committed = current with
        {
            ActiveVersion = current.PendingVersion,
            PreviousVersion = current.ActiveVersion,
            PendingVersion = null,
            TransactionId = null
        };
        await WriteAsync(committed, cancellationToken);
        return committed;
    }

    public async Task<VersionState> RollbackPendingAsync(CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(cancellationToken) ??
                      throw new InvalidDataException("No active version is installed.");
        var rolledBack = current with { PendingVersion = null, TransactionId = null };
        await WriteAsync(rolledBack, cancellationToken);
        return rolledBack;
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
