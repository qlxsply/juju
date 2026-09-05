using Toto.App.Domain;

namespace Toto.App.Data;

internal sealed class ItemRepository(AppPaths paths)
{
    private static readonly string[] ActiveHeader =
        ["事项ID", "事项内容", "计划时间", "提醒时间", "创建时间", "创建序号", "提醒状态", "响铃时间", "备注"];

    private static readonly string[] HistoryHeader =
        ["事项ID", "事项内容", "计划时间", "提醒时间", "创建时间", "创建序号", "结束状态", "结束时间", "备注"];

    public void EnsureFiles()
    {
        if (!File.Exists(paths.ActiveCsvPath)) CsvFile.WriteAtomically(paths.ActiveCsvPath, ActiveHeader, []);
        if (!File.Exists(paths.HistoryCsvPath)) CsvFile.WriteAtomically(paths.HistoryCsvPath, HistoryHeader, []);
    }

    public IReadOnlyList<TodoItem> GetActive(QueryCriteria? criteria = null) => Filter(ReadActive(), criteria)
        .OrderBy(item => item.PlannedAt is null).ThenBy(item => item.PlannedAt).ThenBy(item => item.CreatedSeq)
        .ToArray();

    public HistoryPage GetHistory(QueryCriteria? criteria, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = pageSize is 100 or 200 or 500 ? pageSize : 200;
        var all = Filter(ReadHistory(), criteria).OrderByDescending(item => item.EndedAt)
            .ThenByDescending(item => item.CreatedSeq).ToArray();
        return new HistoryPage(all.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), all.Length, page, pageSize);
    }

    public TodoItem? Get(string id) => ReadActive().Concat(ReadHistory()).SingleOrDefault(item => item.Id == id);

    public long NextCreatedSeq() =>
        ReadActive().Concat(ReadHistory()).Select(item => item.CreatedSeq).DefaultIfEmpty().Max() + 1;

    public void Add(TodoItem item)
    {
        var active = ReadActive();
        active.Add(item);
        SaveActive(active);
    }

    public bool Update(TodoItem item)
    {
        var active = ReadActive();
        var index = active.FindIndex(x => x.Id == item.Id);
        if (index < 0) return false;
        active[index] = item;
        SaveActive(active);
        return true;
    }

    public bool End(string id, ItemStatus status, string note, DateTime endedAt)
    {
        var active = ReadActive();
        var index = active.FindIndex(item => item.Id == id);
        if (index < 0) return false;
        var item = active[index] with { Status = status, EndedAt = endedAt, Note = note };
        var history = ReadHistory();
        history.Add(item);
        SaveHistory(history);
        active.RemoveAt(index);
        SaveActive(active);
        return true;
    }

    public DateTime? GetNextReminder() => ReadActive()
        .Where(item => item.ReminderStatus == ReminderStatus.Pending && item.RemindAt is not null)
        .Select(item => item.RemindAt).Min();

    public IReadOnlyList<TodoItem> MarkDueReminders(DateTime now)
    {
        var active = ReadActive();
        var due = active.Where(item => item.ReminderStatus == ReminderStatus.Pending && item.RemindAt <= now).ToArray();
        if (due.Length == 0) return due;
        foreach (var item in due)
            active[active.FindIndex(x => x.Id == item.Id)] =
                item with { ReminderStatus = ReminderStatus.Reminded, RemindedAt = now };
        SaveActive(active);
        return due.Select(item => item with { ReminderStatus = ReminderStatus.Reminded, RemindedAt = now }).ToArray();
    }

    private List<TodoItem> ReadActive() => Read(paths.ActiveCsvPath, true);
    private List<TodoItem> ReadHistory() => Read(paths.HistoryCsvPath, false);

    private void SaveActive(IEnumerable<TodoItem> items) => CsvFile.WriteAtomically(paths.ActiveCsvPath, ActiveHeader,
        items.OrderBy(item => item.PlannedAt is null).ThenBy(item => item.PlannedAt).ThenBy(item => item.CreatedSeq)
            .Select(ToActiveRow));

    private void SaveHistory(IEnumerable<TodoItem> items) => CsvFile.WriteAtomically(paths.HistoryCsvPath,
        HistoryHeader,
        items.OrderByDescending(item => item.EndedAt).ThenByDescending(item => item.CreatedSeq).Select(ToHistoryRow));

    private static List<TodoItem> Read(string path, bool active)
    {
        var rows = CsvFile.Read(path);
        if (rows.Count == 0) return [];
        var legacy = rows[0].Count >= 4 && rows[0][3] == "提前提醒分钟数";
        var items = new List<TodoItem>();
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrEmpty)) continue;
            try
            {
                var offset = legacy ? 1 : 0;
                if (row.Count < 8 + offset || !long.TryParse(row[5 + offset], out var sequence) ||
                    DateTimeText.Read(row[4 + offset]) is not { } created) continue;
                var planned = DateTimeText.Read(row[2]);
                var remind = DateTimeText.Read(row[3 + offset]);
                if (legacy && remind is null && planned is not null && int.TryParse(row[3], out var minutes))
                    remind = planned.Value.AddMinutes(-minutes);
                var note = row.Count > 8 + offset ? row[^1] : "";
                if (active)
                {
                    var reminder = remind is null ? ReminderStatus.None :
                        row[6 + offset] == "已提醒" ? ReminderStatus.Reminded : ReminderStatus.Pending;
                    items.Add(new TodoItem(row[0], row[1], planned, remind, created, sequence, ItemStatus.Active,
                        reminder, DateTimeText.Read(row[7 + offset]), null, note));
                }
                else
                {
                    var status = row[6 + offset] == "已取消" ? ItemStatus.Cancelled : ItemStatus.Completed;
                    var ended = DateTimeText.Read(row[7 + offset]);
                    if (ended is not null)
                        items.Add(new TodoItem(row[0], row[1], planned, remind, created, sequence, status, null, null,
                            ended, note));
                }
            }
            catch
            {
                // ignored
            }
        }

        return items;
    }

    private static IReadOnlyList<string> ToActiveRow(TodoItem item) =>
    [
        item.Id, item.Content, DateTimeText.Text(item.PlannedAt), DateTimeText.Text(item.RemindAt),
        DateTimeText.Text(item.CreatedAt), item.CreatedSeq.ToString(),
        item.ReminderStatus switch { ReminderStatus.Reminded => "已提醒", ReminderStatus.Pending => "未提醒", _ => "无提醒" },
        DateTimeText.Text(item.RemindedAt), item.Note
    ];

    private static IReadOnlyList<string> ToHistoryRow(TodoItem item) =>
    [
        item.Id, item.Content, DateTimeText.Text(item.PlannedAt), DateTimeText.Text(item.RemindAt),
        DateTimeText.Text(item.CreatedAt), item.CreatedSeq.ToString(),
        item.Status == ItemStatus.Cancelled ? "已取消" : "已完成", DateTimeText.Text(item.EndedAt), item.Note
    ];

    private static IEnumerable<TodoItem> Filter(IEnumerable<TodoItem> items, QueryCriteria? c)
    {
        if (c is null) return items;
        return items.Where(item => (string.IsNullOrWhiteSpace(c.Id) || item.Id == c.Id) &&
                                   (string.IsNullOrWhiteSpace(c.Content) ||
                                    item.Content.Contains(c.Content, StringComparison.OrdinalIgnoreCase)) &&
                                   (string.IsNullOrWhiteSpace(c.Note) ||
                                    item.Note.Contains(c.Note, StringComparison.OrdinalIgnoreCase)) &&
                                   (c.Status is null || item.Status == c.Status) &&
                                   (c.ReminderStatus is null || item.ReminderStatus == c.ReminderStatus) &&
                                   (c.CreatedSeqFrom is null || item.CreatedSeq >= c.CreatedSeqFrom) &&
                                   (c.CreatedSeqTo is null || item.CreatedSeq <= c.CreatedSeqTo) &&
                                   InRange(item.PlannedAt, c.PlannedFrom, c.PlannedTo) &&
                                   InRange(item.RemindAt, c.RemindFrom, c.RemindTo) &&
                                   InRange(item.CreatedAt, c.CreatedFrom, c.CreatedTo) &&
                                   InRange(item.RemindedAt, c.RemindedFrom, c.RemindedTo) &&
                                   InRange(item.EndedAt, c.EndedFrom, c.EndedTo));
    }

    private static bool InRange(DateTime? value, DateTime? from, DateTime? to) => from is null && to is null ||
        value is not null && (from is null || value >= from) && (to is null || value < to);
}