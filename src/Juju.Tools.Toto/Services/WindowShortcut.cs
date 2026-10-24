using System.Windows.Input;

namespace Toto.App.Services;

internal static class WindowShortcut
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) return false;
        var modifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts[..^1])
        {
            var name = part.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : part;
            if (!new[] { "Ctrl", "Alt", "Shift", "Win" }.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
            if (!modifiers.Add(name)) return false;
        }

        var last = parts[^1];
        var token = last.Equals("Backspace", StringComparison.OrdinalIgnoreCase) ? "Back" :
            last.Equals("Return", StringComparison.OrdinalIgnoreCase) ? "Enter" :
            last.Equals("PageUp", StringComparison.OrdinalIgnoreCase) ? "Prior" :
            last.Equals("PageDown", StringComparison.OrdinalIgnoreCase) ? "Next" : last;
        Key key;
        if (token.Length == 1 && char.IsDigit(token[0]) && token[0] is >= '0' and <= '9')
            key = Key.D0 + (token[0] - '0');
        else if (!Enum.TryParse(token, ignoreCase: true, out key)) return false;
        if (key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return false;
        var keyName = key switch
        {
            >= Key.D0 and <= Key.D9 => key.ToString()[1..], Key.Back => "Backspace",
            Key.Prior => "PageUp", Key.Next => "PageDown", Key.Return => "Enter", _ => key.ToString()
        };
        normalized = string.Join('+', new[] { "Ctrl", "Alt", "Shift", "Win" }.Where(modifiers.Contains).Append(keyName));
        return true;
    }
}
