using System.Collections.Concurrent;
using System.Security.Cryptography;
using Juju.Core.Errors;

namespace Juju.Core.Storage;

/// <summary>来自其他进程的文件系统变更，以数据根目录内的相对路径传递给订阅者。</summary>
public sealed record StorageExternalChange(string RelativePath, WatcherChangeTypes ChangeType);

/// <summary>
/// 数据根目录的异步生命周期和受限文件访问契约。继承 <see cref="IAsyncDisposable"/> 表示实现
/// 可能拥有需异步停止的资源；调用方应优先使用 <c>await using</c>。
/// </summary>
public interface IStorageManager : IAsyncDisposable
{
    /// <summary>已验证数据根目录；尚未初始化时为 null。</summary>
    string? DataRoot { get; }

    /// <summary>最近一次可恢复的存储 IO 失败后为 false。</summary>
    bool IsAvailable { get; }

    /// <summary>经过去抖并排除本进程写入后的外部文件变更事件。</summary>
    event EventHandler<StorageExternalChange>? ExternalChanged;

    /// <summary>验证、必要时初始化并切换至数据根目录。</summary>
    Task InitializeAsync(string dataRoot, CancellationToken cancellationToken = default);

    /// <summary>验证并使用既有数据根目录，当前实现也允许初始化空目录。</summary>
    Task UseExistingDataRootAsync(string dataRoot, CancellationToken cancellationToken = default);

    /// <summary>复制并校验所有数据文件后切换根目录，源目录会被保留。</summary>
    Task MigrateToAsync(string destination, CancellationToken cancellationToken = default);

    /// <summary>由相对片段得到根目录内路径，并拒绝路径穿越。</summary>
    string GetPath(params string[] segments);

    /// <summary>异步读取根目录内文本，IO 错误转换为领域异常。</summary>
    Task<string> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>原子异步写入根目录内文本，并标记为本进程写入。</summary>
    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// 协调数据根目录切换、文件读写和外部变更通知。并发字典服务于回调线程，
/// <see cref="SemaphoreSlim"/> 则串行化会改变根目录和观察器的异步操作。
/// </summary>
public sealed class StorageManager(DataRootService roots, IAtomicFileWriter writer) : IStorageManager
{
    // 异步锁不能用 C# lock 替代；lock 无法跨 await 保持所有权。
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private readonly ConcurrentDictionary<string, WatcherChangeTypes> _pending = new(StringComparer.OrdinalIgnoreCase);

    public string? DataRoot { get; private set; }
    public bool IsAvailable { get; private set; }
    public event EventHandler<StorageExternalChange>? ExternalChanged;

    /// <summary>切换到可初始化的数据根目录。</summary>
    public Task InitializeAsync(string dataRoot, CancellationToken cancellationToken = default) =>
        SwitchAsync(dataRoot, true, cancellationToken);

    /// <summary>切换到经过验证的已有数据根目录。</summary>
    public Task UseExistingDataRootAsync(string dataRoot, CancellationToken cancellationToken = default) =>
        SwitchAsync(dataRoot, true, cancellationToken);

    /// <summary>
    /// 将源目录复制到空目标目录，逐文件比较 SHA-256 和长度，全部成功后才切换活动根目录。
    /// 文件流使用异步 IO 与写穿透，取消会在文件边界和复制操作中传播。
    /// </summary>
    public async Task MigrateToAsync(string destination, CancellationToken cancellationToken = default)
    {
        var source = DataRoot ??
                     throw new JujuException(ErrorCode.DataRootUnavailable, "No data root is available to migrate.");
        var target = await roots.ValidateAsync(destination, false, cancellationToken);
        if (target.Path is null ||
            (Directory.Exists(target.Path) && Directory.EnumerateFileSystemEntries(target.Path).Any()))
            throw new JujuException(ErrorCode.DataRootInvalid, "The migration destination must be an empty directory.");
        if (target.Path.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new JujuException(ErrorCode.DataRootInvalid,
                "The migration destination cannot be inside the source data root.");
        await roots.EnsureInitializedAsync(target.Path, cancellationToken);
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
        } // 即使迁移成功也保留源目录；失败时也不尝试破坏性清理。
    }

    /// <summary>组合后再规范化完整路径，防止 <c>..</c> 或绝对片段逃出数据根目录。</summary>
    public string GetPath(params string[] segments)
    {
        var root = DataRoot ?? throw new JujuException(ErrorCode.DataRootUnavailable, "The data root is unavailable.");
        var path = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            throw new JujuException(ErrorCode.DataRootInvalid, "The storage path escapes the data root.");
        return path;
    }

    /// <summary>读取文本；取消不包装，以便调用方能按常规取消语义处理。</summary>
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

    /// <summary>通过原子写入器保存文本，并暂时抑制随后的自身文件观察器事件。</summary>
    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var path = GetPath(relativePath);
        try
        {
            await writer.WriteTextAsync(path, content, cancellationToken);
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

    /// <summary>在异步互斥锁内验证根目录、重建观察器，并确保锁在异常或取消时释放。</summary>
    private async Task SwitchAsync(string root, bool initializeEmpty, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var validated = await roots.ValidateAsync(root, initializeEmpty, cancellationToken);
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

    /// <summary>合并短时间内同一路径的嘈杂通知，延迟后只发布最终一次变更。</summary>
    private void Queue(string path, WatcherChangeTypes change)
    {
        if (_selfWrites.TryGetValue(path, out var until) && until >= DateTimeOffset.UtcNow) return;
        _pending[path] = change;
        _debounce ??= new Timer(_ => PublishChanges(), null, Timeout.Infinite, Timeout.Infinite);
        _debounce.Change(150, Timeout.Infinite);
    }

    /// <summary>在定时器线程提取待发布项，并清理过期的自身写入抑制标记。</summary>
    private void PublishChanges()
    {
        foreach (var item in _pending.ToArray())
            if (_pending.TryRemove(item.Key, out var change) && DataRoot is not null)
                ExternalChanged?.Invoke(this, new(Path.GetRelativePath(DataRoot, item.Key), change));
        foreach (var entry in _selfWrites.Where(entry => entry.Value < DateTimeOffset.UtcNow).ToArray())
            _selfWrites.TryRemove(entry.Key, out _);
    }

    /// <summary>异步流式计算文件 SHA-256，避免为完整文件额外分配内存。</summary>
    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    /// <summary>
    /// 停止非托管文件观察器和定时器并释放异步锁。当前清理本身无需等待，故返回已完成的
    /// <see cref="ValueTask"/>，避免为同步完成路径分配 <see cref="Task"/>。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}