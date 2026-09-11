using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Documents;

public interface IJsonDocumentService
{
    Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default);
    Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default);
    Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision, CancellationToken cancellationToken = default);
    Task RenameAsync(JsonDocumentId id, string name, CancellationToken cancellationToken = default);
    Task DeleteAsync(JsonDocumentId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default);
}

public sealed class JsonDocumentService : IJsonDocumentService
{
    private readonly string _documents;
    private readonly string _trash;
    private readonly JsonMetadataStore _metadata;
    private readonly IAtomicFileWriter _writer;
    private readonly DocumentRevisionService _revisions;
    private readonly SemaphoreSlim _operations = new(1, 1);

    public JsonDocumentService(string dataRoot, JsonMetadataStore metadata, IAtomicFileWriter writer, DocumentRevisionService revisions)
    {
        _documents = Path.Combine(dataRoot, "json", "documents");
        _trash = Path.Combine(dataRoot, "json", ".trash");
        _metadata = metadata;
        _writer = writer;
        _revisions = revisions;
    }

    public async Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileAsync(cancellationToken);
        return (await _metadata.ReadAsync(cancellationToken)).Documents.OrderBy(document => document.Order).ToArray();
    }

    public async Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(id, cancellationToken);
        var path = DocumentPath(document.FileName);
        if (!File.Exists(path)) throw new JujuException(ErrorCode.DocumentNotFound, "The document file no longer exists.");
        return new(id, await File.ReadAllTextAsync(path, cancellationToken), await _revisions.GetAsync(path, cancellationToken));
    }

    public async Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default)
    {
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
            await _writer.WriteTextAsync(DocumentPath(fileName), "{}", cancellationToken);
            await _metadata.SaveAsync(metadata with { Sequence = new(today, sequence), Documents = [.. metadata.Documents, document] }, cancellationToken);
            return document;
        }
        finally { _operations.Release(); }
    }

    public async Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            if (!Equals(await _revisions.GetAsync(path, cancellationToken), expectedRevision))
                throw new JujuException(ErrorCode.ExternalModificationConflict, "The file changed outside juju and was not overwritten.");
            await _writer.WriteTextAsync(path, content, cancellationToken);
            await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Select(item => item.Id == id ? item with { UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList() }, cancellationToken);
        }
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
            File.Move(oldPath, targetPath);
            try { await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Select(item => item.Id == id ? item with { FileName = targetName, UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList() }, cancellationToken); }
            catch { try { File.Move(targetPath, oldPath); } catch { } throw; }
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
            File.Move(source, trash);
            try { await _metadata.UpdateAsync(current => current with { Documents = current.Documents.Where(item => item.Id != id).Select((item, order) => item with { Order = order }).ToList() }, cancellationToken); }
            catch { try { File.Move(trash, source); } catch { } throw; }
        }
        finally { _operations.Release(); }
    }

    public async Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default)
    {
        var imported = new List<JsonDocumentMetadata>();
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            foreach (var source in sourcePaths)
            {
                if (!string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase)) continue;
                var baseName = JsonDocumentName.Normalize(Path.GetFileName(source));
                var candidate = baseName;
                for (var suffix = 2; File.Exists(DocumentPath(candidate)); suffix++) candidate = $"{Path.GetFileNameWithoutExtension(baseName)}-{suffix}.json";
                await _writer.WriteTextAsync(DocumentPath(candidate), await File.ReadAllTextAsync(source, cancellationToken), cancellationToken);
                var now = DateTimeOffset.UtcNow;
                var item = new JsonDocumentMetadata(JsonDocumentId.New(), candidate, metadata.Documents.Count + imported.Count, now, now, new("imported", Path.GetFileName(source)));
                imported.Add(item);
            }
            if (imported.Count > 0) await _metadata.SaveAsync(metadata with { Documents = [.. metadata.Documents, .. imported] }, cancellationToken);
            return imported;
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
        await _operations.WaitAsync(cancellationToken);
        try
        {
            JsonMetadata metadata;
            try { metadata = await _metadata.ReadAsync(cancellationToken); }
            catch (JujuException ex) when (ex.Code == ErrorCode.MetadataCorrupted) { metadata = JsonMetadata.Empty(); }
            var files = Directory.EnumerateFiles(_documents, "*.json").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var existing = metadata.Documents.Where(document => files.Contains(document.FileName, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var file in files.Where(file => existing.All(document => !string.Equals(document.FileName, file, StringComparison.OrdinalIgnoreCase))))
            {
                var stamp = new DateTimeOffset(File.GetCreationTimeUtc(DocumentPath(file)));
                existing.Add(new(JsonDocumentId.New(), file, existing.Count, stamp, stamp, new("recovered")));
            }
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var recoveredLast = files.Select(file => TryGetSequence(file, today)).DefaultIfEmpty(0).Max();
            var sequence = metadata.Sequence.Date == today
                ? new JsonSequence(today, Math.Max(metadata.Sequence.Last, recoveredLast))
                : metadata.Sequence;
            var reconciled = metadata with { Sequence = sequence, Documents = existing.Select((document, order) => document with { Order = order }).ToList() };
            if (!Equals(metadata, reconciled)) await _metadata.SaveAsync(reconciled, cancellationToken);
        }
        finally { _operations.Release(); }
    }

    private static int TryGetSequence(string fileName, string date) => fileName.StartsWith(date, StringComparison.Ordinal) && int.TryParse(Path.GetFileNameWithoutExtension(fileName)[date.Length..], out var value) ? value : 0;
}
