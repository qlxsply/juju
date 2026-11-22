namespace Juju.Tools.MySqlSchema.Domain;

public static class MySqlSslModes
{
    public static readonly IReadOnlyList<string> All = ["Preferred", "Required", "VerifyCA", "VerifyFull"];
}

public enum SchemaObjectKind
{
    Table,
    View
}

public enum SchemaValueKind
{
    NotApplicable,
    Missing,
    Null,
    Literal,
    Expression
}

public sealed record SchemaValue(SchemaValueKind Kind, string? Value = null);

public sealed record MySqlConnectionProfile(
    string Id,
    string Name,
    string Host,
    uint Port,
    string UserName,
    string ProtectedPassword,
    string? DefaultDatabase,
    string SslMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConnectionProfileDraft(
    string? Id,
    string Name,
    string Host,
    uint Port,
    string UserName,
    string Password,
    bool KeepExistingPassword,
    string? DefaultDatabase,
    string SslMode);

/// <summary>Password is transient form data and is never serialized into profile files.</summary>
public sealed record ConnectionDraft(
    string? Id,
    string Name,
    string Host,
    uint Port,
    string UserName,
    string Password,
    bool KeepExistingPassword,
    string? DefaultDatabase,
    string SslMode);

public sealed record ConnectionTestResult(bool Success, string Message, TimeSpan Elapsed,
    string? ServerVersion = null);

public sealed record DatabaseInfo(string Name, string? CharacterSet, string? Collation);
public sealed record DatabaseObjectInfo(string Name, SchemaObjectKind Kind);
public sealed record TableProperty(string Name, string? Engine, string? RowFormat, string? CharacterSet,
    string? Collation, string? Comment, ulong? AutoIncrement, string? CreateOptions);
public sealed record SchemaColumn(int Ordinal, string Name, string DataType, string? ColumnType,
    bool Nullable, SchemaValue Default, string? Extra, string? CharacterSet, string? Collation,
    string? Comment, string? GenerationExpression, bool? Visible);
public sealed record SchemaIndexPart(int Sequence, string? ColumnName, string? Expression, int? PrefixLength,
    string? SortDirection);
public sealed record SchemaIndex(string Name, bool Unique, string Type, bool? Visible, string? Parser,
    string? Comment, IReadOnlyList<SchemaIndexPart> Parts);
public sealed record SchemaForeignKeyPart(int Sequence, string ColumnName, string ReferencedColumnName);
public sealed record SchemaForeignKey(string Name, string ReferencedSchema, string ReferencedTable,
    string UpdateRule, string DeleteRule, string? MatchOption, IReadOnlyList<SchemaForeignKeyPart> Parts);
public sealed record SchemaCheckConstraint(string Name, string Expression, bool? Enforced);
public sealed record SchemaPartition(int Sequence, string? Name, string? Method, string? Expression,
    string? Description, ulong? Rows);
public sealed record SchemaViewProperty(string Algorithm, string SecurityType, string? Definer,
    string CheckOption, string? Definition, bool? IsUpdatable);

public sealed record SchemaObject(
    string DatabaseName,
    string Name,
    SchemaObjectKind Kind,
    string ServerVersion,
    string? LowerCaseTableNames,
    DatabaseInfo Database,
    IReadOnlyList<SchemaColumn> Columns,
    IReadOnlyList<SchemaIndex> Indexes,
    IReadOnlyList<SchemaForeignKey> ForeignKeys,
    IReadOnlyList<SchemaCheckConstraint> CheckConstraints,
    IReadOnlyList<SchemaPartition> Partitions,
    TableProperty? Table,
    SchemaViewProperty? View,
    string? OriginalDdl,
    IReadOnlyList<string> CaptureWarnings);

public sealed record SchemaSnapshot(
    int SchemaVersion,
    string SnapshotId,
    DateTimeOffset CapturedAtUtc,
    string EnvironmentId,
    string EnvironmentName,
    string VersionId,
    string VersionName,
    string ConnectionId,
    string ConnectionNameAtCapture,
    string HostAtCapture,
    uint PortAtCapture,
    string UserNameAtCapture,
    SchemaObject Object);

public sealed record BackupTree(IReadOnlyList<EnvironmentNode> Environments);
public sealed record EnvironmentNode(string Id, string Name, IReadOnlyList<VersionNode> Versions);
public sealed record VersionNode(string Id, string Name, IReadOnlyList<ConnectionNode> Connections);
public sealed record ConnectionNode(string ProfileId, string NameAtFirstBackup, IReadOnlyList<DatabaseNode> Databases);
public sealed record DatabaseNode(string Name, string? LowerCaseTableNames, IReadOnlyList<BackupDocumentRef> Documents);
public sealed record BackupDocumentRef(string Id, string Name, SchemaObjectKind Kind, DateTimeOffset CapturedAtUtc,
    string RelativePath);

public enum SchemaDifferenceKind
{
    Same,
    MissingInA,
    MissingInB,
    Modified
}

public sealed record PropertyDifference(string Property, string? ValueA, string? ValueB, bool Changed);
public sealed record ObjectDifference(string Category, string Key, SchemaDifferenceKind Kind,
    IReadOnlyList<PropertyDifference> Properties);
public sealed record SchemaComparisonResult(SchemaObject SourceA, SchemaObject SourceB,
    IReadOnlyList<ObjectDifference> Differences)
{
    public int MissingCount => Differences.Count(item => item.Kind is SchemaDifferenceKind.MissingInA or SchemaDifferenceKind.MissingInB);
    public int ModifiedCount => Differences.Count(item => item.Kind == SchemaDifferenceKind.Modified);
    public int ChangedPropertyCount => Differences.Sum(item => item.Properties.Count(property => property.Changed));
}
