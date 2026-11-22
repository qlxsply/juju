using System.Globalization;
using System.Text.RegularExpressions;
using Juju.Tools.MySqlSchema.Domain;

namespace Juju.Tools.MySqlSchema.Application;

public sealed record MigrationSqlDraft(string Direction, string Sql, IReadOnlyList<string> Warnings);

/// <summary>Generates review-only DDL for the explicitly supported table changes; it never executes SQL.</summary>
public static partial class MigrationSqlGenerator
{
    public static MigrationSqlDraft Generate(SchemaObject a, SchemaObject b, bool aToB)
    {
        if ( a.Kind != b.Kind )
            throw new InvalidOperationException("不能为表和视图之间的比较生成迁移语句。");

        var source = aToB ? a : b;
        var target = aToB ? b : a;
        var direction = aToB ? "A → B" : "B → A";
        var warnings = new List<string>
        {
            "此脚本仅供审阅，不会执行；执行前请备份目标数据库并在测试环境验证。"
        };
        var statements = new List<string>();

        if ( source.Kind == SchemaObjectKind.View )
        {
            if ( !string.Equals(source.View?.Definition, target.View?.Definition, StringComparison.Ordinal) )
                warnings.Add("视图定义的迁移需要人工检查 definer、SQL SECURITY、算法和权限，未自动生成 CREATE OR REPLACE VIEW。");
            return new MigrationSqlDraft(direction, Render(direction, statements, warnings), warnings);
        }

        var qualifiedTarget = Qualified(target.DatabaseName, target.Name);
        var sourceColumns = source.Columns.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var targetColumns = target.Columns.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach ( var column in source.Columns.OrderBy(item => item.Ordinal) )
        {
            if ( !targetColumns.TryGetValue(column.Name, out var old) )
            {
                statements.Add($"ALTER TABLE {qualifiedTarget} ADD COLUMN {FormatColumn(column)}{PositionClause(source.Columns, column)};");
                continue;
            }
            if ( !ColumnEquivalent(column, old) )
                statements.Add($"ALTER TABLE {qualifiedTarget} MODIFY COLUMN {FormatColumn(column)}{PositionClause(source.Columns, column)};");
        }
        foreach ( var old in target.Columns.Where(column => !sourceColumns.ContainsKey(column.Name)) )
            statements.Add($"ALTER TABLE {qualifiedTarget} DROP COLUMN {Quote(old.Name)};");

        AddIndexChanges(source, target, qualifiedTarget, statements, warnings);
        if ( !Equivalent(source.ForeignKeys, target.ForeignKeys) )
            warnings.Add("外键变化未自动生成；请检查引用库、字段顺序以及 ON UPDATE/ON DELETE 规则。");
        if ( !Equivalent(source.CheckConstraints, target.CheckConstraints) )
            warnings.Add("CHECK 约束变化未自动生成；目标 MySQL 版本可能不支持相同约束语法。");
        if ( !Equivalent(source.Partitions, target.Partitions) )
            warnings.Add("分区定义变化未自动生成；请人工制定分区重组方案。");
        if ( !Equivalent(source.Table, target.Table) )
            warnings.Add("表选项变化（如引擎、字符集、排序规则、注释或自增值）未自动生成。");
        if ( !string.Equals(source.Name, target.Name, StringComparison.Ordinal) )
            warnings.Add($"源对象名“{source.Name}”与目标对象名“{target.Name}”不同；脚本针对目标对象名生成。");

        return new MigrationSqlDraft(direction, Render(direction, statements, warnings), warnings);
    }

