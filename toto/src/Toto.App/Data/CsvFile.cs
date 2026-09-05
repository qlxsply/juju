using System.Text;

namespace Toto.App.Data;

/// <summary>读写应用使用的简化 CSV 格式，并以临时文件替换方式保存。</summary>
internal static class CsvFile
{
    /// <summary>读取 CSV 行；文件不存在时返回空列表。</summary>
    public static List<List<string>> Read(string path)
    {
        // [] 是 C# 集合表达式，根据返回类型推断为新的 List<List<string>>。
        return !File.Exists(path) ? [] : Parse(File.ReadAllText(path, new UTF8Encoding(true)));
    }

    /// <summary>写入表头和数据行，然后原子替换目标 CSV 文件。</summary>
    public static void WriteAtomically(string path, IReadOnlyList<string> header,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        // using 块确保发生异常时也会释放文件句柄，等价于 Java 的 try-with-resources。
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
        {
            writer.WriteLine(Join(header));
            foreach (var row in rows) writer.WriteLine(Join(row));
        }

        File.Move(temporary, path, true);
    }

    /// <summary>将字段按 CSV 规则连接为一行。</summary>
    private static string Join(IEnumerable<string> fields) => string.Join(',', fields.Select(Escape));

    /// <summary>按 CSV 转义规则处理一个字段。</summary>
    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <summary>逐字符解析 CSV 文本，保留引号内的逗号和换行。</summary>
    private static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            switch (current)
            {
                case '"' when quoted && i + 1 < text.Length && text[i + 1] == '"':
                    field.Append(current);
                    i++;
                    break;
                case '"':
                    quoted = !quoted;
                    break;
                case ',' when !quoted:
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' or '\n' when !quoted:
                {
                    if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                }
                default:
                    field.Append(current);
                    break;
            }
        }

        if (field.Length <= 0 && row.Count <= 0) return rows;
        row.Add(field.ToString());
        rows.Add(row);

        return rows;
    }
}