using System.Data;
using System.Globalization;
using MySqlConnector;
using Juju.Tools.MySqlSchema.Domain;

namespace Juju.Tools.MySqlSchema.Infrastructure.Database;

public sealed class MySqlSchemaReader
{
    private const int ConnectTimeoutSeconds = 10;
    private const int CommandTimeoutSeconds = 30;

    public async Task<ConnectionTestResult> TestConnectionAsync(MySqlConnectionProfile profile, string password,
        CancellationToken cancellationToken = default)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var connection = CreateConnection(profile, password);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand("SELECT VERSION()", connection)
            { CommandTimeout = CommandTimeoutSeconds };
            var serverVersion = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            timer.Stop();
            return new ConnectionTestResult(true, "连接成功", timer.Elapsed, serverVersion ?? "未知版本");
        }
        catch ( OperationCanceledException ) { throw; }
        catch ( MySqlException error )
        {
            timer.Stop();
            return new ConnectionTestResult(false, DescribeConnectionError(error), timer.Elapsed);
        }
        catch ( Exception error ) when ( error is ArgumentException or InvalidOperationException )
        {
            timer.Stop();
            return new ConnectionTestResult(false, error.Message, timer.Elapsed);
        }
    }

    public async Task<IReadOnlyList<DatabaseInfo>> ListDatabasesAsync(MySqlConnectionProfile profile, string password,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile, password, useProfileDatabase: false);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
            FROM INFORMATION_SCHEMA.SCHEMATA
            ORDER BY SCHEMA_NAME
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DatabaseInfo>();
        while ( await reader.ReadAsync(cancellationToken) )
            result.Add(new DatabaseInfo(reader.GetString(0), NullableString(reader, 1), NullableString(reader, 2)));
        return result;
    }

    public async Task<IReadOnlyList<DatabaseObjectInfo>> ListObjectsAsync(MySqlConnectionProfile profile,
        string password, string database, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile, password, database);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand("""
            SELECT TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema
              AND TABLE_TYPE IN ('BASE TABLE', 'VIEW')
            ORDER BY TABLE_NAME
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<DatabaseObjectInfo>();
        while ( await reader.ReadAsync(cancellationToken) )
        {
            var type = reader.GetString(1);
            result.Add(new DatabaseObjectInfo(reader.GetString(0),
                type == "VIEW" ? SchemaObjectKind.View : SchemaObjectKind.Table));
        }
        return result;
    }

    public async Task<string?> GetLowerCaseTableNamesAsync(MySqlConnectionProfile profile, string password,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile, password);
        await connection.OpenAsync(cancellationToken);
        return await ReadLowerCaseTableNamesAsync(connection, cancellationToken);
    }

    public async Task<SchemaObject> ReadObjectAsync(MySqlConnectionProfile profile, string password, string database,
        DatabaseObjectInfo item, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(profile, password, database);
        await connection.OpenAsync(cancellationToken);
        var warnings = new List<string>();
        var serverVersion = connection.ServerVersion;
        var lowerCaseTableNames = await ReadLowerCaseTableNamesAsync(connection, cancellationToken);
        var databaseInfo = await ReadDatabaseInfoAsync(connection, database, cancellationToken);
        var tableProperty = item.Kind == SchemaObjectKind.Table
            ? await ReadTablePropertyAsync(connection, database, item.Name, cancellationToken)
            : null;
        var columns = await ReadColumnsAsync(connection, database, item.Name, cancellationToken);
        var indexes = item.Kind == SchemaObjectKind.Table
            ? await ReadIndexesAsync(connection, database, item.Name, cancellationToken)
            : [];
        var foreignKeys = item.Kind == SchemaObjectKind.Table
            ? await ReadForeignKeysAsync(connection, database, item.Name, cancellationToken)
            : [];
        var checks = item.Kind == SchemaObjectKind.Table
            ? await ReadCheckConstraintsAsync(connection, database, item.Name, serverVersion, warnings, cancellationToken)
            : [];
        var partitions = item.Kind == SchemaObjectKind.Table
            ? await ReadPartitionsAsync(connection, database, item.Name, cancellationToken)
            : [];
        var view = item.Kind == SchemaObjectKind.View
            ? await ReadViewAsync(connection, database, item.Name, cancellationToken)
            : null;
        var ddl = await ReadCreateStatementAsync(connection, database, item, cancellationToken);
        if ( string.IsNullOrWhiteSpace(ddl) )
            warnings.Add("MySQL did not return a CREATE statement for this object.");

        return new SchemaObject(database, item.Name, item.Kind, serverVersion, lowerCaseTableNames, databaseInfo,
            columns, indexes, foreignKeys, checks, partitions, tableProperty, view, ddl, warnings);
    }

    private static MySqlConnection CreateConnection(MySqlConnectionProfile profile, string password,
        string? database = null, bool useProfileDatabase = true)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = profile.Host,
            Port = profile.Port,
            UserID = profile.UserName,
            Password = password,
            ConnectionTimeout = ConnectTimeoutSeconds,
            DefaultCommandTimeout = (uint) CommandTimeoutSeconds,
            SslMode = Enum.Parse<MySqlSslMode>(profile.SslMode, ignoreCase: false)
        };
        var selectedDatabase = database ?? (useProfileDatabase ? profile.DefaultDatabase : null);
        if ( !string.IsNullOrWhiteSpace(selectedDatabase) )
            builder.Database = selectedDatabase;
        return new MySqlConnection(builder.ConnectionString);
    }

    private static async Task<string?> ReadLowerCaseTableNamesAsync(MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("SELECT @@lower_case_table_names", connection)
        { CommandTimeout = CommandTimeoutSeconds };
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<DatabaseInfo> ReadDatabaseInfoAsync(MySqlConnection connection, string database,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME, DEFAULT_COLLATION_NAME
            FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @schema
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if ( !await reader.ReadAsync(cancellationToken) )
            throw new InvalidOperationException($"数据库“{database}”不存在或当前账号无权访问。");
        return new DatabaseInfo(reader.GetString(0), NullableString(reader, 1), NullableString(reader, 2));
    }

    private static async Task<TableProperty?> ReadTablePropertyAsync(MySqlConnection connection, string database,
        string table, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT ENGINE, ROW_FORMAT, TABLE_COLLATION, TABLE_COMMENT, AUTO_INCREMENT, CREATE_OPTIONS
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND TABLE_TYPE = 'BASE TABLE'
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if ( !await reader.ReadAsync(cancellationToken) )
            throw new InvalidOperationException($"表“{database}.{table}”已删除或当前账号无权读取。");
        var collation = NullableString(reader, 2);
        return new TableProperty(table, NullableString(reader, 0), NullableString(reader, 1),
            CharacterSetFromCollation(collation), collation, NullableString(reader, 3), NullableUInt64(reader, 4),
            NullableString(reader, 5));
    }

    private static async Task<IReadOnlyList<SchemaColumn>> ReadColumnsAsync(MySqlConnection connection,
        string database, string table, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE,
                   COLUMN_DEFAULT, EXTRA, CHARACTER_SET_NAME, COLLATION_NAME, COLUMN_COMMENT,
                   GENERATION_EXPRESSION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<SchemaColumn>();
        while ( await reader.ReadAsync(cancellationToken) )
        {
            var extra = NullableString(reader, 6);
            var defaultValue = reader.IsDBNull(5)
                ? new SchemaValue(SchemaValueKind.Missing)
                : new SchemaValue(extra?.Contains("DEFAULT_GENERATED", StringComparison.OrdinalIgnoreCase) == true
                    ? SchemaValueKind.Expression : SchemaValueKind.Literal, ValueString(reader, 5));
            columns.Add(new SchemaColumn(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                NullableString(reader, 3), string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                defaultValue, extra, NullableString(reader, 7), NullableString(reader, 8), NullableString(reader, 9),
                NullableString(reader, 10), extra?.Contains("INVISIBLE", StringComparison.OrdinalIgnoreCase) == true
                    ? false : true));
        }
        if ( columns.Count == 0 )
            throw new InvalidOperationException($"对象“{database}.{table}”没有可见字段或无法读取字段信息。");
        return columns;
    }

    private static async Task<IReadOnlyList<SchemaIndex>> ReadIndexesAsync(MySqlConnection connection,
        string database, string table, CancellationToken cancellationToken)
    {
        var indexes = new Dictionary<string, List<(int Sequence, string? Column, string? Expression,
            int? Prefix, string? Direction, bool Unique, string Type, bool? Visible, string? Parser, string? Comment)>>
            (StringComparer.OrdinalIgnoreCase);
        await using var command = new MySqlCommand($"SHOW INDEX FROM {QuoteIdentifier(database)}.{QuoteIdentifier(table)}",
            connection) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ordinals = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, value => value,
            StringComparer.OrdinalIgnoreCase);
        int? Ordinal(string name) => ordinals.TryGetValue(name, out var value) ? value : null;
        while ( await reader.ReadAsync(cancellationToken) )
        {
            var name = ValueString(reader, Ordinal("Key_name") ?? 2) ?? "";
            if ( !indexes.TryGetValue(name, out var parts) )
                indexes[name] = parts = [];
            var nonUnique = Convert.ToInt32(reader.GetValue(Ordinal("Non_unique") ?? 1), CultureInfo.InvariantCulture) != 0;
            var visible = Ordinal("Visible") is { } visibleOrdinal
                ? string.Equals(ValueString(reader, visibleOrdinal), "YES", StringComparison.OrdinalIgnoreCase)
                : (bool?) null;
            var indexCommentOrdinal = Ordinal("Index_comment") ?? Ordinal("Comment") ?? -1;
            parts.Add((Convert.ToInt32(reader.GetValue(Ordinal("Seq_in_index") ?? 3), CultureInfo.InvariantCulture),
                ValueString(reader, Ordinal("Column_name") ?? 4), ValueString(reader, Ordinal("Expression") ?? -1),
                NullableInt32(reader, Ordinal("Sub_part") ?? -1), ValueString(reader, Ordinal("Collation") ?? -1),
                !nonUnique, ValueString(reader, Ordinal("Index_type") ?? 10) ?? "UNKNOWN", visible,
                ValueString(reader, Ordinal("Parser") ?? -1), ValueString(reader, indexCommentOrdinal)));
        }

        return indexes.Select(pair =>
        {
            var parts = pair.Value.OrderBy(item => item.Sequence).ToArray();
            var first = parts[0];
            return new SchemaIndex(pair.Key, first.Unique, first.Type, first.Visible, first.Parser, first.Comment,
                parts.Select(item => new SchemaIndexPart(item.Sequence, item.Column, item.Expression, item.Prefix,
                    item.Direction)).ToArray());
        }).ToArray();
    }

    private static async Task<IReadOnlyList<SchemaForeignKey>> ReadForeignKeysAsync(MySqlConnection connection,
        string database, string table, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT k.CONSTRAINT_NAME, k.REFERENCED_TABLE_SCHEMA, k.REFERENCED_TABLE_NAME,
                   k.ORDINAL_POSITION, k.COLUMN_NAME, k.REFERENCED_COLUMN_NAME,
                   r.UPDATE_RULE, r.DELETE_RULE, r.MATCH_OPTION
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k
            JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS r
              ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA
             AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME
             AND r.TABLE_NAME = k.TABLE_NAME
            WHERE k.TABLE_SCHEMA = @schema AND k.TABLE_NAME = @table
              AND k.REFERENCED_TABLE_NAME IS NOT NULL
            ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var grouped = new Dictionary<string, List<SchemaForeignKeyPart>>(StringComparer.OrdinalIgnoreCase);
        var metadata = new Dictionary<string, (string Schema, string Table, string Update, string Delete, string Match)>(
            StringComparer.OrdinalIgnoreCase);
        while ( await reader.ReadAsync(cancellationToken) )
        {
            var name = reader.GetString(0);
            if ( !grouped.TryGetValue(name, out var parts) )
            {
                grouped[name] = parts = [];
                metadata[name] = (reader.GetString(1), reader.GetString(2), reader.GetString(6),
                    reader.GetString(7), reader.GetString(8));
            }
            parts.Add(new SchemaForeignKeyPart(reader.GetInt32(3), reader.GetString(4), reader.GetString(5)));
        }
        return grouped.Select(pair =>
        {
            var info = metadata[pair.Key];
            return new SchemaForeignKey(pair.Key, info.Schema, info.Table, info.Update, info.Delete, info.Match,
                pair.Value.OrderBy(part => part.Sequence).ToArray());
        }).ToArray();
    }

    private static async Task<IReadOnlyList<SchemaCheckConstraint>> ReadCheckConstraintsAsync(MySqlConnection connection,
        string database, string table, string serverVersion, List<string> warnings,
        CancellationToken cancellationToken)
    {
        if ( !SupportsCheckConstraints(serverVersion) )
        {
            warnings.Add($"MySQL {serverVersion} does not expose enforced CHECK metadata; the check-constraint category may be incomplete.");
            return [];
        }
        await using var command = new MySqlCommand("""
            SELECT tc.CONSTRAINT_NAME, cc.CHECK_CLAUSE, cc.ENFORCED
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.CHECK_CONSTRAINTS cc
              ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA
             AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
            WHERE tc.TABLE_SCHEMA = @schema AND tc.TABLE_NAME = @table
              AND tc.CONSTRAINT_TYPE = 'CHECK'
            ORDER BY tc.CONSTRAINT_NAME
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SchemaCheckConstraint>();
        while ( await reader.ReadAsync(cancellationToken) )
            results.Add(new SchemaCheckConstraint(reader.GetString(0), reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
        return results;
    }

    private static async Task<IReadOnlyList<SchemaPartition>> ReadPartitionsAsync(MySqlConnection connection,
        string database, string table, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT PARTITION_ORDINAL_POSITION, PARTITION_NAME, PARTITION_METHOD,
                   PARTITION_EXPRESSION, PARTITION_DESCRIPTION, TABLE_ROWS
            FROM INFORMATION_SCHEMA.PARTITIONS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND PARTITION_NAME IS NOT NULL
            ORDER BY PARTITION_ORDINAL_POSITION
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<SchemaPartition>();
        while ( await reader.ReadAsync(cancellationToken) )
            results.Add(new SchemaPartition(NullableInt32(reader, 0) ?? 0, NullableString(reader, 1),
                NullableString(reader, 2), NullableString(reader, 3), NullableString(reader, 4), NullableUInt64(reader, 5)));
        return results;
    }

    private static async Task<SchemaViewProperty> ReadViewAsync(MySqlConnection connection,
        string database, string view, CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand("""
            SELECT ALGORITHM, SECURITY_TYPE, DEFINER, CHECK_OPTION, VIEW_DEFINITION, IS_UPDATABLE
            FROM INFORMATION_SCHEMA.VIEWS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @view
            """, connection) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@view", view);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if ( !await reader.ReadAsync(cancellationToken) )
            throw new InvalidOperationException($"视图“{database}.{view}”已删除或当前账号无权读取。");
        return new SchemaViewProperty(NullableString(reader, 0) ?? "UNDEFINED",
            NullableString(reader, 1) ?? "DEFINER", NullableString(reader, 2),
            NullableString(reader, 3) ?? "NONE", NullableString(reader, 4),
            string.Equals(NullableString(reader, 5), "YES", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadCreateStatementAsync(MySqlConnection connection, string database,
        DatabaseObjectInfo item, CancellationToken cancellationToken)
    {
        var keyword = item.Kind == SchemaObjectKind.View ? "VIEW" : "TABLE";
        await using var command = new MySqlCommand(
            $"SHOW CREATE {keyword} {QuoteIdentifier(database)}.{QuoteIdentifier(item.Name)}", connection)
        { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) && reader.FieldCount > 1
            ? NullableString(reader, 1)
            : null;
    }

    private static string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static string? CharacterSetFromCollation(string? collation)
    {
        if ( string.IsNullOrWhiteSpace(collation) )
            return null;
        var separator = collation.IndexOf('_');
        return separator > 0 ? collation[..separator] : collation;
    }

    private static string DescribeConnectionError(MySqlException error) => error.Number switch
    {
        1045 => "登录认证失败，请检查用户名和密码。",
        1049 => "默认数据库不存在或当前用户无权访问。",
        1042 or 2002 or 2003 => "无法连接到 MySQL 主机，请检查主机地址、端口和网络。",
        1044 or 1142 or 1143 => "当前账号缺少访问数据库或读取结构所需的权限。",
        0 when error.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "连接超时，请检查主机和网络。",
        _ => $"MySQL 连接失败（错误 {error.Number}）：{error.Message}"
    };

    private static bool SupportsCheckConstraints(string version)
    {
        var versionPart = version.Split('-', '+')[0];
        return Version.TryParse(versionPart, out var parsed) && parsed >= new Version(8, 0, 16);
    }

    private static string? NullableString(IDataRecord record, int ordinal) =>
        ordinal < 0 || record.IsDBNull(ordinal) ? null : Convert.ToString(record.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? ValueString(IDataRecord record, int ordinal) => NullableString(record, ordinal);

    private static int? NullableInt32(IDataRecord record, int ordinal) =>
        ordinal < 0 || record.IsDBNull(ordinal) ? null : Convert.ToInt32(record.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static ulong? NullableUInt64(IDataRecord record, int ordinal) =>
        ordinal < 0 || record.IsDBNull(ordinal) ? null : Convert.ToUInt64(record.GetValue(ordinal), CultureInfo.InvariantCulture);
}