    private static void AddIndexChanges(SchemaObject source, SchemaObject target, string table,
        ICollection<string> statements, ICollection<string> warnings)
    {
        var sourceIndexes = source.Indexes.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var targetIndexes = target.Indexes.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        foreach ( var old in target.Indexes.Where(index => !sourceIndexes.ContainsKey(index.Name)) )
        {
            statements.Add(old.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                ? $"ALTER TABLE {table} DROP PRIMARY KEY;"
                : $"ALTER TABLE {table} DROP INDEX {Quote(old.Name)};");
        }
        foreach ( var index in source.Indexes )
        {
            if ( targetIndexes.TryGetValue(index.Name, out var old ) && Equivalent(index, old) )
                continue;
            if ( index.Parts.Count == 0 || index.Parts.Any(part => part.ColumnName is null) ||
                 index.Type is not ("BTREE" or "HASH") || index.Visible == false ||
                 !string.IsNullOrEmpty(index.Parser) || !string.IsNullOrEmpty(index.Comment) )
            {
                warnings.Add($"索引“{index.Name}”含未支持的类型、属性或表达式，未生成变更 SQL。");
                continue;
            }
            if ( old is not null )
                statements.Add(index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                    ? $"ALTER TABLE {table} DROP PRIMARY KEY;"
                    : $"ALTER TABLE {table} DROP INDEX {Quote(index.Name)};");
            var columns = string.Join(", ", index.Parts.OrderBy(part => part.Sequence).Select(part =>
                Quote(part.ColumnName!) + (part.PrefixLength is { } prefix ? $"({prefix})" : "") +
                (part.SortDirection == "D" ? " DESC" : part.SortDirection == "A" ? " ASC" : "")));
            if ( index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase) )
                statements.Add($"ALTER TABLE {table} ADD PRIMARY KEY ({columns});");
            else
                statements.Add($"ALTER TABLE {table} ADD {(index.Unique ? "UNIQUE " : "")}INDEX {Quote(index.Name)} ({columns}) USING {index.Type};");
        }
    }

    private static string FormatColumn(SchemaColumn column)
    {
        var type = column.ColumnType ?? column.DataType;
        var sql = $"{Quote(column.Name)} {type} {(column.Nullable ? "NULL" : "NOT NULL")}";
        sql += column.Default.Kind switch
        {
            SchemaValueKind.Null => " DEFAULT NULL",
            SchemaValueKind.Literal when column.Default.Value is not null => " DEFAULT " + FormatLiteral(column),
            SchemaValueKind.Expression when column.Default.Value is not null => " DEFAULT " + column.Default.Value,
            _ => ""
        };
        if ( !string.IsNullOrWhiteSpace(column.Extra) )
            sql += " " + column.Extra;
        if ( !string.IsNullOrWhiteSpace(column.CharacterSet) )
            sql += " CHARACTER SET " + Quote(column.CharacterSet);
        if ( !string.IsNullOrWhiteSpace(column.Collation) )
            sql += " COLLATE " + Quote(column.Collation);
        if ( !string.IsNullOrWhiteSpace(column.Comment) )
            sql += " COMMENT '" + column.Comment.Replace("'", "''", StringComparison.Ordinal) + "'";
        return sql;
    }

    private static string FormatLiteral(SchemaColumn column)
    {
        var value = column.Default.Value!;
        if ( NumericType().IsMatch(column.DataType) ||
             value.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase) )
            return value;
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string PositionClause(IReadOnlyList<SchemaColumn> columns, SchemaColumn column)
    {
        if ( column.Ordinal <= 1 )
            return " FIRST";
        var previous = columns.SingleOrDefault(item => item.Ordinal == column.Ordinal - 1);
        return previous is null ? "" : " AFTER " + Quote(previous.Name);
    }

    private static bool ColumnEquivalent(SchemaColumn a, SchemaColumn b) =>
        a.DataType == b.DataType && a.ColumnType == b.ColumnType && a.Nullable == b.Nullable &&
        a.Default == b.Default && a.Extra == b.Extra && a.CharacterSet == b.CharacterSet &&
        a.Collation == b.Collation && a.Comment == b.Comment &&
        a.GenerationExpression == b.GenerationExpression && a.Visible == b.Visible && a.Ordinal == b.Ordinal;

    private static bool Equivalent<T>(T? a, T? b) =>
        string.Equals(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b),
            StringComparison.Ordinal);

    private static string Render(string direction, IEnumerable<string> statements, IEnumerable<string> warnings)
    {
        var lines = new List<string> { $"-- MySQL Schema migration draft: {direction}", "-- Review before executing." };
        lines.AddRange(warnings.Select(warning => "-- WARNING: " + warning));
        lines.AddRange(statements);
        if ( statements is ICollection<string> collection && collection.Count == 0 )
            lines.Add("-- No automatically supported statements were generated.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Qualified(string database, string table) => Quote(database) + "." + Quote(table);
    private static string Quote(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    [GeneratedRegex("^(tinyint|smallint|mediumint|int|integer|bigint|decimal|numeric|float|double|real|bit|year)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumericType();
}
