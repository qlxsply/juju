using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Core.Telemetry;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Documents;

public interface IJsonDocumentService
{
    event EventHandler<JsonDocumentExternalChange>? ExternalChanged;
    Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default);
    Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default);
    Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision, CancellationToken cancellationToken = default);
    Task OverwriteAsync(JsonDocumentId id, string content, CancellationToken cancellationToken = default);
    Task RenameAsync(JsonDocumentId id, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(JsonDocumentId id, CancellationToken cancellationToken = default);
    Task ReorderAsync(JsonDocumentId id, int order, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default);
}

public sealed class JsonDocumentService : IJsonDocumentService, IAsyncDisposable
{
    private readonly string _documents;
    private readonly string _trash;
    private readonly JsonMetadataStore _metadata;
    private readonly IAtomicFileWriter _writer;
    private readonly DocumentRevisionService _revisions;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly JsonDocumentWatcher _watcher;
    private readonly IPerformanceTelemetry _telemetry;

    public JsonDocumentService(string dataRoot, JsonMetadataStore metadata, IAtomicFileWriter writer, DocumentRevisionService revisions, IPerformanceTelemetry? telemetry = null)
    {
        _documents = Path.Combine(dataRoot, "json", "documents");
        _trash = Path.Combine(dataRoot, "json", ".trash");
        _metadata = metadata;
        _writer = writer;
        _revisions = revisions;
        _telemetry = telemetry ?? NullPerformanceTelemetry.Instance;
        _watcher = new JsonDocumentWatcher(_documents, revisions, FindIdByFileName);
        _watcher.Changed += (sender, change) => ExternalChanged?.Invoke(this, change);
    }

    public event EventHandler<JsonDocumentExternalChange>? ExternalChanged;

