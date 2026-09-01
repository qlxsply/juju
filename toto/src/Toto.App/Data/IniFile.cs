using System.Text;

namespace Toto.App.Data;

internal sealed class IniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);
    public static IniFile Load(string path)
    {
        var result = new IniFile(); if (!File.Exists(path)) return result;
        var bytes = File.ReadAllBytes(path); var encoding = bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? Encoding.Unicode : new UTF8Encoding(false);
        var section = "General";
        foreach (var raw in encoding.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim(); if (line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var split = line.IndexOf('='); if (split >= 0) result.Set(section, line[..split].Trim(), line[(split + 1)..].Trim());
        }
        return result;
    }
    public string? Get(string section, string key) => sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;
    public IEnumerable<string> SectionNames => sections.Keys;
    public IReadOnlyDictionary<string, string> GetSection(string section) => sections.TryGetValue(section, out var values) ? values : new Dictionary<string, string>();
    public void Set(string section, string key, string value) { if (!sections.TryGetValue(section, out var values)) sections[section] = values = new(StringComparer.OrdinalIgnoreCase); values[key] = value; }
    public void Remove(string section, string key) { if (sections.TryGetValue(section, out var values)) values.Remove(key); }
    public void RemoveSection(string section) => sections.Remove(section);
    public void SaveAtomically(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, false, Encoding.Unicode)) foreach (var (section, values) in sections.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) { writer.WriteLine($"[{section}]"); foreach (var (key, value) in values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) writer.WriteLine($"{key}={value}"); writer.WriteLine(); }
        File.Move(temporary, path, true);
    }
}
