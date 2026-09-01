using System.Text;
using Microsoft.Data.Sqlite;
using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App.Data;

internal sealed class LegacyMigrationService(AppPaths paths, TotoDatabase database, ItemRepository items, AppLog log)
{
    public void MigrateIfNeeded()
    {
        using var connection = database.Open();
        if (GetValue(connection, "legacy_migration_completed") == "1") return;
        BackupLegacyFiles();
        var active = ReadCsv(paths.ActiveCsvPath, ItemStatus.Active);
        var history = ReadCsv(paths.HistoryCsvPath, null);
        var historyIds = history.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        active = active.Where(x => !historyIds.Contains(x.Id)).ToList();
        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var item in active.Concat(history)) items.Insert(connection, transaction, item);
            SaveLegacySettings(transaction);
            SetValue(connection, transaction, "legacy_migration_completed", "1");
            transaction.Commit();
            log.Write($"Legacy migration complete: active={active.Count}, history={history.Count}");
        }
        catch (Exception ex) { log.Write($"Legacy migration failed: {ex.Message}"); throw new InvalidOperationException("旧版数据迁移失败。CSV/INI 文件保持不变，请修复数据后重试。", ex); }
    }

    private void BackupLegacyFiles()
    {
        var sources = new[] { paths.ConfigPath, paths.ActiveCsvPath, paths.HistoryCsvPath }.Where(File.Exists).ToArray();
        if (sources.Length == 0) return;
        var directory = Path.Combine(paths.BackupDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss")); Directory.CreateDirectory(directory);
        foreach (var source in sources) File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), true);
    }

    private List<TodoItem> ReadCsv(string path, ItemStatus? expectedStatus)
    {
        if (!File.Exists(path)) return [];
        var rows = Csv.Parse(File.ReadAllText(path, new UTF8Encoding(true)));
        if (rows.Count < 2) return [];
        var legacy = rows[0].Count >= 4 && rows[0][3] == "提前提醒分钟数"; var hasNote = rows[0].LastOrDefault() == "备注"; var result = new List<TodoItem>();
        foreach (var row in rows.Skip(1))
        {
            try
            {
                if (row.All(string.IsNullOrEmpty)) continue; var offset = legacy ? 1 : 0; var min = legacy ? 9 : 8; if (row.Count < min) continue;
                var id = row[0].Trim(); var content = row[1]; if (id.Length == 0 || content.Length == 0 || !long.TryParse(row[5 + offset], out var sequence)) continue;
                var planned = ParseTime(row[2]); var remind = ParseTime(row[3 + offset]); if (legacy && remind is null && planned is not null && int.TryParse(row[3], out var minutes)) remind = planned.Value.AddMinutes(-minutes);
                var created = ParseTime(row[4 + offset]); if (created is null) continue;
                if (expectedStatus is ItemStatus.Active) { var state = remind is null ? ReminderStatus.None : row[6 + offset] == "已提醒" ? ReminderStatus.Reminded : ReminderStatus.Pending; result.Add(new TodoItem(id, content, planned, remind, created.Value, sequence, ItemStatus.Active, state, ParseTime(row[7 + offset]), null, hasNote && row.Count > 8 + offset ? row[^1] : "")); }
                else { var status = row[6 + offset] == "已取消" ? ItemStatus.Cancelled : ItemStatus.Completed; var ended = ParseTime(row[7 + offset]); if (ended is null) continue; result.Add(new TodoItem(id, content, planned, remind, created.Value, sequence, status, null, null, ended, hasNote && row.Count > 8 + offset ? row[^1] : "")); }
            }
            catch { log.Write($"Skipped malformed legacy row in {Path.GetFileName(path)}"); }
        }
        return result;
    }

    private void SaveLegacySettings(SqliteTransaction transaction)
    {
        var map = new Dictionary<string, string> { ["hotkey"] = "Ctrl+Alt+Space", ["default_remind_minutes"] = "5", ["auto_start"] = "0" };
        if (File.Exists(paths.ConfigPath)) { var text = File.ReadAllText(paths.ConfigPath, Encoding.Unicode); foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) { var pair = line.Split('=', 2); if (pair.Length == 2 && map.ContainsKey(pair[0])) map[pair[0]] = pair[0] == "hotkey" ? AhkHotkey(pair[1]) : pair[1]; } }
        foreach (var (key, value) in map) { using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT OR IGNORE INTO app_settings(key,value) VALUES(@key,@value)"; command.Parameters.AddWithValue("@key", key); command.Parameters.AddWithValue("@value", value); command.ExecuteNonQuery(); }
    }
    private static string AhkHotkey(string value) => value.Trim().Replace("^", "Ctrl+").Replace("!", "Alt+").Replace("#", "Win+").Replace("+", "Shift+");
    private static DateTime? ParseTime(string value) => DbTime.Read(value.Trim());
    private static string? GetValue(SqliteConnection connection, string key) { using var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM schema_info WHERE key=@key"; command.Parameters.AddWithValue("@key", key); return command.ExecuteScalar() as string; }
    private static void SetValue(SqliteConnection connection, SqliteTransaction transaction, string key, string value) { using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO schema_info(key,value) VALUES(@key,@value) ON CONFLICT(key) DO UPDATE SET value=excluded.value"; command.Parameters.AddWithValue("@key", key); command.Parameters.AddWithValue("@value", value); command.ExecuteNonQuery(); }
}

internal static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++) { var c = text[i]; if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append(c); i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); } else if ((c == '\r' || c == '\n') && !quoted) { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; } else field.Append(c); }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); } return rows;
    }
}
