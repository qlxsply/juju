using System.Text;

namespace Toto.App.Data;

internal static class CsvFile
{
    public static List<List<string>> Read(string path)
    {
        return !File.Exists(path) ? [] : Parse(File.ReadAllText(path, new UTF8Encoding(true)));
    }

    public static void WriteAtomically(string path, IReadOnlyList<string> header,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
        {
            writer.WriteLine(Join(header));
            foreach (var row in rows) writer.WriteLine(Join(row));
        }

        File.Move(temporary, path, true);
    }

    private static string Join(IEnumerable<string> fields) => string.Join(',', fields.Select(Escape));

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

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