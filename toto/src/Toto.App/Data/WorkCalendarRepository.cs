using Toto.App.Domain;

namespace Toto.App.Data;

internal sealed class WorkCalendarRepository(TotoDatabase database)
{
    public IReadOnlyList<WorkCalendarException> GetYear(int year)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT date,day_type,name,note,source,updated_at FROM work_calendar_exceptions WHERE date>=@from AND date<@to ORDER BY date";
        command.Parameters.AddWithValue("@from", $"{year:D4}-01-01"); command.Parameters.AddWithValue("@to", $"{year + 1:D4}-01-01"); return Read(command);
    }
    public WorkCalendarException? Get(DateOnly date) { using var connection = database.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT date,day_type,name,note,source,updated_at FROM work_calendar_exceptions WHERE date=@date"; command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd")); return Read(command).SingleOrDefault(); }
    public void Upsert(WorkCalendarException item) { using var connection = database.Open(); using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO work_calendar_exceptions(date,day_type,name,note,source,updated_at) VALUES(@date,@type,@name,@note,@source,@updated) ON CONFLICT(date) DO UPDATE SET day_type=excluded.day_type,name=excluded.name,note=excluded.note,source=excluded.source,updated_at=excluded.updated_at"; Bind(command, item); command.ExecuteNonQuery(); }
    public void Delete(DateOnly date) { using var connection = database.Open(); using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM work_calendar_exceptions WHERE date=@date"; command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd")); command.ExecuteNonQuery(); }
    public bool WasShown(DateOnly date, ScheduledPopupKind kind) { using var connection = database.Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT EXISTS(SELECT 1 FROM scheduled_popup_log WHERE trigger_date=@date AND trigger_kind=@kind)"; command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("@kind", (int)kind); return Convert.ToInt64(command.ExecuteScalar()) == 1; }
    public bool TryMarkShown(DateOnly date, ScheduledPopupKind kind, DateTime now) { using var connection = database.Open(); using var command = connection.CreateCommand(); command.CommandText = "INSERT OR IGNORE INTO scheduled_popup_log(trigger_date,trigger_kind,shown_at) VALUES(@date,@kind,@shown)"; command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("@kind", (int)kind); command.Parameters.AddWithValue("@shown", DbTime.Text(now)); return command.ExecuteNonQuery() == 1; }
    private static void Bind(Microsoft.Data.Sqlite.SqliteCommand command, WorkCalendarException item) { command.Parameters.AddWithValue("@date", item.Date.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("@type", (int)item.DayType); command.Parameters.AddWithValue("@name", item.Name); command.Parameters.AddWithValue("@note", item.Note); command.Parameters.AddWithValue("@source", item.Source); command.Parameters.AddWithValue("@updated", DbTime.Text(item.UpdatedAt)); }
    private static IReadOnlyList<WorkCalendarException> Read(Microsoft.Data.Sqlite.SqliteCommand command) { using var reader = command.ExecuteReader(); var result = new List<WorkCalendarException>(); while (reader.Read()) result.Add(new WorkCalendarException(DateOnly.Parse(reader.GetString(0)), (DayType)reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DbTime.Read(reader.GetString(5))!.Value)); return result; }
}
