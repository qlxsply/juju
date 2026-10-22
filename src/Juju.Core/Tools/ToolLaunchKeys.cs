namespace Juju.Core.Tools;

/// <summary>工具入口的默认单键；常量 switch 的重复分支在编译时产生 CS8510，而非等待运行。</summary>
public static class ToolLaunchKeys
{
    public const char Json = 'J';
    public const char Toto = 'T';
    public const char EchoHttp = 'E';
    public const char Kairos = 'K';
    // 编译器检查各常量分支的可达性。把两个默认键改成同一个字符会导致编译失败。
    private static int DefaultKeyIndex(char key) => key switch
    {
        Json => 1, Toto => 2, EchoHttp => 3, Kairos => 4, _ => 0
    };

    public static Dictionary<string, string> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [ToolId.Json.ToString()] = Json.ToString(),
        [ToolId.Toto.ToString()] = Toto.ToString(),
        [ToolId.EchoHttp.ToString()] = EchoHttp.ToString(),
        [ToolId.Kairos.ToString()] = Kairos.ToString()
    };

    /// <summary>补齐旧配置并验证工具入口单键不重复。</summary>
    public static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? configured)
    {
        var values = Defaults();
        if (configured is not null)
            foreach (var key in values.Keys.ToArray())
                if (configured.TryGetValue(key, out var assigned)) values[key] = assigned.Trim().ToUpperInvariant();
        if (values.Values.Any(value => value.Length != 1 || !char.IsLetterOrDigit(value[0])) ||
            values.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidOperationException("各工具的单键必须是不重复的字母或数字。");
        return values;
    }
}
