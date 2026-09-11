namespace Juju.Tools.Json.Documents;

public readonly record struct JsonDocumentId(Guid Value)
{
    public static JsonDocumentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record JsonDocumentSource(string Kind, string? OriginalFileName = null);
public sealed record JsonDocumentMetadata(JsonDocumentId Id, string FileName, int Order, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, JsonDocumentSource Source);
public sealed record JsonSequence(string Date, int Last);
public sealed record JsonMetadata(int SchemaVersion, JsonSequence Sequence, List<JsonDocumentMetadata> Documents)
{
    public static JsonMetadata Empty() => new(1, new(DateTime.UtcNow.ToString("yyyyMMdd"), 0), []);
}
public sealed record DocumentRevision(long Length, DateTime LastWriteTimeUtc, string Hash);
public sealed record JsonDocumentSnapshot(JsonDocumentId DocumentId, string Content, DocumentRevision Revision);
public enum SaveState { Clean, Dirty, Saving, SaveFailed, Conflict }
public enum ExternalDocumentChangeKind { Changed, Deleted, Renamed }
public sealed record JsonDocumentExternalChange(JsonDocumentId DocumentId, ExternalDocumentChangeKind Kind, DocumentRevision? Revision);
