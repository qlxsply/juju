using Juju.Core.Errors;

namespace Juju.Tools.Json.Storage;

public static class JsonDocumentName
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    public static string Normalize(string name)
    {
        var value = name.Trim();
        if (!value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) value += ".json";
        if (value.Length == 5 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal) || value.Contains('/') || value.Contains('\\') || Reserved.Contains(Path.GetFileNameWithoutExtension(value)))
            throw new JujuException(ErrorCode.InvalidDocumentName, "The document name is not a valid JSON file name.");
        return value;
    }

    public static string DisplayName(string fileName) => Path.GetFileNameWithoutExtension(fileName);
}
