using Toto.App.Domain;

namespace Toto.App.Data;

internal sealed class SettingsRepository
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["hotkey"] = "Ctrl+Alt+Space", ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q", ["shortcut_settings"] = "Alt+S", ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D", ["shortcut_edit"] = "Alt+E", ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C", ["default_remind_minutes"] = "5", ["auto_start"] = "0", ["work_start_popup_enabled"] = "0", ["work_end_popup_enabled"] = "0", ["work_start_time"] = "09:00", ["work_end_time"] = "18:00"
    };

    private readonly AppPaths paths;
    private readonly object gate = new();
    public SettingsRepository(AppPaths paths) => this.paths = paths;

    public IReadOnlyDictionary<string, string> Load()
    {
        lock (gate)
        {
            var result = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase); var ini = IniFile.Load(paths.ConfigPath);
            foreach (var (key, value) in ini.GetSection("General")) if (Defaults.ContainsKey(key)) result[key] = value;
            return result;
        }
    }
    public void EnsureExists() { lock (gate) if (!File.Exists(paths.ConfigPath)) SaveCore(Defaults); }
    public void Save(IReadOnlyDictionary<string, string> settings) { lock (gate) SaveCore(settings); }
    public bool WasScheduledPopupShown(DateOnly date, ScheduledPopupKind kind) { lock (gate) return IniFile.Load(paths.ConfigPath).Get("ScheduledPopups", PopupKey(date, kind)) is not null; }
    public bool TryMarkScheduledPopupShown(DateOnly date, ScheduledPopupKind kind, DateTime shownAt)
    {
        lock (gate)
        {
            var ini = IniFile.Load(paths.ConfigPath); var key = PopupKey(date, kind); if (ini.Get("ScheduledPopups", key) is not null) return false; ini.Set("ScheduledPopups", key, DateTimeText.Text(shownAt)); ini.SaveAtomically(paths.ConfigPath); return true;
        }
    }
    private void SaveCore(IReadOnlyDictionary<string, string> settings)
    {
        var ini = IniFile.Load(paths.ConfigPath); foreach (var (key, defaultValue) in Defaults) ini.Set("General", key, settings.TryGetValue(key, out var value) ? value : defaultValue); ini.SaveAtomically(paths.ConfigPath);
    }
    private static string PopupKey(DateOnly date, ScheduledPopupKind kind) => $"{date:yyyy-MM-dd}.{(kind == ScheduledPopupKind.WorkStart ? "work_start" : "work_end")}";
}
