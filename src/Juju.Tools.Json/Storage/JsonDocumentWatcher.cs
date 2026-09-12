using System.Collections.Concurrent;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Storage;

/// <summary>Debounces noisy filesystem notifications and suppresses the revision written by this process.</summary>
public sealed class JsonDocumentWatcher : IAsyncDisposable
{
    private readonly string _directory;
    private readonly DocumentRevisionService _revisions;
    private readonly Func<string, JsonDocumentId?> _documentIdForFile;
    private readonly ConcurrentDictionary<string, (DocumentRevision Revision, int Events)> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _selfWriteIntents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (WatcherChangeTypes Kind, JsonDocumentId? Id)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;

    public JsonDocumentWatcher(string directory, DocumentRevisionService revisions, Func<string, JsonDocumentId?> documentIdForFile)
    {
        _directory = directory;
        _revisions = revisions;
        _documentIdForFile = documentIdForFile;
        _timer = new Timer(_ => _ = PublishAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(directory, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, EnableRaisingEvents = true };
        _watcher.Changed += OnChanged; _watcher.Created += OnChanged; _watcher.Deleted += OnChanged; _watcher.Renamed += OnRenamed;
    }

    public event EventHandler<JsonDocumentExternalChange>? Changed;
    public void MarkSelfWrite(string path) => _selfWriteIntents[path] = DateTimeOffset.UtcNow.AddSeconds(1);
    public void RegisterSelfWrite(string path, DocumentRevision revision) => _selfWrites.AddOrUpdate(path, (revision, 1), (_, current) => (revision, current.Events + 1));
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Atomic replacement produces unreliable delete/create pairs on NTFS; reconciliation owns deletion recovery.
        if (e.ChangeType != WatcherChangeTypes.Deleted) Queue(e.FullPath, e.ChangeType, null);
    }
    private void OnRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath, e.ChangeType, _documentIdForFile(Path.GetFileName(e.OldFullPath)));
    private void Queue(string path, WatcherChangeTypes change, JsonDocumentId? id) { _pending[path] = (change, id); _timer.Change(150, Timeout.Infinite); }
    private async Task PublishAsync()
    {
        foreach (var item in _pending.ToArray())
        {
            if (!_pending.TryRemove(item.Key, out var pending)) continue;
            var kind = pending.Kind;
            var id = pending.Id ?? _documentIdForFile(Path.GetFileName(item.Key));
            if (id is null) continue;
            // Atomic replacement can transiently surface as Deleted before the replacement file appears.
            if (kind == WatcherChangeTypes.Deleted)
            {
                await Task.Delay(300).ConfigureAwait(false);
                if (!File.Exists(item.Key))
                {
                    // Windows can report the removal half of an atomic replacement after the new file event.
                    if (_selfWriteIntents.TryGetValue(item.Key, out var intent) && intent >= DateTimeOffset.UtcNow) continue;
                    _selfWrites.TryRemove(item.Key, out _);
                    Changed?.Invoke(this, new(id.Value, ExternalDocumentChangeKind.Deleted, null));
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
                Changed?.Invoke(this, new(id.Value, kind == WatcherChangeTypes.Renamed ? ExternalDocumentChangeKind.Renamed : ExternalDocumentChangeKind.Changed, revision));
            }
            catch (IOException) { /* A transient atomic replacement will be reconciled by its follow-up event. */ }
        }
        foreach (var intent in _selfWriteIntents.Where(item => item.Value < DateTimeOffset.UtcNow).ToArray()) _selfWriteIntents.TryRemove(intent.Key, out _);
    }
    public ValueTask DisposeAsync() { _watcher.Dispose(); _timer.Dispose(); return ValueTask.CompletedTask; }
}
