using Juju.Tools.MySqlSchema.Domain;
using Juju.Tools.MySqlSchema.Infrastructure.Backups;
using Juju.Tools.MySqlSchema.Infrastructure.Connections;
using Juju.Tools.MySqlSchema.Infrastructure.Database;

namespace Juju.Tools.MySqlSchema.Application;

public sealed record BackupObjectResult(string ObjectName, SchemaObjectKind Kind, bool Success, string Message,
    string? SnapshotId = null);

public sealed class MySqlSchemaWorkspace(ConnectionProfileRepository connections, MySqlSchemaReader reader,
    BackupCatalogRepository backups)
{
    public Task<IReadOnlyList<MySqlConnectionProfile>> ListConnectionsAsync(CancellationToken cancellationToken = default) =>
        connections.ListAsync(cancellationToken);

    public Task<MySqlConnectionProfile> GetConnectionAsync(string id, CancellationToken cancellationToken = default) =>
        connections.GetAsync(id, cancellationToken);

    public async Task<string> GetConnectionPasswordAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(id, cancellationToken);
        return connections.RevealPassword(profile);
    }

    public Task<MySqlConnectionProfile> SaveConnectionAsync(ConnectionProfileDraft draft,
        CancellationToken cancellationToken = default) => connections.SaveAsync(draft, cancellationToken);

    public Task DeleteConnectionAsync(string id, CancellationToken cancellationToken = default) =>
        connections.DeleteAsync(id, cancellationToken);

    public Task<string> ExportConnectionsAsync(bool includePasswords,
        CancellationToken cancellationToken = default) => connections.ExportJsonAsync(includePasswords, cancellationToken);

    public async Task<ConnectionTestResult> TestConnectionAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(id, cancellationToken);
        return await reader.TestConnectionAsync(profile, connections.RevealPassword(profile), cancellationToken);
    }

    public async Task<IReadOnlyList<DatabaseInfo>> ListDatabasesAsync(string id,
        CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(id, cancellationToken);
        return await reader.ListDatabasesAsync(profile, connections.RevealPassword(profile), cancellationToken);
    }

    public async Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(string id, string database,
        CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(id, cancellationToken);
        return await reader.ListObjectsAsync(profile, connections.RevealPassword(profile), database, cancellationToken);
    }

    public async Task<SchemaObject> ReadLiveObjectAsync(string connectionId, string database,
        DatabaseObjectInfo item, CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(connectionId, cancellationToken);
        return await reader.ReadObjectAsync(profile, connections.RevealPassword(profile), database, item,
            cancellationToken);
    }

    public Task<BackupTree> ReadBackupTreeAsync(CancellationToken cancellationToken = default) =>
        backups.ReadTreeAsync(cancellationToken);

    public Task<EnvironmentNode> AddEnvironmentAsync(string name, CancellationToken cancellationToken = default) =>
        backups.AddEnvironmentAsync(name, cancellationToken);

    public Task<VersionNode> AddVersionAsync(string environmentId, string name,
        CancellationToken cancellationToken = default) => backups.AddVersionAsync(environmentId, name, cancellationToken);

    public Task RenameEnvironmentAsync(string id, string name, CancellationToken cancellationToken = default) =>
        backups.RenameEnvironmentAsync(id, name, cancellationToken);

    public Task RenameVersionAsync(string environmentId, string id, string name,
        CancellationToken cancellationToken = default) => backups.RenameVersionAsync(environmentId, id, name, cancellationToken);

    public Task DeleteEnvironmentAsync(string id, CancellationToken cancellationToken = default) =>
        backups.DeleteEnvironmentAsync(id, cancellationToken);

    public Task DeleteVersionAsync(string environmentId, string id, CancellationToken cancellationToken = default) =>
        backups.DeleteVersionAsync(environmentId, id, cancellationToken);

    public Task DeleteBackupConnectionAsync(string environmentId, string versionId, string connectionId,
        CancellationToken cancellationToken = default) =>
        backups.DeleteConnectionFolderAsync(environmentId, versionId, connectionId, cancellationToken);

    public Task DeleteBackupDatabaseAsync(string environmentId, string versionId, string connectionId,
        string databaseName, CancellationToken cancellationToken = default) =>
        backups.DeleteDatabaseFolderAsync(environmentId, versionId, connectionId, databaseName, cancellationToken);

    public Task<SchemaSnapshot> ReadSnapshotAsync(string snapshotId, CancellationToken cancellationToken = default) =>
        backups.ReadSnapshotAsync(snapshotId, cancellationToken);

    public Task DeleteSnapshotAsync(string environmentId, string versionId, string connectionId, string databaseName,
        string snapshotId, CancellationToken cancellationToken = default) =>
        backups.DeleteSnapshotAsync(environmentId, versionId, connectionId, databaseName, snapshotId, cancellationToken);

    public async Task<IReadOnlyList<BackupObjectResult>> BackupAsync(string environmentId, string versionId,
        string connectionId, string databaseName, IReadOnlyCollection<DatabaseObjectInfo> selected,
        CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(connectionId, cancellationToken);
        var password = connections.RevealPassword(profile);
        var result = new List<BackupObjectResult>(selected.Count);
        foreach ( var item in selected )
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var schema = await reader.ReadObjectAsync(profile, password, databaseName, item, cancellationToken);
                var saved = await backups.SaveSnapshotAsync(environmentId, versionId, profile, schema, cancellationToken);
                result.Add(new BackupObjectResult(item.Name, item.Kind, true, "备份成功", saved.Id));
            }
            catch ( OperationCanceledException ) { throw; }
            catch ( Exception error )
            {
                result.Add(new BackupObjectResult(item.Name, item.Kind, false, error.Message));
            }
        }
        return result;
    }

    public async Task<IReadOnlySet<string>> GetBackedObjectNamesAsync(string environmentId, string versionId,
        string connectionId, string databaseName, CancellationToken cancellationToken = default)
    {
        var profile = await connections.GetAsync(connectionId, cancellationToken);
        var lowerCaseTableNames = await reader.GetLowerCaseTableNamesAsync(profile,
            connections.RevealPassword(profile), cancellationToken);
        var comparison = lowerCaseTableNames == "0" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var comparer = lowerCaseTableNames == "0" ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var tree = await backups.ReadTreeAsync(cancellationToken);
        var env = tree.Environments.SingleOrDefault(item => item.Id == environmentId);
        var version = env?.Versions.SingleOrDefault(item => item.Id == versionId);
        var connection = version?.Connections.SingleOrDefault(item => item.ProfileId == connectionId);
        var database = connection?.Databases.SingleOrDefault(item =>
            string.Equals(item.Name, databaseName, comparison));
        return new HashSet<string>(database?.Documents.Select(item => item.Name) ?? [], comparer);
    }

    public async Task<int> CountSnapshotsForConnectionAsync(string connectionId,
        CancellationToken cancellationToken = default)
    {
        var tree = await backups.ReadTreeAsync(cancellationToken);
        return tree.Environments.SelectMany(environment => environment.Versions)
            .SelectMany(version => version.Connections)
            .Where(connection => connection.ProfileId == connectionId)
            .SelectMany(connection => connection.Databases)
            .Sum(database => database.Documents.Count);
    }

    public static SchemaComparisonResult Compare(SchemaObject a, SchemaObject b) => SchemaComparisonService.Compare(a, b);
}
