using Microsoft.Data.Sqlite;

namespace Toto.App.Data;

internal sealed class SettingsRepository(TotoDatabase database)
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["hotkey"] = "Ctrl+Alt+Space", ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q",
        ["shortcut_settings"] = "Alt+S", ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D",
        ["shortcut_edit"] = "Alt+E", ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C",
        ["default_remind_minutes"] = "5", ["auto_start"] = "0", ["work_start_popup_enabled"] = "0",
        ["work_end_popup_enabled"] = "0", ["work_start_time"] = "09:00", ["work_end_time"] = "18:00"
    };

    public IReadOnlyDictionary<string, string> Load()
    {
        var settings = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM app_settings";
        using var reader = command.ExecuteReader();
        while (reader.Read()) settings[reader.GetString(0)] = reader.GetString(1);
        return settings;
    }

    public void Save(IReadOnlyDictionary<string, string> settings)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var (key, defaultValue) in Defaults)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO app_settings(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", settings.TryGetValue(key, out var value) ? value : defaultValue);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
