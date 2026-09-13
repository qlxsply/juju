using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Juju.Core.Storage;

namespace Juju.Tools.Json.Settings;

/// <summary>JSON 工具可绑定快捷键的命令标识；枚举避免配置和命令分发依赖易拼错的字符串。</summary>
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

/// <summary>快捷键由 Juju 宿主处理还是由 Monaco 编辑器处理。</summary>
public enum JsonToolShortcutSource
{
    Juju,
    Monaco
}

/// <summary>快捷键生效的 UI 上下文；不同上下文可合法复用同一组合键。</summary>
public enum JsonToolShortcutContext
{
    Editor,
    List,
    Diff
}

/// <summary>命令显示名、处理来源、作用域和默认组合键的不可变定义。</summary>
public sealed record JsonToolCommandMetadata(
    JsonToolCommand Command,
    string DisplayName,
    JsonToolShortcutSource Source,
    JsonToolShortcutContext Context,
    string Default);

/// <summary>
/// 集中维护快捷键定义、规范化和冲突检查。输入文本先转为唯一规范形式，再验证，
/// 使大小写和常见别名不会影响重复检测。
/// </summary>
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

    /// <summary>所有支持命令的权威定义；集合表达式创建初始只读列表。</summary>
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

    /// <summary>从定义派生的命令列表，避免维护第二份命令清单。</summary>
    public static IReadOnlyList<JsonToolCommand> Commands { get; } =
        Definitions.Select(definition => definition.Command).ToArray();

    /// <summary>按命令索引的默认快捷键映射。</summary>
    public static IReadOnlyDictionary<JsonToolCommand, string> Defaults { get; } =
        Definitions.ToDictionary(definition => definition.Command, definition => definition.Default);

    /// <summary>
    /// 以传入配置覆盖默认值，规范化所有组合键后执行完整校验；结果始终包含每个已知命令。
    /// 这是不可变式建立点，调用方无需逐项补齐缺失默认值。
    /// </summary>
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

    /// <summary>检查完整性、保留组合键和同一 UI 上下文内的重复绑定，失败时抛出说明性异常。</summary>
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

    /// <summary>取得命令的元数据；未知命令因 <c>Single</c> 的集合语义而失败。</summary>
    public static JsonToolCommandMetadata GetDefinition(JsonToolCommand command) =>
        Definitions.Single(definition => definition.Command == command);

    /// <summary>规范化一到两个按键步骤，并统一加号周围的空白。</summary>
    private static string NormalizeCombination(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut)) throw new InvalidOperationException("JSON 快捷键不能为空。");
        var strokes = Regex.Split(Regex.Replace(shortcut.Trim(), @"\s*\+\s*", "+"), @"\s+");
        if (strokes.Length is < 1 or > 2) throw new InvalidOperationException("JSON 快捷键最多支持两个按键序列。");
        return string.Join(" ", strokes.Select(NormalizeStroke));
    }

    /// <summary>规范化单个按键步骤，固定修饰键顺序为 Ctrl、Alt、Shift。</summary>
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

    /// <summary>展开常见别名、统一单字符大小写，并限制为显式允许的键集合。</summary>
    private static string NormalizeKey(string value)
    {
        var key = KeyAliases.TryGetValue(value, out var alias) ? alias :
            value.Length == 1 ? value.ToUpperInvariant() : value;
        if (!Keys.Contains(key)) throw new InvalidOperationException($"不支持的 JSON 快捷键按键: {value}。");
        return key;
    }

    /// <summary>标记会被系统或启动器保留、不能安全交给 JSON 工具的组合键。</summary>
    private static bool IsReserved(string shortcut) =>
        shortcut is "Alt+F4" or "Ctrl+Alt+Delete" or "Ctrl+Alt+Shift+Space";
}

/// <summary>
/// JSON 工具的持久化设置。record 主构造函数给出自动保存延迟和缩进默认值；<c>init</c> 属性仅能
/// 在构造或 <c>with</c> 表达式中设置，类似 Java 不可变配置对象的构建阶段。
/// </summary>
public sealed record JsonToolSettings(int AutosaveDelayMilliseconds = 800, int IndentSize = 2)
{
    /// <summary>每条命令的规范化快捷键；初始值使用全局默认映射。</summary>
    public IReadOnlyDictionary<JsonToolCommand, string> Shortcuts { get; init; } = JsonToolShortcuts.Defaults;
}

/// <summary>JSON 工具设置的当前快照与异步磁盘读写契约。</summary>
public interface IJsonToolSettingsService
{
    /// <summary>内存中的当前设置。</summary>
    JsonToolSettings Current { get; }

    /// <summary>从设置文件加载并规范化数据；首次运行创建默认文件。</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>规范化输入后以原子方式异步保存。</summary>
    Task SaveAsync(JsonToolSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>以 camelCase JSON 和字符串枚举持久化 JSON 工具设置的实现。</summary>
public sealed class JsonToolSettingsService(IAtomicFileWriter writer) : IJsonToolSettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path = Path.Combine(ApplicationPaths.ToolDataDirectory("json"), "settings.json");
    public JsonToolSettings Current { get; private set; } = new();

    /// <summary>异步加载设置并用 <see cref="JsonToolShortcuts.Normalize"/> 修复缺失或非规范快捷键。</summary>
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

    /// <summary>先建立规范化当前快照，再委托原子写入器保存完整 JSON。</summary>
    public async Task SaveAsync(JsonToolSettings settings, CancellationToken cancellationToken = default)
    {
        Current = Normalize(settings);
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(Current, Options), cancellationToken);
    }

    /// <summary>使用 record 的 <c>with</c> 复制语法仅替换快捷键属性，不修改调用方传入的实例。</summary>
    private static JsonToolSettings Normalize(JsonToolSettings settings) => settings with
    {
        Shortcuts = JsonToolShortcuts.Normalize(settings.Shortcuts)
    };
}