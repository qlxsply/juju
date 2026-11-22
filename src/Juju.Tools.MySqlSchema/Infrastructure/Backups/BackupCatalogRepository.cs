using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Juju.Core.Storage;
using Juju.Tools.MySqlSchema.Domain;

namespace Juju.Tools.MySqlSchema.Infrastructure.Backups;

public sealed class BackupCatalogRepository(string toolDataDirectory, IAtomicFileWriter writer)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _toolDataDirectory = Path.GetFullPath(toolDataDirectory);
    private readonly string _catalogPath = Path.Combine(Path.GetFullPath(toolDataDirectory), "backup-catalog.json");
    private readonly string _backupsDirectory = Path.Combine(Path.GetFullPath(toolDataDirectory), "backups");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BackupTree> ReadTreeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return new BackupTree((await ReadCoreAsync(cancellationToken)).Environments.ToArray()); }
        finally { _gate.Release(); }
    }

    public async Task<EnvironmentNode> AddEnvironmentAsync(string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name, "环境");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            EnsureUnique(catalog.Environments, item => item.Name, name, "环境名称已存在。");
            var environment = new EnvironmentNode(NewId(), name, []);
            catalog.Environments.Add(environment);
            await WriteCatalogAsync(catalog, cancellationToken);
            return environment;
        }
        finally { _gate.Release(); }
    }

    public async Task<VersionNode> AddVersionAsync(string environmentId, string name,
        CancellationToken cancellationToken = default)
    {
        name = ValidateName(name, "版本");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var environmentIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( environmentIndex < 0 )
                throw new InvalidOperationException("环境不存在。");
            var environment = catalog.Environments[environmentIndex];
            EnsureUnique(environment.Versions, item => item.Name, name, "当前环境下的版本名称已存在。");
            var version = new VersionNode(NewId(), name, []);
            catalog.Environments[environmentIndex] = environment with { Versions = [.. environment.Versions, version] };
            await WriteCatalogAsync(catalog, cancellationToken);
            return version;
        }
        finally { _gate.Release(); }
    }

    public async Task RenameEnvironmentAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name, "环境");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var index = catalog.Environments.FindIndex(item => item.Id == id);
            if ( index < 0 ) throw new InvalidOperationException("环境不存在。");
            EnsureUnique(catalog.Environments.Where(item => item.Id != id), item => item.Name, name, "环境名称已存在。");
            catalog.Environments[index] = catalog.Environments[index] with { Name = name };
            await WriteCatalogAsync(catalog, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RenameVersionAsync(string environmentId, string id, string name,
        CancellationToken cancellationToken = default)
    {
        name = ValidateName(name, "版本");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) throw new InvalidOperationException("环境不存在。");
            var environment = catalog.Environments[envIndex];
            var index = FindIndex(environment.Versions, item => item.Id == id);
            if ( index < 0 ) throw new InvalidOperationException("版本不存在。");
            EnsureUnique(environment.Versions.Where(item => item.Id != id), item => item.Name, name,
                "当前环境下的版本名称已存在。");
            var versions = environment.Versions.ToArray();
            versions[index] = versions[index] with { Name = name };
            catalog.Environments[envIndex] = environment with { Versions = versions };
            await WriteCatalogAsync(catalog, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<BackupDocumentRef> SaveSnapshotAsync(string environmentId, string versionId,
        MySqlConnectionProfile profile, SchemaObject schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(schema);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) throw new InvalidOperationException("目标环境不存在。");
            var environment = catalog.Environments[envIndex];
            var versionIndex = FindIndex(environment.Versions, item => item.Id == versionId);
            if ( versionIndex < 0 ) throw new InvalidOperationException("目标版本不存在。");
            var version = environment.Versions[versionIndex];

            var connectionIndex = FindIndex(version.Connections, item => item.ProfileId == profile.Id);
            var connection = connectionIndex >= 0
                ? version.Connections[connectionIndex]
                : new ConnectionNode(profile.Id, profile.Name, []);
            var databaseIndex = FindIndex(connection.Databases, item => NameEquals(item.Name, schema.DatabaseName,
                schema.LowerCaseTableNames));
            var database = databaseIndex >= 0
                ? connection.Databases[databaseIndex]
                : new DatabaseNode(schema.DatabaseName, schema.LowerCaseTableNames, []);
            if ( database.Documents.Any(item => NameEquals(item.Name, schema.Name, schema.LowerCaseTableNames)) )
                throw new InvalidOperationException($"“{schema.Name}”已在此环境/版本/连接/数据库路径下备份。请删除现有文档后再重新备份。");

            var snapshotId = NewId();
            var snapshot = new SchemaSnapshot(1, snapshotId, DateTimeOffset.UtcNow,
                environment.Id, environment.Name, version.Id, version.Name, profile.Id, profile.Name,
                profile.Host, profile.Port, profile.UserName, schema);
            var snapshotPath = SnapshotPath(environment.Id, version.Id, profile.Id, schema.DatabaseName, snapshotId);
            await writer.WriteTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
            var relativePath = Path.GetRelativePath(_toolDataDirectory, snapshotPath);
            var document = new BackupDocumentRef(snapshotId, schema.Name, schema.Kind, snapshot.CapturedAtUtc,
                relativePath);

            var documents = database.Documents.Append(document).ToArray();
            var databases = connection.Databases.ToArray();
            if ( databaseIndex >= 0 ) databases[databaseIndex] = database with { Documents = documents };
            else databases = [.. databases, database with { Documents = documents }];
            var connections = version.Connections.ToArray();
            if ( connectionIndex >= 0 ) connections[connectionIndex] = connection with { Databases = databases };
            else connections = [.. connections, connection with { Databases = databases }];
            var versions = environment.Versions.ToArray();
            versions[versionIndex] = version with { Connections = connections };
            catalog.Environments[envIndex] = environment with { Versions = versions };

            try { await WriteCatalogAsync(catalog, cancellationToken); }
            catch
            {
                try { File.Delete(snapshotPath); }
                catch ( IOException ) { }
                catch ( UnauthorizedAccessException ) { }
                throw;
            }
            return document;
        }
        finally { _gate.Release(); }
    }

    public async Task<SchemaSnapshot> ReadSnapshotAsync(string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var catalog = await ReadTreeAsync(cancellationToken);
        var reference = FindDocument(catalog.Environments, snapshotId) ??
                        throw new FileNotFoundException("历史结构文档不存在。");
        var path = ResolveSnapshotPath(reference.RelativePath);
        var snapshot = JsonSerializer.Deserialize<SchemaSnapshot>(await File.ReadAllTextAsync(path, cancellationToken),
                            JsonOptions) ?? throw new InvalidDataException("结构快照文件为空或无效。");
        if ( snapshot.SchemaVersion != 1 || snapshot.SnapshotId != reference.Id )
            throw new InvalidDataException("结构快照版本或标识无效。");
        return snapshot;
    }

    public async Task DeleteSnapshotAsync(string environmentId, string versionId, string profileId,
        string databaseName, string snapshotId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) return;
            var environment = catalog.Environments[envIndex];
            var versionIndex = FindIndex(environment.Versions, item => item.Id == versionId);
            if ( versionIndex < 0 ) return;
            var version = environment.Versions[versionIndex];
            var connectionIndex = FindIndex(version.Connections, item => item.ProfileId == profileId);
            if ( connectionIndex < 0 ) return;
            var connection = version.Connections[connectionIndex];
            var databaseIndex = FindIndex(connection.Databases, item => NameEquals(item.Name, databaseName,
                item.LowerCaseTableNames));
            if ( databaseIndex < 0 ) return;
            var database = connection.Databases[databaseIndex];
            var targetDocument = database.Documents.SingleOrDefault(item => item.Id == snapshotId);
            if ( targetDocument is null ) return;
            var snapshotPath = ResolveSnapshotPath(targetDocument.RelativePath);
            var documents = database.Documents.Where(item => item.Id != snapshotId).ToArray();
            if ( documents.Length == database.Documents.Count ) return;

            var databases = connection.Databases.ToArray();
            if ( documents.Length == 0 ) databases = databases.Where((_, index) => index != databaseIndex).ToArray();
            else databases[databaseIndex] = database with { Documents = documents };
            var connections = version.Connections.ToArray();
            if ( databases.Length == 0 ) connections = connections.Where((_, index) => index != connectionIndex).ToArray();
            else connections[connectionIndex] = connection with { Databases = databases };
            var versions = environment.Versions.ToArray();
            versions[versionIndex] = version with { Connections = connections };
            catalog.Environments[envIndex] = environment with { Versions = versions };

            await WriteCatalogAsync(catalog, cancellationToken);
            try { File.Delete(snapshotPath); }
            catch ( FileNotFoundException ) { }
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteEnvironmentAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var item = catalog.Environments.SingleOrDefault(environment => environment.Id == id);
            if ( item is null ) return;
            catalog.Environments.Remove(item);
            await WriteCatalogAsync(catalog, cancellationToken);
            DeleteEnvironmentFiles(item);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteVersionAsync(string environmentId, string versionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) return;
            var environment = catalog.Environments[envIndex];
            var version = environment.Versions.SingleOrDefault(item => item.Id == versionId);
            if ( version is null ) return;
            catalog.Environments[envIndex] = environment with
            { Versions = environment.Versions.Where(item => item.Id != versionId).ToArray() };
            await WriteCatalogAsync(catalog, cancellationToken);
            DeleteVersionFiles(environmentId, version);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteConnectionFolderAsync(string environmentId, string versionId, string profileId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) return;
            var environment = catalog.Environments[envIndex];
            var versionIndex = FindIndex(environment.Versions, item => item.Id == versionId);
            if ( versionIndex < 0 ) return;
            var version = environment.Versions[versionIndex];
            var connectionIndex = FindIndex(version.Connections, item => item.ProfileId == profileId);
            if ( connectionIndex < 0 ) return;
            var connection = version.Connections[connectionIndex];
            var connections = version.Connections.Where(item => item.ProfileId != profileId).ToArray();
            var versions = environment.Versions.ToArray();
            versions[versionIndex] = version with { Connections = connections };
            catalog.Environments[envIndex] = environment with { Versions = versions };
            await WriteCatalogAsync(catalog, cancellationToken);
            DeleteSnapshotFiles(connection.Databases.SelectMany(database => database.Documents));
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteDatabaseFolderAsync(string environmentId, string versionId, string profileId,
        string databaseName, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var catalog = await ReadCoreAsync(cancellationToken);
            var envIndex = catalog.Environments.FindIndex(item => item.Id == environmentId);
            if ( envIndex < 0 ) return;
            var environment = catalog.Environments[envIndex];
            var versionIndex = FindIndex(environment.Versions, item => item.Id == versionId);
            if ( versionIndex < 0 ) return;
            var version = environment.Versions[versionIndex];
            var connectionIndex = FindIndex(version.Connections, item => item.ProfileId == profileId);
            if ( connectionIndex < 0 ) return;
            var connection = version.Connections[connectionIndex];
            var databaseIndex = FindIndex(connection.Databases, item => NameEquals(item.Name, databaseName,
                item.LowerCaseTableNames));
            if ( databaseIndex < 0 ) return;
            var database = connection.Databases[databaseIndex];
            var databases = connection.Databases.Where((_, index) => index != databaseIndex).ToArray();
            var connections = version.Connections.ToArray();
            connections[connectionIndex] = connection with { Databases = databases };
            var versions = environment.Versions.ToArray();
            versions[versionIndex] = version with { Connections = connections };
            catalog.Environments[envIndex] = environment with { Versions = versions };
            await WriteCatalogAsync(catalog, cancellationToken);
            DeleteSnapshotFiles(database.Documents);
        }
        finally { _gate.Release(); }
    }

    private async Task<CatalogDocument> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if ( !File.Exists(_catalogPath) )
        {
            var empty = new CatalogDocument();
            await WriteCatalogAsync(empty, cancellationToken);
            return empty;
        }
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(
                          await File.ReadAllTextAsync(_catalogPath, cancellationToken), JsonOptions) ??
                      throw new InvalidDataException("备份目录索引为空或无效。");
        if ( catalog.SchemaVersion != 1 )
            throw new InvalidDataException($"不支持的备份目录版本：{catalog.SchemaVersion}。");
        return catalog;
    }

    private Task WriteCatalogAsync(CatalogDocument catalog, CancellationToken cancellationToken) =>
        writer.WriteTextAsync(_catalogPath, JsonSerializer.Serialize(catalog, JsonOptions), cancellationToken);

    private string SnapshotPath(string environmentId, string versionId, string connectionId, string databaseName,
        string snapshotId) => Path.Combine(_backupsDirectory, environmentId, versionId, connectionId,
        DatabaseKey(databaseName), snapshotId + ".json");

    private static string DatabaseKey(string name) => "db-" + Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..24];

    private static BackupDocumentRef? FindDocument(IEnumerable<EnvironmentNode> environments, string id) =>
        environments.SelectMany(environment => environment.Versions)
            .SelectMany(version => version.Connections)
            .SelectMany(connection => connection.Databases)
            .SelectMany(database => database.Documents)
            .FirstOrDefault(document => document.Id == id);

    private string ResolveSnapshotPath(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_toolDataDirectory, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(_toolDataDirectory) + Path.DirectorySeparatorChar;
        if ( !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) )
            throw new InvalidDataException("备份文档路径越出 MySQL Schema 数据目录。");
        return path;
    }

    private void DeleteEnvironmentFiles(EnvironmentNode environment)
    {
        var directory = Path.Combine(_backupsDirectory, environment.Id);
        if ( Directory.Exists(directory) )
            Directory.Delete(directory, recursive: true);
    }

    private void DeleteVersionFiles(string environmentId, VersionNode version)
    {
        var directory = Path.Combine(_backupsDirectory, environmentId, version.Id);
        if ( Directory.Exists(directory) )
            Directory.Delete(directory, recursive: true);
    }

    private void DeleteSnapshotFiles(IEnumerable<BackupDocumentRef> documents)
    {
        foreach ( var document in documents )
        {
            try
            {
                var path = ResolveSnapshotPath(document.RelativePath);
                if ( File.Exists(path) ) File.Delete(path);
            }
            catch ( FileNotFoundException ) { }
        }
    }

    private static string ValidateName(string name, string subject)
    {
        var trimmed = name?.Trim();
        if ( string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 100 )
            throw new InvalidOperationException($"{subject}名称必填且不能超过 100 个字符。");
        return trimmed;
    }

    private static void EnsureUnique<T>(IEnumerable<T> siblings, Func<T, string> name, string candidate,
        string message)
    {
        if ( siblings.Any(item => string.Equals(name(item), candidate, StringComparison.OrdinalIgnoreCase)) )
            throw new InvalidOperationException(message);
    }

    private static int FindIndex<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        for ( var index = 0; index < items.Count; index++ )
            if ( predicate(items[index]) )
                return index;
        return -1;
    }

    private static bool NameEquals(string left, string right, string? lowerCaseTableNames) =>
        string.Equals(left, right, lowerCaseTableNames == "0" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private sealed class CatalogDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<EnvironmentNode> Environments { get; set; } = [];
    }
}
