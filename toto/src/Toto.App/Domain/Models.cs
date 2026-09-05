namespace Toto.App.Domain;

/// <summary>表示待办事项的生命周期状态；与 Java <c>enum</c> 类似，但底层整数值可显式指定以稳定持久化含义。</summary>
internal enum ItemStatus
{
    /// <summary>事项尚未结束，保存在活动事项列表中。</summary>
    Active = 0,

    /// <summary>事项已完成，保存在历史事项列表中。</summary>
    Completed = 1,

    /// <summary>事项已取消，保存在历史事项列表中。</summary>
    Cancelled = 2
}

/// <summary>表示带提醒事项的提醒处理状态。</summary>
internal enum ReminderStatus
{
    /// <summary>事项没有配置提醒时间。</summary>
    None = 0,

    /// <summary>提醒时间已配置但尚未触发。</summary>
    Pending = 1,

    /// <summary>提醒已触发并已记录触发时间。</summary>
    Reminded = 2
}

/// <summary>区分工作日开始和结束时显示的计划弹窗。</summary>
internal enum ScheduledPopupKind
{
    /// <summary>工作开始弹窗。</summary>
    WorkStart = 1,

    /// <summary>工作结束弹窗。</summary>
    WorkEnd = 2
}

/// <summary>表示一个待办事项的不可变数据快照。</summary>
/// <remarks><c>record</c> 会自动提供基于值的相等性；与 Java record 相近。主构造函数的参数同时定义只读属性。</remarks>
internal sealed record TodoItem(
    string Id,
    string Content,
    DateTime? PlannedAt,
    DateTime? RemindAt,
    DateTime CreatedAt,
    long CreatedSeq,
    ItemStatus Status,
    ReminderStatus? ReminderStatus,
    DateTime? RemindedAt,
    DateTime? EndedAt,
    string Note);

/// <summary>表示某日的节假日或调休信息。</summary>
/// <remarks>这是值对象；两个属性相同的实例在 record 语义下相等。</remarks>
internal sealed record HolidayCalendarDay(string Name, DateOnly Date, bool IsOffDay);

/// <summary>封装事项查询的可选筛选条件。</summary>
internal sealed class QueryCriteria
{
    /// <summary>按精确事项标识筛选。</summary>
    public string? Id { get; init; }

    /// <summary>按事项内容进行不区分大小写的包含匹配。</summary>
    public string? Content { get; init; }

    /// <summary>按备注进行不区分大小写的包含匹配。</summary>
    public string? Note { get; init; }

    /// <summary>按事项状态筛选。</summary>
    public ItemStatus? Status { get; init; }

    /// <summary>按提醒状态筛选。</summary>
    public ReminderStatus? ReminderStatus { get; init; }

    /// <summary>创建序号的包含下界。</summary>
    public long? CreatedSeqFrom { get; init; }

    /// <summary>创建序号的包含上界。</summary>
    public long? CreatedSeqTo { get; init; }

    /// <summary>计划时间的包含下界。</summary>
    public DateTime? PlannedFrom { get; init; }

    /// <summary>计划时间的排他上界。</summary>
    public DateTime? PlannedTo { get; init; }

    /// <summary>提醒时间的包含下界。</summary>
    public DateTime? RemindFrom { get; init; }

    /// <summary>提醒时间的排他上界。</summary>
    public DateTime? RemindTo { get; init; }

    /// <summary>创建时间的包含下界。</summary>
    public DateTime? CreatedFrom { get; init; }

    /// <summary>创建时间的排他上界。</summary>
    public DateTime? CreatedTo { get; init; }

    /// <summary>实际提醒时间的包含下界。</summary>
    public DateTime? RemindedFrom { get; init; }

    /// <summary>实际提醒时间的排他上界。</summary>
    public DateTime? RemindedTo { get; init; }

    /// <summary>结束时间的包含下界。</summary>
    public DateTime? EndedFrom { get; init; }

    /// <summary>结束时间的排他上界。</summary>
    public DateTime? EndedTo { get; init; }
}

/// <summary>表示一页历史事项及其分页元数据。</summary>
internal sealed record HistoryPage(IReadOnlyList<TodoItem> Items, int TotalCount, int Page, int PageSize);