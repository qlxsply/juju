using System.Collections.Concurrent;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Storage;

/// <summary>Debounces noisy filesystem notifications and suppresses the revision written by this process.</summary>
public sealed class JsonDocumentWatcher : IAsyncDisposable
{
    private readonly string _directory;
    private readonly DocumentRevisionService _revisions;
    private readonly Func<string, JsonDocumentId?> _documentIdForFile;
    private readonly ConcurrentDictionary<string, DocumentRevision> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WatcherChangeTypes> _pending = new(StringComparer.OrdinalIgnoreCase);
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
    public void RegisterSelfWrite(string path, DocumentRevision revision) => _selfWrites[path] = revision;
    private void OnChanged(object sender, FileSystemEventArgs e) => Queue(e.FullPath, e.ChangeType);
    private void OnRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath, e.ChangeType);
    private void Queue(string path, WatcherChangeTypes change) { _pending[path] = change; _timer.Change(150, Timeout.Infinite); }
    private async Task PublishAsync()
    {
        foreach (var item in _pending.ToArray())
        {
            if (!_pending.TryRemove(item.Key, out var kind)) continue;
            var id = _documentIdForFile(Path.GetFileName(item.Key));
            if (id is null) continue;
            if (kind == WatcherChangeTypes.Deleted)
            {
                _selfWrites.TryRemove(item.Key, out _);
                Changed?.Invoke(this, new(id.Value, ExternalDocumentChangeKind.Deleted, null));
                continue;
            }
            try
            {
                var revision = await _revisions.GetAsync(item.Key).ConfigureAwait(false);
                if (_selfWrites.TryGetValue(item.Key, out var self) && self == revision) { _selfWrites.TryRemove(item.Key, out _); continue; }
                Changed?.Invoke(this, new(id.Value, kind == WatcherChangeTypes.Renamed ? ExternalDocumentChangeKind.Renamed : ExternalDocumentChangeKind.Changed, revision));
            }
            catch (IOException) { Changed?.Invoke(this, new(id.Value, ExternalDocumentChangeKind.Deleted, null)); }
        }
    }
    public ValueTask DisposeAsync() { _watcher.Dispose(); _timer.Dispose(); return ValueTask.CompletedTask; }
}
