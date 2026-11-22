using System.Text.Json;
using Juju.Tools.MySqlSchema.Domain;

namespace Juju.Tools.MySqlSchema.Application;

public static class SchemaComparisonService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SchemaComparisonResult Compare(SchemaObject a, SchemaObject b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if ( a.Kind != b.Kind )
            throw new InvalidOperationException("只能比较相同类型的数据库对象（表与表、视图与视图）。");

        var differences = new List<ObjectDifference>();
        AddSingle("数据库", "database", a.Database, b.Database, differences);
        AddRows("字段", a.Columns, b.Columns, item => item.Name, differences);
        AddRows("主键", a.Indexes.Where(index => index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)),
            b.Indexes.Where(index => index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)),
            item => item.Name, differences);
        AddRows("索引", a.Indexes.Where(index => !index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)),
            b.Indexes.Where(index => !index.Name.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)),
            item => item.Name, differences);
        AddRows("外键", a.ForeignKeys, b.ForeignKeys, item => item.Name, differences);
        AddRows("检查约束", a.CheckConstraints, b.CheckConstraints, item => item.Name, differences);
        AddRows("分区", a.Partitions, b.Partitions,
            item => item.Name ?? item.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), differences);
        AddSingle("选项", a.Table?.Name ?? b.Table?.Name ?? a.Name,
            a.Kind == SchemaObjectKind.Table ? a.Table : null,
            b.Kind == SchemaObjectKind.Table ? b.Table : null, differences);
        AddSingle("视图", a.Name,
            a.Kind == SchemaObjectKind.View ? a.View : null,
            b.Kind == SchemaObjectKind.View ? b.View : null, differences);
        return new SchemaComparisonResult(a, b, differences);
    }

    private static void AddSingle<T>(string category, string key, T? a, T? b,
        ICollection<ObjectDifference> output) where T : class
    {
        if ( a is null && b is null )
            return;
        output.Add(CreateDifference(category, key, a, b));
    }

    private static void AddRows<T>(string category, IEnumerable<T> a, IEnumerable<T> b,
        Func<T, string> key, ICollection<ObjectDifference> output) where T : class
    {
        var left = a.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        var right = b.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        foreach ( var name in left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase) )
        {
            left.TryGetValue(name, out var leftItem);
            right.TryGetValue(name, out var rightItem);
            output.Add(CreateDifference(category, name, leftItem, rightItem));
        }
    }

    private static ObjectDifference CreateDifference<T>(string category, string key, T? a, T? b)
        where T : class
    {
        if ( a is null || b is null )
        {
            var present = a ?? b!;
            var missingProperties = ToProperties(present).Select(pair => new PropertyDifference(pair.Key,
                a is null ? null : pair.Value, b is null ? null : pair.Value, Changed: true)).ToArray();
            return new ObjectDifference(category, key,
                a is null ? SchemaDifferenceKind.MissingInA : SchemaDifferenceKind.MissingInB, missingProperties);
        }

        var left = ToProperties(a);
        var right = ToProperties(b);
        var names = left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var properties = names.Select(name =>
        {
            left.TryGetValue(name, out var leftValue);
            right.TryGetValue(name, out var rightValue);
            return new PropertyDifference(name, leftValue, rightValue,
                !string.Equals(leftValue, rightValue, StringComparison.Ordinal));
        }).ToArray();
        return new ObjectDifference(category, key,
            properties.Any(property => property.Changed) ? SchemaDifferenceKind.Modified : SchemaDifferenceKind.Same,
            properties);
    }

    private static SortedDictionary<string, string?> ToProperties<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        var result = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach ( var property in document.RootElement.EnumerateObject() )
            result[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                ? null
                : property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.GetRawText();
        return result;
    }
}
