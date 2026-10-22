namespace Juju.Tools.Json.Documents;

public enum JsonDiffKind { Same, Added, Removed, Empty }

public sealed record JsonDiffLine(int? LeftNumber, string LeftText, JsonDiffKind LeftKind,
    int? RightNumber, string RightText, JsonDiffKind RightKind);

/// <summary>Aligns unchanged lines; insertions and deletions remain paired for side-by-side display.</summary>
public static class JsonLineDiff
{
    public static IReadOnlyList<JsonDiffLine> Compare(string left, string right)
    {
        var a = left.Replace("\r\n", "\n").Split('\n');
        var b = right.Replace("\r\n", "\n").Split('\n');
        var result = new List<JsonDiffLine>();
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            result.Add(new(prefix + 1, a[prefix], JsonDiffKind.Same, prefix + 1, b[prefix], JsonDiffKind.Same));
            prefix++;
        }
        var endA = a.Length;
        var endB = b.Length;
        while (endA > prefix && endB > prefix && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        var n = endA - prefix;
        var m = endB - prefix;
        // Avoid allocating a quadratic table for large documents; still mark all changed lines.
        if ((long)(n + 1) * (m + 1) > 2_000_000)
        {
            for (var k = 0; k < Math.Max(n, m); k++)
                result.Add(ChangeLine(a, b, prefix + k < endA ? prefix + k : null,
                    prefix + k < endB ? prefix + k : null));
        }
        else
        {
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[prefix + i] == b[prefix + j] ? lcs[i + 1, j + 1] + 1 :
                        Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var x = 0;
            var y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[prefix + x] == b[prefix + y])
                {
                    result.Add(new(prefix + x + 1, a[prefix + x], JsonDiffKind.Same,
                        prefix + y + 1, b[prefix + y], JsonDiffKind.Same));
                    x++;
                    y++;
                    continue;
                }
                var removed = new List<int>();
                var added = new List<int>();
                while (x < n || y < m)
                {
                    if (x < n && y < m && a[prefix + x] == b[prefix + y]) break;
                    if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) removed.Add(prefix + x++);
                    else added.Add(prefix + y++);
                }
                for (var k = 0; k < Math.Max(removed.Count, added.Count); k++)
                    result.Add(ChangeLine(a, b, k < removed.Count ? removed[k] : null,
                        k < added.Count ? added[k] : null));
            }
        }

        for (var i = endA; i < a.Length; i++)
            result.Add(new(i + 1, a[i], JsonDiffKind.Same, endB + i - endA + 1,
                b[endB + i - endA], JsonDiffKind.Same));
        return result;
    }

    private static JsonDiffLine ChangeLine(string[] a, string[] b, int? i, int? j) => new(
        i + 1, i is { } left ? a[left] : "", i is null ? JsonDiffKind.Empty : JsonDiffKind.Removed,
        j + 1, j is { } right ? b[right] : "", j is null ? JsonDiffKind.Empty : JsonDiffKind.Added);
}
