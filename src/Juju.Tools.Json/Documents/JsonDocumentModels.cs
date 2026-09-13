namespace Juju.Tools.Json.Documents;

/// <summary>
/// 文档 GUID 的强类型包装，防止把任意 <see cref="Guid"/> 误传入文档 API。<c>readonly record struct</c>
/// 是无堆分配的值类型，同时获得 record 的值相等性，接近 Java 的小型不可变值对象。
/// </summary>
public readonly record struct JsonDocumentId(Guid Value)
{
    /// <summary>生成新的随机文档标识。</summary>
    public static JsonDocumentId New() => new(Guid.NewGuid());

    /// <summary>以 GUID 标准文本表示标识，便于序列化和日志。</summary>
    public override string ToString() => Value.ToString();
}

/// <summary>记录文档来源类型，以及导入时可选的原始文件名。</summary>
public sealed record JsonDocumentSource(string Kind, string? OriginalFileName = null);

/// <summary>文档列表展示和定位所需的持久化元数据。</summary>
public sealed record JsonDocumentMetadata(
    JsonDocumentId Id,
    string FileName,
    int Order,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    JsonDocumentSource Source);

/// <summary>按日期生成默认文件名时使用的最后序号。</summary>
public sealed record JsonSequence(string Date, int Last);

/// <summary>metadata.json 的 schema 化根对象。</summary>
public sealed record JsonMetadata(int SchemaVersion, JsonSequence Sequence, List<JsonDocumentMetadata> Documents)
{
    /// <summary>创建 schema 版本 1 的空元数据，并从当天序号零开始。</summary>
    public static JsonMetadata Empty() => new(1, new(DateTime.UtcNow.ToString("yyyyMMdd"), 0), []);
}

/// <summary>文件长度、最后写入时间和内容哈希组成的乐观并发版本戳。</summary>
public sealed record DocumentRevision(long Length, DateTime LastWriteTimeUtc, string Hash);

/// <summary>打开文档时返回的内容与版本快照，保存时可回传版本用于冲突检测。</summary>
public sealed record JsonDocumentSnapshot(JsonDocumentId DocumentId, string Content, DocumentRevision Revision);

/// <summary>编辑器保存流程的 UI 状态；冲突与写入失败分开以支持不同恢复动作。</summary>
public enum SaveState
{
    Clean,
    Dirty,
    Saving,
    SaveFailed,
    Conflict
}

/// <summary>文件观察器归一化后的外部文档变更种类。</summary>
public enum ExternalDocumentChangeKind
{
    Changed,
    Deleted,
    Renamed
}

/// <summary>外部文件变更事件；删除时没有可读取的 <see cref="DocumentRevision"/>。</summary>
public sealed record JsonDocumentExternalChange(
    JsonDocumentId DocumentId,
    ExternalDocumentChangeKind Kind,
    DocumentRevision? Revision);