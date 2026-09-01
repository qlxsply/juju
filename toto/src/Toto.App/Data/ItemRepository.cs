using Microsoft.Data.Sqlite;
using Toto.App.Domain;

namespace Toto.App.Data;

internal sealed class ItemRepository(TotoDatabase database)
{
    public IReadOnlyList<TodoItem> GetActive(QueryCriteria? criteria = null) => Query(criteria, "status = 0", "CASE WHEN planned_at IS NULL OR planned_at = '' THEN 1 ELSE 0 END, planned_at, created_seq");

    public HistoryPage GetHistory(QueryCriteria? criteria, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 100 or 200 or 500 ? pageSize : 200;
        using var connection = database.Open();
        var (where, parameters) = BuildWhere(criteria, "status IN (1,2)");
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM items WHERE {where}";
        AddParameters(count, parameters);
        var total = Convert.ToInt32(count.ExecuteScalar());
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM items WHERE {where} ORDER BY ended_at DESC, created_seq DESC LIMIT @limit OFFSET @offset";
        AddParameters(command, parameters);
        command.Parameters.AddWithValue("@limit", pageSize);
        command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
        return new HistoryPage(Read(command), total, page, pageSize);
    }

    public TodoItem? Get(string id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM items WHERE id=@id";
        command.Parameters.AddWithValue("@id", id);
        return Read(command).SingleOrDefault();
    }

