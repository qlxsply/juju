namespace Juju.Tools.Toto.Application.Settings;

public static class WindowShortcut
{
    private static readonly IReadOnlyDictionary<string, string> Keys = CreateKeyNames();

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

        if (!Keys.TryGetValue(parts[^1], out var keyName)) return false;
        normalized = string.Join('+', new[] { "Ctrl", "Alt", "Shift", "Win" }.Where(modifiers.Contains).Append(keyName));
        return true;
    }

    private static IReadOnlyDictionary<string, string> CreateKeyNames()
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var letter in Enumerable.Range('A', 26).Select(value => (char)value))
            keys[letter.ToString()] = letter.ToString();
        foreach (var digit in Enumerable.Range(0, 10))
        {
            keys[digit.ToString()] = digit.ToString();
            keys[$"D{digit}"] = digit.ToString();
            keys[$"NumPad{digit}"] = $"NumPad{digit}";
        }
        for (var function = 1; function <= 24; function++) keys[$"F{function}"] = $"F{function}";

        foreach (var key in new[]
                 {
                     "Cancel", "Tab", "LineFeed", "Clear", "Pause", "Capital", "KanaMode", "JunjaMode",
                     "FinalMode", "HanjaMode", "KanjiMode", "Escape", "ImeConvert", "ImeNonConvert",
                     "ImeAccept", "ImeModeChange", "Space", "End", "Home", "Left", "Up", "Right", "Down",
                     "Select", "Print", "Execute", "Snapshot", "Insert", "Delete", "Help", "Apps", "Sleep",
                     "Multiply", "Add", "Separator", "Subtract", "Decimal", "Divide", "NumLock", "Scroll",
                     "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserStop", "BrowserSearch",
                     "BrowserFavorites", "BrowserHome", "VolumeMute", "VolumeDown", "VolumeUp", "MediaNextTrack",
                     "MediaPreviousTrack", "MediaStop", "MediaPlayPause", "LaunchMail", "SelectMedia",
                     "LaunchApplication1", "LaunchApplication2", "OemSemicolon", "OemPlus", "OemComma",
                     "OemMinus", "OemPeriod", "OemOpenBrackets", "OemPipe", "OemCloseBrackets", "OemQuotes",
                     "Oem8", "OemBackslash", "Oem102", "OemClear", "ImeProcessed", "Packet", "Attn", "CrSel",
                     "ExSel", "EraseEof", "Play", "Zoom", "NoName", "Pa1"
                 })
            keys[key] = key;

        keys["Back"] = "Backspace";
        keys["Backspace"] = "Backspace";
        keys["Return"] = "Enter";
        keys["Enter"] = "Enter";
        keys["Prior"] = "PageUp";
        keys["PageUp"] = "PageUp";
        keys["Next"] = "PageDown";
        keys["PageDown"] = "PageDown";
        return keys;
    }
}
