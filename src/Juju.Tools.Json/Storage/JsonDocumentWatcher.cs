using System.Collections.Concurrent;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Storage;

/// <summary>
/// 对嘈杂的文件系统通知去抖，并抑制本进程刚写入的版本。它实现 <see cref="IAsyncDisposable"/>，
/// 因而拥有者应使用 <c>await using</c> 或显式等待释放，以关闭观察器和定时器。
/// </summary>
public sealed class JsonDocumentWatcher : IAsyncDisposable
{
    private readonly string _directory;
    private readonly DocumentRevisionService _revisions;
    private readonly Func<string, JsonDocumentId?> _documentIdForFile;

    // 观察器回调与定时器并发运行，使用并发字典而非普通 Dictionary 加 lock。
    private readonly ConcurrentDictionary<string, (DocumentRevision Revision, int Events)> _selfWrites =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _selfWriteIntents =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, (WatcherChangeTypes Kind, JsonDocumentId? Id)> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    public JsonDocumentWatcher(string directory, DocumentRevisionService revisions,
        Func<string, JsonDocumentId?> documentIdForFile)
    {
        _directory = directory;
        _revisions = revisions;
        _documentIdForFile = documentIdForFile;
        _timer = new Timer(_ => _ = PublishAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(directory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    /// <summary>确认来自外部的文档变更，经过去抖和自身写入过滤后触发。</summary>
    public event EventHandler<JsonDocumentExternalChange>? Changed;

    /// <summary>在原子写入前登记短暂意图，以忽略替换过程虚假的删除通知。</summary>
    public void MarkSelfWrite(string path) => _selfWriteIntents[path] = DateTimeOffset.UtcNow.AddSeconds(1);

    /// <summary>在写入完成后登记其版本；相同版本的后续观察器事件会被消耗而非发布。</summary>
    public void RegisterSelfWrite(string path, DocumentRevision revision) =>
        _selfWrites.AddOrUpdate(path, (revision, 1), (_, current) => (revision, current.Events + 1));

    /// <summary>接收创建和修改通知；删除交由协调流程恢复，因为 NTFS 原子替换会产生不可靠事件对。</summary>
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // NTFS 原子替换会产生不可靠的删除/创建对；协调流程拥有删除恢复的最终决定权。
        if (e.ChangeType != WatcherChangeTypes.Deleted) Queue(e.FullPath, e.ChangeType, null);
    }

    /// <summary>将重命名的新路径入队，同时用旧文件名解析原文档 ID。</summary>
    private void OnRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath, e.ChangeType,
        _documentIdForFile(Path.GetFileName(e.OldFullPath)));

    /// <summary>覆盖同一路径的待处理项并重置 150ms 定时器，实现末次事件去抖。</summary>
    private void Queue(string path, WatcherChangeTypes change, JsonDocumentId? id)
    {
        _pending[path] = (change, id);
        _timer.Change(150, Timeout.Infinite);
    }

    /// <summary>
    /// 在定时器触发后异步确认每项变更。删除会额外等待文件替换窗口；读取版本失败的 IO 通常是
    /// 原子替换中的短暂状态，留给后续事件或协调过程处理。此后台任务没有取消令牌，因为观察器
    /// 生命周期由 <see cref="DisposeAsync"/> 终止。
    /// </summary>
    private async Task PublishAsync()
    {
        foreach (var item in _pending.ToArray())
        {
            if (!_pending.TryRemove(item.Key, out var pending)) continue;
            var kind = pending.Kind;
            var id = pending.Id ?? _documentIdForFile(Path.GetFileName(item.Key));
            if (id is null) continue;
            // 原子替换时，新文件出现前可能暂时报告 Deleted。
            if (kind == WatcherChangeTypes.Deleted)
            {
                await Task.Delay(300).ConfigureAwait(false);
                if (!File.Exists(item.Key))
                {
                    // Windows 甚至可能在新文件事件之后才报告原子替换的移除半边。
                    if (_selfWriteIntents.TryGetValue(item.Key, out var intent) &&
                        intent >= DateTimeOffset.UtcNow) continue;
                    _selfWrites.TryRemove(item.Key, out _);
                    Changed?.Invoke(this,
                        new JsonDocumentExternalChange(id.Value, ExternalDocumentChangeKind.Deleted, null));
                    continue;
                }
            }

            try
            {
                var revision = await _revisions.GetAsync(item.Key).ConfigureAwait(false);
                if (_selfWrites.TryGetValue(item.Key, out var self) && self.Revision == revision)
                {
                    if (self.Events <= 1) _selfWrites.TryRemove(item.Key, out _);
                    else _selfWrites.TryUpdate(item.Key, (self.Revision, self.Events - 1), self);
                    continue;
                }

                Changed?.Invoke(this,
                    new JsonDocumentExternalChange(id.Value,
                        kind == WatcherChangeTypes.Renamed
                            ? ExternalDocumentChangeKind.Renamed
                            : ExternalDocumentChangeKind.Changed, revision));
            }
            catch (IOException)
            {
                /* 原子替换造成的短暂读取失败会由其后续事件或协调流程修复。 */
            }
        }

        foreach (var intent in _selfWriteIntents.Where(item => item.Value < DateTimeOffset.UtcNow).ToArray())
            _selfWriteIntents.TryRemove(intent.Key, out _);
    }

    /// <summary>同步释放 FileSystemWatcher 和 Timer；返回完成的 ValueTask 以满足异步释放契约。</summary>
    public ValueTask DisposeAsync()
    {
        _watcher.Dispose();
        _timer.Dispose();
        return ValueTask.CompletedTask;
    }
}