    public long NextCreatedSeq()
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(created_seq), 0) + 1 FROM items";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Add(TodoItem item)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Insert(connection, transaction, item);
        transaction.Commit();
    }

    public bool Update(TodoItem item)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE items SET content=@content, planned_at=@planned, remind_at=@remind, remind_status=@remindStatus, reminded_at=@reminded, note=@note WHERE id=@id AND status=0";
        command.Parameters.AddWithValue("@content", item.Content); command.Parameters.AddWithValue("@planned", Db(item.PlannedAt));
        command.Parameters.AddWithValue("@remind", Db(item.RemindAt)); command.Parameters.AddWithValue("@remindStatus", item.ReminderStatus is null ? DBNull.Value : (int)item.ReminderStatus.Value);
        command.Parameters.AddWithValue("@reminded", Db(item.RemindedAt)); command.Parameters.AddWithValue("@note", item.Note); command.Parameters.AddWithValue("@id", item.Id);
        return command.ExecuteNonQuery() == 1;
    }

    public bool End(string id, ItemStatus status, string note, DateTime endedAt)
    {
        using var connection = database.Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE items SET status=@status, ended_at=@ended, note=@note WHERE id=@id AND status=0";
        command.Parameters.AddWithValue("@status", (int)status); command.Parameters.AddWithValue("@ended", DbTime.Text(endedAt)); command.Parameters.AddWithValue("@note", note); command.Parameters.AddWithValue("@id", id);
        var changed = command.ExecuteNonQuery() == 1; if (changed) transaction.Commit(); else transaction.Rollback(); return changed;
    }

    public DateTime? GetNextReminder()
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT remind_at FROM items WHERE status=0 AND remind_status=1 AND remind_at IS NOT NULL ORDER BY remind_at LIMIT 1";
        return DbTime.Read(command.ExecuteScalar() as string);
    }

    public IReadOnlyList<TodoItem> MarkDueReminders(DateTime now)
    {
        using var connection = database.Open(); using var transaction = connection.BeginTransaction();
        using var select = connection.CreateCommand(); select.Transaction = transaction;
        select.CommandText = $"SELECT {Columns} FROM items WHERE status=0 AND remind_status=1 AND remind_at IS NOT NULL AND remind_at<=@now ORDER BY remind_at";
        select.Parameters.AddWithValue("@now", DbTime.Text(now)); var due = Read(select);
        if (due.Count > 0) { using var update = connection.CreateCommand(); update.Transaction = transaction; update.CommandText = "UPDATE items SET remind_status=2, reminded_at=@now WHERE status=0 AND remind_status=1 AND remind_at IS NOT NULL AND remind_at<=@now"; update.Parameters.AddWithValue("@now", DbTime.Text(now)); update.ExecuteNonQuery(); }
        transaction.Commit(); return due.Select(item => item with { ReminderStatus = ReminderStatus.Reminded, RemindedAt = now }).ToArray();
    }

    internal void Insert(SqliteConnection connection, SqliteTransaction transaction, TodoItem item)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO items(id,content,planned_at,remind_at,created_at,created_seq,status,remind_status,reminded_at,ended_at,note) VALUES(@id,@content,@planned,@remind,@created,@seq,@status,@remindStatus,@reminded,@ended,@note)";
        command.Parameters.AddWithValue("@id", item.Id); command.Parameters.AddWithValue("@content", item.Content); command.Parameters.AddWithValue("@planned", Db(item.PlannedAt)); command.Parameters.AddWithValue("@remind", Db(item.RemindAt)); command.Parameters.AddWithValue("@created", DbTime.Text(item.CreatedAt)); command.Parameters.AddWithValue("@seq", item.CreatedSeq); command.Parameters.AddWithValue("@status", (int)item.Status); command.Parameters.AddWithValue("@remindStatus", item.ReminderStatus is null ? DBNull.Value : (int)item.ReminderStatus.Value); command.Parameters.AddWithValue("@reminded", Db(item.RemindedAt)); command.Parameters.AddWithValue("@ended", Db(item.EndedAt)); command.Parameters.AddWithValue("@note", item.Note); command.ExecuteNonQuery();
    }

    private IReadOnlyList<TodoItem> Query(QueryCriteria? criteria, string fixedWhere, string orderBy) { using var connection = database.Open(); var (where, parameters) = BuildWhere(criteria, fixedWhere); using var command = connection.CreateCommand(); command.CommandText = $"SELECT {Columns} FROM items WHERE {where} ORDER BY {orderBy}"; AddParameters(command, parameters); return Read(command); }
    private static readonly string Columns = "id,content,planned_at,remind_at,created_at,created_seq,status,remind_status,reminded_at,ended_at,note";
    private static object Db(DateTime? value) => value is null ? DBNull.Value : DbTime.Text(value.Value);
    private static IReadOnlyList<TodoItem> Read(SqliteCommand command) { using var reader = command.ExecuteReader(); var items = new List<TodoItem>(); while (reader.Read()) items.Add(new TodoItem(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : DbTime.Read(reader.GetString(2)), reader.IsDBNull(3) ? null : DbTime.Read(reader.GetString(3)), DbTime.Read(reader.GetString(4))!.Value, reader.GetInt64(5), (ItemStatus)reader.GetInt32(6), reader.IsDBNull(7) ? null : (ReminderStatus)reader.GetInt32(7), reader.IsDBNull(8) ? null : DbTime.Read(reader.GetString(8)), reader.IsDBNull(9) ? null : DbTime.Read(reader.GetString(9)), reader.GetString(10))); return items; }
    private static void AddParameters(SqliteCommand command, IEnumerable<(string Name, object Value)> parameters) { foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value); }
    private static (string Where, List<(string, object)> Parameters) BuildWhere(QueryCriteria? c, string fixedWhere)
    {
        var clauses = new List<string> { fixedWhere }; var parameters = new List<(string, object)>(); if (c is null) return (string.Join(" AND ", clauses), parameters);
        void Equal(string column, object? value, string key) { if (value is not null) { clauses.Add($"{column}=@{key}"); parameters.Add(("@" + key, value)); } }
        void Range(string column, DateTime? from, DateTime? to, string key) { if (from is not null) { clauses.Add($"{column}>=@{key}From"); parameters.Add(("@" + key + "From", DbTime.Text(from.Value))); } if (to is not null) { clauses.Add($"{column}<@{key}To"); parameters.Add(("@" + key + "To", DbTime.Text(to.Value))); } }
        Equal("id", string.IsNullOrWhiteSpace(c.Id) ? null : c.Id, "id"); Equal("status", c.Status is null ? null : (int)c.Status.Value, "status"); Equal("remind_status", c.ReminderStatus is null ? null : (int)c.ReminderStatus.Value, "remindStatus"); Equal("created_seq>=", c.CreatedSeqFrom, "seqFrom"); Equal("created_seq<=", c.CreatedSeqTo, "seqTo");
        if (!string.IsNullOrEmpty(c.Content)) { clauses.Add("content LIKE @content ESCAPE '\\'"); parameters.Add(("@content", "%" + EscapeLike(c.Content) + "%")); } if (!string.IsNullOrEmpty(c.Note)) { clauses.Add("note LIKE @note ESCAPE '\\'"); parameters.Add(("@note", "%" + EscapeLike(c.Note) + "%")); }
        Range("planned_at", c.PlannedFrom, c.PlannedTo, "planned"); Range("remind_at", c.RemindFrom, c.RemindTo, "remind"); Range("created_at", c.CreatedFrom, c.CreatedTo, "created"); Range("reminded_at", c.RemindedFrom, c.RemindedTo, "reminded"); Range("ended_at", c.EndedFrom, c.EndedTo, "ended"); return (string.Join(" AND ", clauses), parameters);
    }
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
