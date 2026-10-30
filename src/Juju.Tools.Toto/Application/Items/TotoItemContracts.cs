namespace Juju.Tools.Toto.Application.Items;

public enum TotoItemStatus
{
    Active,
    Completed,
    Cancelled
}

public enum TotoReminderStatus
{
    None,
    Pending,
    Reminded
}

public sealed record TotoItemSnapshot(
    string Id,
    string Content,
    DateTime? PlannedAt,
    DateTime? RemindAt,
    DateTime CreatedAt,
    long CreatedSeq,
    TotoItemStatus Status,
    TotoReminderStatus? ReminderStatus,
    DateTime? RemindedAt,
    DateTime? EndedAt,
    string Note);

public sealed record NewTotoItem(string Content, DateTime? PlannedAt, DateTime? RemindAt, string Note = "");

public sealed class TotoHistoryQuery
{
    public string? SearchText { get; init; }
    public string? Id { get; init; }
    public string? Content { get; init; }
    public string? Note { get; init; }
    public TotoItemStatus? Status { get; init; }
    public TotoReminderStatus? ReminderStatus { get; init; }
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

public sealed record TotoHistoryPage(IReadOnlyList<TotoItemSnapshot> Items, int TotalCount, int Page, int PageSize);

public interface ITotoItemsApplicationService
{
    IReadOnlyList<TotoItemSnapshot> GetActiveItems();
    TotoItemSnapshot? GetItem(string id);
    TotoHistoryPage GetHistory(TotoHistoryQuery? query, int page, int pageSize);
    IReadOnlyList<TotoItemSnapshot> GetCompletedItems(DateOnly date);
    void AddItem(NewTotoItem item);
    bool UpdateItem(TotoItemSnapshot item);
    bool EndItem(string id, TotoItemStatus status, string note, DateTime endedAt);
}
