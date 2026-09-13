using Juju.Core.Errors;

namespace Juju.Tools.Json.Storage;

/// <summary>JSON 文档文件名的规范化和安全验证工具，集中阻止路径穿越与 Windows 保留设备名。</summary>
public static class JsonDocumentName
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1",
        "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// 去除首尾空白、补齐扩展名并拒绝空名称、无效字符、目录分隔符和保留名称。
    /// 验证发生在路径组合前，是抵御 <c>..</c> 路径穿越的第一层防线。
    /// </summary>
    public static string Normalize(string name)
    {
        var value = name.Trim();
        if (!value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) value += ".json";
        if (value.Length == 5 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains("..", StringComparison.Ordinal) || value.Contains('/') || value.Contains('\\') ||
            Reserved.Contains(Path.GetFileNameWithoutExtension(value)))
            throw new JujuException(ErrorCode.InvalidDocumentName, "The document name is not a valid JSON file name.");
        return value;
    }

    /// <summary>返回供 UI 展示的无扩展名部分，不承担安全验证职责。</summary>
    public static string DisplayName(string fileName) => Path.GetFileNameWithoutExtension(fileName);
}