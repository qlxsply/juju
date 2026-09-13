using System.Collections.Concurrent;
using System.Security.Cryptography;
using Juju.Core.Errors;

namespace Juju.Core.Storage;

public sealed record StorageExternalChange(string RelativePath, WatcherChangeTypes ChangeType);

public interface IStorageManager : IAsyncDisposable
{
    string? DataRoot { get; }
    bool IsAvailable { get; }
    event EventHandler<StorageExternalChange>? ExternalChanged;
    Task InitializeAsync(string dataRoot, CancellationToken cancellationToken = default);
    Task UseExistingDataRootAsync(string dataRoot, CancellationToken cancellationToken = default);
    Task MigrateToAsync(string destination, CancellationToken cancellationToken = default);
    string GetPath(params string[] segments);
    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default);
    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default);
}

public sealed class StorageManager : IStorageManager
{
    private readonly DataRootService _roots;
    private readonly IAtomicFileWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private readonly ConcurrentDictionary<string, WatcherChangeTypes> _pending = new(StringComparer.OrdinalIgnoreCase);

    public StorageManager(DataRootService roots, IAtomicFileWriter writer)
    {
        _roots = roots;
        _writer = writer;
    }

    public string? DataRoot { get; private set; }
    public bool IsAvailable { get; private set; }
    public event EventHandler<StorageExternalChange>? ExternalChanged;

    public Task InitializeAsync(string dataRoot, CancellationToken cancellationToken = default) =>
        SwitchAsync(dataRoot, true, cancellationToken);

    public Task UseExistingDataRootAsync(string dataRoot, CancellationToken cancellationToken = default) =>
        SwitchAsync(dataRoot, true, cancellationToken);

    public async Task MigrateToAsync(string destination, CancellationToken cancellationToken = default)
    {
        var source = DataRoot ??
                     throw new JujuException(ErrorCode.DataRootUnavailable, "No data root is available to migrate.");
        var target = await _roots.ValidateAsync(destination, false, cancellationToken);
        if (target.Path is null ||
            (Directory.Exists(target.Path) && Directory.EnumerateFileSystemEntries(target.Path).Any()))
            throw new JujuException(ErrorCode.DataRootInvalid, "The migration destination must be an empty directory.");
        if (target.Path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new JujuException(ErrorCode.DataRootInvalid,
                "The migration destination cannot be inside the source data root.");
        await _roots.EnsureInitializedAsync(target.Path, cancellationToken);
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                if (string.Equals(relative, "juju.json", StringComparison.OrdinalIgnoreCase)) continue;
                var copy = Path.Combine(target.Path, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                                 FileOptions.Asynchronous))
                await using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(true);
                }

                var sourceHash = await HashAsync(file, cancellationToken);
                var copyHash = await HashAsync(copy, cancellationToken);
                if (new FileInfo(file).Length != new FileInfo(copy).Length ||
                    !CryptographicOperations.FixedTimeEquals(sourceHash, copyHash))
                    throw new IOException("Copied file does not match source.");
            }

            await SwitchAsync(target.Path, false, cancellationToken);
        }
        catch
        {
            throw;
        } // Source is intentionally retained after failed or successful migration.
    }

    public string GetPath(params string[] segments)
    {
        var root = DataRoot ?? throw new JujuException(ErrorCode.DataRootUnavailable, "The data root is unavailable.");
        var path = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            throw new JujuException(ErrorCode.DataRootInvalid, "The storage path escapes the data root.");
        return path;
    }

    public async Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await File.ReadAllTextAsync(GetPath(relativePath), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsAvailable = false;
            throw new JujuException(ErrorCode.StorageIoError, "Storage could not be read.", ex);
        }
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var path = GetPath(relativePath);
        try
        {
            await _writer.WriteTextAsync(path, content, cancellationToken);
            _selfWrites[path] = DateTimeOffset.UtcNow.AddSeconds(2);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            IsAvailable = false;
            throw new JujuException(ErrorCode.StorageIoError, "Storage could not be written.", ex);
        }
    }

    private async Task SwitchAsync(string root, bool initializeEmpty, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var validated = await _roots.ValidateAsync(root, initializeEmpty, cancellationToken);
            if (!validated.IsValid)
                throw new JujuException(
                    validated.Path is null ? ErrorCode.DataRootUnavailable : ErrorCode.DataRootInvalid,
                    validated.Error ?? "The data root is invalid.");
            _watcher?.Dispose();
            DataRoot = validated.Path!;
            IsAvailable = true;
            var documents = GetPath("json", "documents");
            _watcher = new FileSystemWatcher(documents, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false, EnableRaisingEvents = true
            };
            _watcher.Changed += OnWatcher;
            _watcher.Created += OnWatcher;
            _watcher.Deleted += OnWatcher;
            _watcher.Renamed += OnRenamed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnWatcher(object sender, FileSystemEventArgs e) => Queue(e.FullPath, e.ChangeType);
    private void OnRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath, e.ChangeType);

    private void Queue(string path, WatcherChangeTypes change)
    {
        if (_selfWrites.TryGetValue(path, out var until) && until >= DateTimeOffset.UtcNow) return;
        _pending[path] = change;
        _debounce ??= new Timer(_ => PublishChanges(), null, Timeout.Infinite, Timeout.Infinite);
        _debounce.Change(150, Timeout.Infinite);
    }

    private void PublishChanges()
    {
        foreach (var item in _pending.ToArray())
            if (_pending.TryRemove(item.Key, out var change) && DataRoot is not null)
                ExternalChanged?.Invoke(this, new(Path.GetRelativePath(DataRoot, item.Key), change));
        foreach (var entry in _selfWrites.Where(entry => entry.Value < DateTimeOffset.UtcNow).ToArray())
            _selfWrites.TryRemove(entry.Key, out _);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}