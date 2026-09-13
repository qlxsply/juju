using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Juju.Core.Storage;

namespace Juju.Tools.Json.Settings;

public enum JsonToolCommand
{
    New,
    Save,
    Format,
    CopyText,
    CopyMinified,
    CopyFile,
    FoldAll,
    UnfoldAll,
    UnfoldLevel,
    EnterDiff,
    ExitDiff,
    Rename,
    Delete
}

public enum JsonToolShortcutSource
{
    Juju,
    Monaco
}

public enum JsonToolShortcutContext
{
    Editor,
    List,
    Diff
}

public sealed record JsonToolCommandMetadata(
    JsonToolCommand Command,
    string DisplayName,
    JsonToolShortcutSource Source,
    JsonToolShortcutContext Context,
    string Default);

public static class JsonToolShortcuts
{
    private static readonly HashSet<string> Keys =
    [
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString()),
        .. Enumerable.Range(0, 10).Select(value => value.ToString()),
        .. Enumerable.Range(1, 24).Select(value => $"F{value}"),
        "Escape", "Delete", "Backspace", "Enter", "Space", "Tab", "Up", "Down", "Left", "Right", "Home", "End",
        "PageUp", "PageDown", "Insert",
    ];

    private static readonly IReadOnlyDictionary<string, string> KeyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = "Escape", ["Del"] = "Delete", ["Return"] = "Enter", ["PgUp"] = "PageUp", ["PgDn"] = "PageDown",
        };

    public static IReadOnlyList<JsonToolCommandMetadata> Definitions { get; } =
    [
        new(JsonToolCommand.Save, "保存", JsonToolShortcutSource.Monaco, JsonToolShortcutContext.Editor, "Ctrl+S"),
        new(JsonToolCommand.Format, "格式化", JsonToolShortcutSource.Monaco, JsonToolShortcutContext.Editor,
            "Shift+Alt+F"),
        new(JsonToolCommand.FoldAll, "折叠全部", JsonToolShortcutSource.Monaco, JsonToolShortcutContext.Editor,
            "Ctrl+K Ctrl+0"),
        new(JsonToolCommand.UnfoldAll, "展开全部", JsonToolShortcutSource.Monaco, JsonToolShortcutContext.Editor,
            "Ctrl+K Ctrl+J"),
        new(JsonToolCommand.New, "新建", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Editor, "Ctrl+Alt+N"),
        new(JsonToolCommand.CopyText, "复制文本", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Editor,
            "Ctrl+Alt+C"),
        new(JsonToolCommand.CopyMinified, "复制压缩文本", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Editor,
            "Ctrl+Alt+M"),
        new(JsonToolCommand.CopyFile, "复制文件", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Editor,
            "Ctrl+Alt+Shift+C"),
        new(JsonToolCommand.EnterDiff, "进入对比", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Editor,
            "Ctrl+Alt+D"),
        new(JsonToolCommand.ExitDiff, "退出对比", JsonToolShortcutSource.Juju, JsonToolShortcutContext.Diff, "Escape"),
        new(JsonToolCommand.Rename, "重命名", JsonToolShortcutSource.Juju, JsonToolShortcutContext.List, "F2"),
        new(JsonToolCommand.Delete, "删除", JsonToolShortcutSource.Juju, JsonToolShortcutContext.List, "Delete"),
    ];

    public static IReadOnlyList<JsonToolCommand> Commands { get; } =
        Definitions.Select(definition => definition.Command).ToArray();

    public static IReadOnlyDictionary<JsonToolCommand, string> Defaults { get; } =
        Definitions.ToDictionary(definition => definition.Command, definition => definition.Default);

    public static IReadOnlyDictionary<JsonToolCommand, string> Normalize(
        IReadOnlyDictionary<JsonToolCommand, string>? shortcuts)
    {
        var normalized = Commands.ToDictionary(command => command,
            command => shortcuts is not null && shortcuts.TryGetValue(command, out var shortcut)
                ? NormalizeCombination(shortcut)
                : Defaults[command]);
        Validate(normalized);
        return normalized;
    }

    public static void Validate(IReadOnlyDictionary<JsonToolCommand, string>? shortcuts)
    {
        if (shortcuts is null || Commands.Any(command => !shortcuts.ContainsKey(command)))
            throw new InvalidOperationException("每个 JSON 命令都必须配置快捷键。");
        var normalized = Commands.ToDictionary(command => command, command => NormalizeCombination(shortcuts[command]));
        if (normalized.Values.Any(IsReserved)) throw new InvalidOperationException("JSON 快捷键不能使用系统或启动器保留组合键。");
        foreach (var context in Definitions.GroupBy(definition => definition.Context))
        {
            var bindings = context.Select(definition => normalized[definition.Command]).ToArray();
            if (bindings.Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Length)
                throw new InvalidOperationException("同一上下文中的 JSON 快捷键不能重复。");
        }
    }

    public static JsonToolCommandMetadata GetDefinition(JsonToolCommand command) =>
        Definitions.Single(definition => definition.Command == command);

    private static string NormalizeCombination(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) throw new InvalidOperationException("JSON 快捷键不能为空。");
        var strokes = Regex.Split(Regex.Replace(shortcut.Trim(), @"\s*\+\s*", "+"), @"\s+");
        if (strokes.Length is < 1 or > 2) throw new InvalidOperationException("JSON 快捷键最多支持两个按键序列。");
        return string.Join(" ", strokes.Select(NormalizeStroke));
    }

    private static string NormalizeStroke(string stroke)
    {
        var parts = stroke.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(string.IsNullOrEmpty)) throw new InvalidOperationException("JSON 快捷键格式无效。");
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? key = null;
        foreach (var part in parts)
        {
            if (part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers.Add("Ctrl");
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers.Add(part);
            else if (key is null) key = NormalizeKey(part);
            else throw new InvalidOperationException("每个 JSON 快捷键步骤只能包含一个按键。");
        }

        if (key is null || modifiers.Count != parts.Length - 1) throw new InvalidOperationException("JSON 快捷键格式无效。");
        return string.Join('+', new[] { "Ctrl", "Alt", "Shift" }.Where(modifiers.Contains).Append(key));
    }

    private static string NormalizeKey(string value)
    {
        var key = KeyAliases.TryGetValue(value, out var alias) ? alias :
            value.Length == 1 ? value.ToUpperInvariant() : value;
        if (!Keys.Contains(key)) throw new InvalidOperationException($"不支持的 JSON 快捷键按键: {value}。");
        return key;
    }

    private static bool IsReserved(string shortcut) =>
        shortcut is "Alt+F4" or "Ctrl+Alt+Delete" or "Ctrl+Alt+Shift+Space";
}

public sealed record JsonToolSettings(int AutosaveDelayMilliseconds = 800, int IndentSize = 2)
{
    public IReadOnlyDictionary<JsonToolCommand, string> Shortcuts { get; init; } = JsonToolShortcuts.Defaults;
}

public interface IJsonToolSettingsService
{
    JsonToolSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(JsonToolSettings settings, CancellationToken cancellationToken = default);
}

public sealed class JsonToolSettingsService(IAtomicFileWriter writer) : IJsonToolSettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path = Path.Combine(ApplicationPaths.ToolDataDirectory("json"), "settings.json");
    public JsonToolSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            await SaveAsync(Current, cancellationToken);
            return;
        }

        Current = Normalize(
            JsonSerializer.Deserialize<JsonToolSettings>(await File.ReadAllTextAsync(_path, cancellationToken),
                Options) ?? new());
    }

    public async Task SaveAsync(JsonToolSettings settings, CancellationToken cancellationToken = default)
    {
        Current = Normalize(settings);
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(Current, Options), cancellationToken);
    }

    private static JsonToolSettings Normalize(JsonToolSettings settings) => settings with
    {
        Shortcuts = JsonToolShortcuts.Normalize(settings.Shortcuts)
    };
}