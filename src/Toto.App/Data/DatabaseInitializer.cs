using Microsoft.Data.Sqlite;

namespace Toto.App.Data;

internal sealed class DatabaseInitializer(TotoDatabase database)
{
    public void Initialize()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_info (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS items (
                id TEXT PRIMARY KEY, content TEXT NOT NULL, planned_at TEXT NULL, remind_at TEXT NULL,
                created_at TEXT NOT NULL, created_seq INTEGER NOT NULL, status INTEGER NOT NULL,
                remind_status INTEGER NULL, reminded_at TEXT NULL, ended_at TEXT NULL,
                note TEXT NOT NULL DEFAULT '', CHECK (status IN (0,1,2)),
                CHECK (remind_status IS NULL OR remind_status IN (0,1,2)));
            CREATE INDEX IF NOT EXISTS idx_items_active_plan ON items(status, planned_at, created_seq);
            CREATE INDEX IF NOT EXISTS idx_items_history_end ON items(status, ended_at DESC, created_seq DESC);
            CREATE INDEX IF NOT EXISTS idx_items_reminder ON items(status, remind_status, remind_at);
            CREATE INDEX IF NOT EXISTS idx_items_created_at ON items(created_at);
            CREATE INDEX IF NOT EXISTS idx_items_planned_at ON items(planned_at);
            CREATE INDEX IF NOT EXISTS idx_items_remind_at ON items(remind_at);
            CREATE INDEX IF NOT EXISTS idx_items_reminded_at ON items(reminded_at);
            CREATE INDEX IF NOT EXISTS idx_items_ended_at ON items(ended_at);
            CREATE TABLE IF NOT EXISTS app_settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS work_calendar_exceptions (
                date TEXT PRIMARY KEY, day_type INTEGER NOT NULL, name TEXT NOT NULL DEFAULT '',
                note TEXT NOT NULL DEFAULT '', source TEXT NOT NULL DEFAULT 'manual', updated_at TEXT NOT NULL,
                CHECK(day_type IN (0,1)));
            CREATE TABLE IF NOT EXISTS scheduled_popup_log (
                trigger_date TEXT NOT NULL, trigger_kind INTEGER NOT NULL, shown_at TEXT NOT NULL,
                PRIMARY KEY(trigger_date, trigger_kind), CHECK(trigger_kind IN (1,2)));
            INSERT OR IGNORE INTO schema_info(key, value) VALUES('schema_version', '1');
            """;
        command.ExecuteNonQuery();
    }
}
