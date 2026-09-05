using System.Text;

namespace Toto.App.Data;

/// <summary>提供大小写不敏感的内存 INI 文档读写，并支持原子保存。</summary>
internal sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>从指定路径加载 INI 文件；文件不存在时返回空文档。</summary>
    public static IniFile Load(string path)
    {
        var result = new IniFile();
        if (!File.Exists(path)) return result;
        var bytes = File.ReadAllBytes(path);
        var encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode
            : new UTF8Encoding(false);
        var section = "General";
        // 集合表达式是 C# 的简写；编译器会按 Split 所需类型构造字符数组，而非 Java 的数组字面量语法。
        foreach (var raw in encoding.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var split = line.IndexOf('=');
            if (split >= 0) result.Set(section, line[..split].Trim(), line[(split + 1)..].Trim());
        }

        return result;
    }

    /// <summary>获取节中键的值；键或节不存在时返回空值。</summary>
    public string? Get(string section, string key) =>
        sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    /// <summary>获取当前文档中的所有节名。</summary>
    public IEnumerable<string> SectionNames => sections.Keys;

    /// <summary>获取节内键值对；节不存在时返回空字典。</summary>
    public IReadOnlyDictionary<string, string> GetSection(string section) =>
        sections.TryGetValue(section, out var values) ? values : new Dictionary<string, string>();

    /// <summary>创建或覆盖指定节中的键值。</summary>
    public void Set(string section, string key, string value)
    {
        if (!sections.TryGetValue(section, out var values))
            sections[section] = values = new(StringComparer.OrdinalIgnoreCase);
        values[key] = value;
    }

    /// <summary>从指定节移除键；不存在时不执行操作。</summary>
    public void Remove(string section, string key)
    {
        if (sections.TryGetValue(section, out var values)) values.Remove(key);
    }

    /// <summary>移除整个节；不存在时不执行操作。</summary>
    public void RemoveSection(string section) => sections.Remove(section);

    /// <summary>先写入临时文件，再替换目标文件，以降低写入中断造成文件损坏的风险。</summary>
    public void SaveAtomically(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        // using 块在离开作用域时调用 IDisposable.Dispose，类似 Java try-with-resources。
        using (var writer = new StreamWriter(temporary, false, Encoding.Unicode))
            foreach (var (section, values) in sections.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine($"[{section}]");
                foreach (var (key, value) in values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    writer.WriteLine($"{key}={value}");
                writer.WriteLine();
            }

        File.Move(temporary, path, true);
    }
}
