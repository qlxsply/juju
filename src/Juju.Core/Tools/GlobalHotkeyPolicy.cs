namespace Juju.Core.Tools;

/// <summary>统一比较工具箱入口和工具专属热键；修饰键顺序、大小写不会掩盖冲突。</summary>
public static class GlobalHotkeyPolicy
{
    public const string None = "None";

    public static string Normalize(string? value, bool optional = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals(None, StringComparison.OrdinalIgnoreCase))
        {
            if (optional) return None;
            throw new InvalidOperationException("工具箱入口必须配置全局快捷键。");
        }
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty) || parts.Length < 2)
            throw new InvalidOperationException("全局快捷键需要修饰键和一个按键，例如 Ctrl+Alt+Space。");
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts[..^1])
        {
            var modifier = part.Equals("Control", StringComparison.OrdinalIgnoreCase) ||
                           part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Ctrl" :
                part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ? "Alt" :
                part.Equals("Shift", StringComparison.OrdinalIgnoreCase) ? "Shift" :
                part.Equals("Win", StringComparison.OrdinalIgnoreCase) ? "Win" : null;
            if (modifier is null || !modifiers.Add(modifier))
                throw new InvalidOperationException("全局快捷键包含无效或重复修饰键。");
        }
        var key = parts[^1].Equals("Space", StringComparison.OrdinalIgnoreCase) ? "Space" :
            parts[^1].ToUpperInvariant();
        var namedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Space", "Escape", "Tab", "Enter", "Back", "Backspace", "Delete", "Insert", "Home", "End",
            "PageUp", "PageDown", "Up", "Down", "Left", "Right", "Oemplus", "OemMinus", "Oemcomma",
            "OemPeriod", "OemQuestion", "Oemtilde", "OemOpenBrackets", "OemCloseBrackets", "OemPipe",
            "OemQuotes", "OemSemicolon", "OemBackslash", "Add", "Subtract", "Multiply", "Divide", "Decimal"
        };
        if (!(key.Length == 1 && ((key[0] is >= 'A' and <= 'Z') || (key[0] is >= '0' and <= '9'))) &&
            !namedKeys.Contains(key) && !(key.StartsWith('F') && int.TryParse(key[1..], out var n) && n is >= 1 and <= 24))
            throw new InvalidOperationException("全局快捷键按键仅支持字母、数字、Space 或 F1-F24。");
        var normalized = string.Join('+', new[] { "Ctrl", "Alt", "Shift", "Win" }.Where(modifiers.Contains).Append(key));
        if (normalized is "Alt+F4" or "Ctrl+Alt+Delete")
            throw new InvalidOperationException("不能占用 Windows 系统快捷键。");
        return normalized;
    }

    public static void EnsureDistinct(string launcher, params string?[] tools)
    {
        var entries = new[] { Normalize(launcher) }.Concat(tools.Select(value => Normalize(value, optional: true)))
            .Where(value => value != None).ToArray();
        if (entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new InvalidOperationException("工具箱入口与各工具的全局快捷键不能冲突。");
    }
}
