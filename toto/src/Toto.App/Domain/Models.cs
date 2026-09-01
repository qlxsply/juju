namespace Toto.App.Domain;

internal enum ItemStatus { Active = 0, Completed = 1, Cancelled = 2 }
internal enum ReminderStatus { None = 0, Pending = 1, Reminded = 2 }
internal enum ScheduledPopupKind { WorkStart = 1, WorkEnd = 2 }

internal sealed record TodoItem(
    string Id, string Content, DateTime? PlannedAt, DateTime? RemindAt, DateTime CreatedAt,
    long CreatedSeq, ItemStatus Status, ReminderStatus? ReminderStatus, DateTime? RemindedAt,
    DateTime? EndedAt, string Note);

internal sealed record HolidayCalendarDay(string Name, DateOnly Date, bool IsOffDay);

internal sealed class QueryCriteria
{
    public string? Id { get; init; }
    public string? Content { get; init; }
    public string? Note { get; init; }
    public ItemStatus? Status { get; init; }
    public ReminderStatus? ReminderStatus { get; init; }
    public long? CreatedSeqFrom { get; init; }
    public long? CreatedSeqTo { get; init; }
    public DateTime? PlannedFrom { get; init; }
    public DateTime? PlannedTo { get; init; }
    public DateTime? RemindFrom { get; init; }
    public DateTime? RemindTo { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public DateTime? RemindedFrom { get; init; }
    public DateTime? RemindedTo { get; init; }
    public DateTime? EndedFrom { get; init; }
    public DateTime? EndedTo { get; init; }
}

internal sealed record HistoryPage(IReadOnlyList<TodoItem> Items, int TotalCount, int Page, int PageSize);