    public async Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileAsync(cancellationToken);
        return (await _metadata.ReadAsync(cancellationToken)).Documents.OrderBy(document => document.Order).ToArray();
    }

    public async Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentLoad);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            if (!File.Exists(path)) throw new JujuException(ErrorCode.DocumentNotFound, "The document file no longer exists.");
            return new(id, await File.ReadAllTextAsync(path, cancellationToken), await _revisions.GetAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new JujuException(ErrorCode.DocumentReadFailed, "The document could not be read.", ex); }
    }

    public async Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var next = metadata.Sequence.Date == today ? metadata.Sequence.Last + 1 : 1;
            string fileName;
            do { fileName = $"{today}{next:D3}.json"; next++; } while (File.Exists(DocumentPath(fileName)));
            var sequence = next - 1;
            var now = DateTimeOffset.UtcNow;
            var document = new JsonDocumentMetadata(JsonDocumentId.New(), fileName, metadata.Documents.Count, now, now, new("created"));
            var path = DocumentPath(fileName);
            _watcher.MarkSelfWrite(path);
            await _writer.WriteTextAsync(path, "{}", cancellationToken);
            try { await _metadata.SaveAsync(metadata with { Sequence = new(today, sequence), Documents = [.. metadata.Documents, document] }, cancellationToken); }
            catch { try { File.Delete(path); } catch { } throw; }
            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be created.", ex); }
        finally { _operations.Release(); }
    }

    public async Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            if (!Equals(await _revisions.GetAsync(path, cancellationToken), expectedRevision))
                throw new JujuException(ErrorCode.ExternalModificationConflict, "The file changed outside juju and was not overwritten.");
            _watcher.MarkSelfWrite(path);
            await _writer.WriteTextAsync(path, content, cancellationToken);
            await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Select(item => item.Id == id ? item with { UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList() }, cancellationToken);
            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be saved.", ex); }
        finally { _operations.Release(); }
    }

    public async Task OverwriteAsync(JsonDocumentId id, string content, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            await _writer.WriteTextAsync(path, content, cancellationToken);
            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Select(item => item.Id == id ? item with { UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList() }, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be saved.", ex); }
        finally { _operations.Release(); }
    }

    public async Task RenameAsync(JsonDocumentId id, string name, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var targetName = JsonDocumentName.Normalize(name);
            if (string.Equals(targetName, document.FileName, StringComparison.OrdinalIgnoreCase)) return;
            var oldPath = DocumentPath(document.FileName);
            var targetPath = DocumentPath(targetName);
            if (File.Exists(targetPath)) throw new JujuException(ErrorCode.DocumentAlreadyExists, "A document with that name already exists.");
            try
            {
                File.Move(oldPath, targetPath);
                await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Select(item => item.Id == id ? item with { FileName = targetName, UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList() }, cancellationToken);
                _watcher.RegisterSelfWrite(targetPath, await _revisions.GetAsync(targetPath, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { if (File.Exists(targetPath) && !File.Exists(oldPath)) File.Move(targetPath, oldPath); } catch { }
                throw new JujuException(ErrorCode.DocumentRenameFailed, "The document could not be renamed.", ex);
            }
        }
        finally { _operations.Release(); }
    }

    public async Task DeleteAsync(JsonDocumentId id, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var source = DocumentPath(document.FileName);
            var trash = Path.Combine(_trash, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}__{document.FileName}");
            try
            {
                File.Move(source, trash);
                await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Where(item => item.Id != id).Select((item, order) => item with { Order = order }).ToList() }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { if (File.Exists(trash) && !File.Exists(source)) File.Move(trash, source); } catch { }
                throw new JujuException(ErrorCode.DocumentDeleteFailed, "The document could not be deleted.", ex);
            }
        }
        finally { _operations.Release(); }
    }

    public async Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentImport);
        var imported = new List<JsonDocumentMetadata>();
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var copiedPaths = new List<string>();
            foreach (var source in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase)) continue;
                var baseName = JsonDocumentName.Normalize(Path.GetFileName(source));
                var candidate = baseName;
                for (var suffix = 2; File.Exists(DocumentPath(candidate)) || metadata.Documents.Any(item => string.Equals(item.FileName, candidate, StringComparison.OrdinalIgnoreCase)) || imported.Any(item => string.Equals(item.FileName, candidate, StringComparison.OrdinalIgnoreCase)); suffix++) candidate = $"{Path.GetFileNameWithoutExtension(baseName)}-{suffix}.json";
                try { await _writer.WriteTextAsync(DocumentPath(candidate), await File.ReadAllTextAsync(source, cancellationToken), cancellationToken); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new JujuException(ErrorCode.ImportFailed, "A document could not be imported.", ex); }
                copiedPaths.Add(DocumentPath(candidate));
                var now = DateTimeOffset.UtcNow;
                var item = new JsonDocumentMetadata(JsonDocumentId.New(), candidate, metadata.Documents.Count + imported.Count, now, now, new("imported", Path.GetFileName(source)));
                imported.Add(item);
            }
            if (imported.Count > 0)
            {
                try { await _metadata.SaveAsync(metadata with { Documents = [.. metadata.Documents, .. imported] }, cancellationToken); }
                catch { foreach (var path in copiedPaths) try { File.Delete(path); } catch { } throw; }
                foreach (var path in copiedPaths) _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            }
            return imported;
        }
        finally { _operations.Release(); }
    }

    public async Task ReorderAsync(JsonDocumentId id, int order, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var documents = metadata.Documents.OrderBy(item => item.Order).ToList();
            var current = documents.FindIndex(item => item.Id == id);
            if (current < 0) throw new JujuException(ErrorCode.DocumentNotFound, "The selected document no longer exists.");
            if (order < 0 || order >= documents.Count) throw new JujuException(ErrorCode.InvalidDocumentOrder, "The document order is outside the available range.");
            var document = documents[current];
            documents.RemoveAt(current);
            documents.Insert(order, document);
            await _metadata.SaveAsync(metadata with { Documents = documents.Select((item, index) => item with { Order = index }).ToList() }, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    private async Task<JsonDocumentMetadata> GetAsync(JsonDocumentId id, CancellationToken cancellationToken) =>
        (await _metadata.ReadAsync(cancellationToken)).Documents.SingleOrDefault(document => document.Id == id)
        ?? throw new JujuException(ErrorCode.DocumentNotFound, "The selected document no longer exists.");

    private string DocumentPath(string fileName)
    {
        var safeName = JsonDocumentName.Normalize(fileName);
        var path = Path.GetFullPath(Path.Combine(_documents, safeName));
        if (!path.StartsWith(Path.GetFullPath(_documents) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new JujuException(ErrorCode.InvalidDocumentName, "Document path escapes the documents directory.");
        return path;
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentReconcile);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            JsonMetadata metadata;
            try { metadata = await _metadata.ReadAsync(cancellationToken); }
            catch (JujuException ex) when (ex.Code == ErrorCode.MetadataCorrupted) { metadata = JsonMetadata.Empty(); }
            var files = Directory.EnumerateFiles(_documents, "*.json").Select(Path.GetFileName).OfType<string>().OrderBy(file => File.GetCreationTimeUtc(DocumentPath(file)), Comparer<DateTime>.Default).ThenBy(file => file, StringComparer.Ordinal).ToArray();
            var existing = metadata.Documents.Where(document => files.Contains(document.FileName, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var file in files.Where(file => existing.All(document => !string.Equals(document.FileName, file, StringComparison.OrdinalIgnoreCase))))
            {
                var stamp = new DateTimeOffset(File.GetCreationTimeUtc(DocumentPath(file)));
                existing.Add(new(JsonDocumentId.New(), file, existing.Count, stamp, stamp, new("recovered")));
            }
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var recoveredLast = files.Select(file => TryGetSequence(file, today)).DefaultIfEmpty(0).Max();
            var sequence = new JsonSequence(today, metadata.Sequence.Date == today ? Math.Max(metadata.Sequence.Last, recoveredLast) : recoveredLast);
            var reconciled = metadata with { Sequence = sequence, Documents = existing.Select((document, order) => document with { Order = order }).ToList() };
            if (!Equals(metadata, reconciled)) await _metadata.SaveAsync(reconciled, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    private static int TryGetSequence(string fileName, string date) => fileName.StartsWith(date, StringComparison.Ordinal) && int.TryParse(Path.GetFileNameWithoutExtension(fileName)[date.Length..], out var value) ? value : 0;

    private JsonDocumentId? FindIdByFileName(string fileName)
    {
        try { return _metadata.ReadAsync().GetAwaiter().GetResult().Documents.SingleOrDefault(item => string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase))?.Id; }
        catch { return null; }
    }

    public async ValueTask DisposeAsync() { await _watcher.DisposeAsync(); _operations.Dispose(); }
}
