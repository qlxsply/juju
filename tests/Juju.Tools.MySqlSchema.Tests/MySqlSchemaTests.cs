using Juju.Core.Storage;
using Juju.Tools.MySqlSchema.Application;
using Juju.Tools.MySqlSchema.Domain;
using Juju.Tools.MySqlSchema.Infrastructure.Backups;
using Juju.Tools.MySqlSchema.Infrastructure.Connections;
using Juju.Tools.MySqlSchema.Infrastructure.Security;
using Xunit;

namespace Juju.Tools.MySqlSchema.Tests;

public sealed class MySqlSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "juju-mysql-schema-tests", Guid.NewGuid().ToString("N"));
    private readonly AtomicFileWriter _writer = new();

    [Fact]
    public async Task Connection_profiles_are_saved_with_protected_passwords_and_keep_secrets_on_edit()
    {
        var repository = NewConnectionRepository();
        var created = await repository.SaveAsync(new ConnectionProfileDraft(null, "开发", "127.0.0.1", 3306,
            "app", "secret", false, "db_app", "Preferred"));

        Assert.NotEqual("secret", created.ProtectedPassword);
        Assert.Equal("secret", repository.RevealPassword(created));
        var edited = await repository.SaveAsync(new ConnectionProfileDraft(created.Id, "本地开发", "localhost", 3306,
            "app", "", true, null, "Preferred"));

        Assert.Equal(created.Id, edited.Id);
        Assert.Equal(created.ProtectedPassword, edited.ProtectedPassword);
        Assert.Equal("secret", repository.RevealPassword(edited));
        Assert.Equal("本地开发", Assert.Single(await repository.ListAsync()).Name);
    }

    [Fact]
    public async Task Backup_catalog_enforces_one_object_per_path_and_preserves_snapshots_independently()
    {
        var repository = NewBackupRepository();
        var environment = await repository.AddEnvironmentAsync("生产");
        var version = await repository.AddVersionAsync(environment.Id, "V1.0");
        var profile = new MySqlConnectionProfile("profile-1", "生产库", "localhost", 3306, "readonly",
            "cipher", "app", "Preferred", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var schema = SampleTable("customer");

        var first = await repository.SaveSnapshotAsync(environment.Id, version.Id, profile, schema);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveSnapshotAsync(environment.Id,
            version.Id, profile, SampleTable("CUSTOMER") with { Kind = SchemaObjectKind.View }));
        var loaded = await repository.ReadSnapshotAsync(first.Id);
        Assert.Equal("customer", loaded.Object.Name);
        Assert.Equal(schema.OriginalDdl, loaded.Object.OriginalDdl);

        await repository.RenameEnvironmentAsync(environment.Id, "生产环境");
        Assert.Equal("生产环境", Assert.Single((await repository.ReadTreeAsync()).Environments).Name);
        await repository.DeleteSnapshotAsync(environment.Id, version.Id, profile.Id, schema.DatabaseName, first.Id);
        Assert.Empty(Assert.Single((await repository.ReadTreeAsync()).Environments).Versions[0].Connections);

        var replacement = await repository.SaveSnapshotAsync(environment.Id, version.Id, profile, schema);
        Assert.NotEqual(first.Id, replacement.Id);
    }

    [Fact]
    public void Comparison_reports_missing_objects_and_property_level_changes()
    {
        var a = SampleTable("customer") with
        {
            Columns = [
                new SchemaColumn(1, "id", "int", "int", false, new(SchemaValueKind.Missing), null,
                    null, null, "", null, true),
                new SchemaColumn(2, "email", "varchar", "varchar(80)", false,
                    new(SchemaValueKind.Missing), null, "utf8mb4", "utf8mb4_0900_ai_ci", "", null, true)
            ]
        };
        var b = a with
        {
            Columns = [
                a.Columns[0],
                a.Columns[1] with { ColumnType = "varchar(160)" },
                new SchemaColumn(3, "phone", "varchar", "varchar(32)", true,
                    new(SchemaValueKind.Missing), null, "utf8mb4", "utf8mb4_0900_ai_ci", "", null, true)
            ]
        };

        var result = SchemaComparisonService.Compare(a, b);

        Assert.Contains(result.Differences, item => item.Category == "字段" && item.Key == "email" &&
            item.Kind == SchemaDifferenceKind.Modified && item.Properties.Any(property => property.Property == "columnType" && property.Changed));
        Assert.Contains(result.Differences, item => item.Category == "字段" && item.Key == "phone" &&
            item.Kind == SchemaDifferenceKind.MissingInA);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(1, result.ModifiedCount);
    }

    [Fact]
    public void Migration_sql_is_directional_review_only_and_quotes_identifiers()
    {
        var source = SampleTable("target`table");
        var target = source with { Columns = [] };

        var draft = MigrationSqlGenerator.Generate(source, target, aToB: true);

        Assert.Equal("A → B", draft.Direction);
        Assert.Contains("ALTER TABLE `db_app`.`target``table` ADD COLUMN `id`", draft.Sql, StringComparison.Ordinal);
        Assert.Contains("仅供审阅", string.Join('\n', draft.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_sql_does_not_drop_an_index_when_its_replacement_is_unsupported()
    {
        var source = SampleTable("customer") with
        {
            Indexes = [new SchemaIndex("ix_spatial", false, "SPATIAL", true, null, null,
                [new(1, "id", null, null, "A")])]
        };
        var target = source with
        {
            Indexes = [new SchemaIndex("ix_spatial", false, "BTREE", true, null, null,
                [new(1, "id", null, null, "A")])]
        };

        var draft = MigrationSqlGenerator.Generate(source, target, aToB: true);

        Assert.DoesNotContain("DROP INDEX `ix_spatial`", draft.Sql, StringComparison.Ordinal);
        Assert.Contains(draft.Warnings, warning => warning.Contains("未生成变更 SQL", StringComparison.Ordinal));
    }

    private ConnectionProfileRepository NewConnectionRepository() =>
        new(Path.Combine(_root, "connections"), _writer, new WindowsCredentialProtector());

    private BackupCatalogRepository NewBackupRepository() =>
        new(Path.Combine(_root, "backup"), _writer);

    private static SchemaObject SampleTable(string name) => new("db_app", name, SchemaObjectKind.Table,
        "8.0.36", "1", new DatabaseInfo("db_app", "utf8mb4", "utf8mb4_0900_ai_ci"),
        [new SchemaColumn(1, "id", "bigint", "bigint unsigned", false, new(SchemaValueKind.Missing),
            "auto_increment", null, null, "primary key", null, true)],
        [new SchemaIndex("PRIMARY", true, "BTREE", true, null, "", [new(1, "id", null, null, "A")])],
        [], [], [], new TableProperty(name, "InnoDB", "Dynamic", "utf8mb4", "utf8mb4_0900_ai_ci", "", 1, ""),
        null, $"CREATE TABLE `{name}` (`id` bigint unsigned NOT NULL AUTO_INCREMENT, PRIMARY KEY (`id`)) ENGINE=InnoDB;", []);

    public void Dispose()
    {
        if ( Directory.Exists(_root) )
            Directory.Delete(_root, recursive: true);
    }
}
