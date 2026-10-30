using Juju.Core.Tools;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Application.Calendar;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Reminders;
using Juju.Tools.Toto.Application.Settings;
using Toto.App.Domain;
using Toto.App.Services;

namespace Juju.Tools.Toto.Application;

internal sealed class TotoApplicationService(
    ITotoItemRepository items,
    ITotoSettingsRepository settings,
    ITotoCalendarRepository calendar,
    ITotoRuntimeControl runtime) : ITotoApplicationService
{
    public IReadOnlyList<TotoItemSnapshot> GetActiveItems() => items.GetActive().Select(ToSnapshot).ToArray();

    public TotoItemSnapshot? GetItem(string id) => items.Get(id) is { } item ? ToSnapshot(item) : null;

    public TotoHistoryPage GetHistory(TotoHistoryQuery? query, int page, int pageSize)
    {
        var result = items.GetHistory(ToCriteria(query), page, pageSize);
        return new TotoHistoryPage(result.Items.Select(ToSnapshot).ToArray(), result.TotalCount, result.Page,
            result.PageSize);
    }

    public IReadOnlyList<TotoItemSnapshot> GetCompletedItems(DateOnly date)
    {
        var criteria = new QueryCriteria
        {
            Status = ItemStatus.Completed,
            EndedFrom = date.ToDateTime(TimeOnly.MinValue),
            EndedTo = date.AddDays(1).ToDateTime(TimeOnly.MinValue)
        };
        var firstPage = items.GetHistory(criteria, 1, 500);
        var completed = firstPage.Items.ToList();
        for (var page = 2; completed.Count < firstPage.TotalCount; page++)
            completed.AddRange(items.GetHistory(criteria, page, 500).Items);
        return completed.Select(ToSnapshot).ToArray();
    }

    public void AddItem(NewTotoItem request)
    {
        var item = new TodoItem(Guid.NewGuid().ToString(), request.Content, request.PlannedAt, request.RemindAt,
            DateTime.Now, 0, ItemStatus.Active,
            request.RemindAt is null ? ReminderStatus.None : ReminderStatus.Pending, null, null, request.Note);
        items.Add(item);
        if (runtime.CalendarReady) runtime.Reschedule();
    }

    public bool UpdateItem(TotoItemSnapshot item)
    {
        var updated = items.Update(new TodoItem(item.Id, item.Content, item.PlannedAt, item.RemindAt, item.CreatedAt,
            item.CreatedSeq, ToDomain(item.Status), item.ReminderStatus is { } reminder ? ToDomain(reminder) : null,
            item.RemindedAt, item.EndedAt, item.Note));
        if (updated && runtime.CalendarReady) runtime.Reschedule();
        return updated;
    }

    public bool EndItem(string id, TotoItemStatus status, string note, DateTime endedAt)
    {
        if (status == TotoItemStatus.Active) throw new ArgumentOutOfRangeException(nameof(status));
        var ended = items.End(id, ToDomain(status), note, endedAt);
        if (ended && runtime.CalendarReady) runtime.Reschedule();
        return ended;
    }

    public IReadOnlyDictionary<string, string> Settings => settings.Load();
    public string GlobalHotkey => settings.Load().GetValueOrDefault("hotkey", GlobalHotkeyPolicy.None);
    public bool CalendarReady => runtime.CalendarReady;

    public event Action<string>? HotkeyChanged
    {
        add => runtime.HotkeyChanged += value;
        remove => runtime.HotkeyChanged -= value;
    }

    public event Func<string, bool>? HotkeyChanging
    {
        add => runtime.HotkeyChanging += value;
        remove => runtime.HotkeyChanging -= value;
    }

    public void SaveSettings(IReadOnlyDictionary<string, string> values)
    {
        if (!int.TryParse(values.GetValueOrDefault("default_remind_minutes"), out var minutes) || minutes < 0 ||
            !OptionalTime(values.GetValueOrDefault("daily_review_start_time")) ||
            !OptionalTime(values.GetValueOrDefault("daily_review_end_time")) ||
            values.GetValueOrDefault("daily_review_workdays") is not ("0" or "1"))
            throw new InvalidOperationException("默认提醒分钟或每日事项盘点设置无效。");

        var shortcuts = values.Where(item => item.Key.StartsWith("shortcut_", StringComparison.Ordinal))
            .Select(item => item.Value).ToArray();
        var normalizedShortcuts = shortcuts.Select(value => WindowShortcut.TryNormalize(value, out var shortcut)
            ? shortcut : "").ToArray();
        if (shortcuts.Length != 7 || normalizedShortcuts.Any(string.IsNullOrEmpty) ||
            normalizedShortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedShortcuts.Length)
            throw new InvalidOperationException("Toto 的七个窗口内快捷键必须有效且不能重复。");

        var newHotkey = GlobalHotkeyPolicy.Normalize(values.GetValueOrDefault("hotkey"), optional: true);
        var previous = settings.Load();
        if (!runtime.TryRegisterHotkey(newHotkey))
            throw new InvalidOperationException("Toto 全局直达热键无效或已占用。");
        try
        {
            var next = values.ToDictionary(item => item.Key, item => item.Value);
            next["hotkey"] = newHotkey;
            settings.SaveDailyReviewSettings(next, DateTime.Now);
            runtime.SettingsSaved(newHotkey);
        }
        catch
        {
            runtime.TryRegisterHotkey(previous.GetValueOrDefault("hotkey", GlobalHotkeyPolicy.None));
            throw;
        }
    }

    public int LatestCalendarYear => DateTime.Today >= new DateTime(DateTime.Today.Year, 12, 1)
        ? DateTime.Today.Year + 1 : DateTime.Today.Year;

    public IReadOnlyList<TotoHolidaySnapshot> GetHolidays(int year) =>
        calendar.GetYear(year).Select(day => new TotoHolidaySnapshot(day.Name, day.Date, day.IsOffDay)).ToArray();

    public async Task DownloadHolidaysAsync(int year)
    {
        if (year > LatestCalendarYear) throw new InvalidOperationException("该年份的节假日尚未开放下载。");
        await Task.Run(() => calendar.Download(year));
        if (runtime.CalendarReady) runtime.Reschedule();
    }

    public void SaveHoliday(TotoHolidaySnapshot holiday)
    {
        calendar.Upsert(new HolidayCalendarDay(holiday.Name, holiday.Date, holiday.IsOffDay));
        if (runtime.CalendarReady) runtime.Reschedule();
    }

    public void DeleteHoliday(DateOnly date)
    {
        calendar.Delete(date);
        if (runtime.CalendarReady) runtime.Reschedule();
    }

    internal static TotoItemSnapshot ToSnapshot(TodoItem item) => new(item.Id, item.Content, item.PlannedAt,
        item.RemindAt, item.CreatedAt, item.CreatedSeq, ToPublic(item.Status),
        item.ReminderStatus is { } reminder ? ToPublic(reminder) : null, item.RemindedAt, item.EndedAt, item.Note);

    private static QueryCriteria? ToCriteria(TotoHistoryQuery? query) => query is null ? null : new QueryCriteria
    {
        SearchText = query.SearchText,
        Id = query.Id,
        Content = query.Content,
        Note = query.Note,
        Status = query.Status is { } status ? ToDomain(status) : null,
        ReminderStatus = query.ReminderStatus is { } reminder ? ToDomain(reminder) : null,
        CreatedSeqFrom = query.CreatedSeqFrom,
        CreatedSeqTo = query.CreatedSeqTo,
        PlannedFrom = query.PlannedFrom,
        PlannedTo = query.PlannedTo,
        RemindFrom = query.RemindFrom,
        RemindTo = query.RemindTo,
        CreatedFrom = query.CreatedFrom,
        CreatedTo = query.CreatedTo,
        RemindedFrom = query.RemindedFrom,
        RemindedTo = query.RemindedTo,
        EndedFrom = query.EndedFrom,
        EndedTo = query.EndedTo
    };

    private static TotoItemStatus ToPublic(ItemStatus status) => status switch
    {
        ItemStatus.Active => TotoItemStatus.Active,
        ItemStatus.Completed => TotoItemStatus.Completed,
        ItemStatus.Cancelled => TotoItemStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static ItemStatus ToDomain(TotoItemStatus status) => status switch
    {
        TotoItemStatus.Active => ItemStatus.Active,
        TotoItemStatus.Completed => ItemStatus.Completed,
        TotoItemStatus.Cancelled => ItemStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static TotoReminderStatus ToPublic(ReminderStatus status) => status switch
    {
        ReminderStatus.None => TotoReminderStatus.None,
        ReminderStatus.Pending => TotoReminderStatus.Pending,
        ReminderStatus.Reminded => TotoReminderStatus.Reminded,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static ReminderStatus ToDomain(TotoReminderStatus status) => status switch
    {
        TotoReminderStatus.None => ReminderStatus.None,
        TotoReminderStatus.Pending => ReminderStatus.Pending,
        TotoReminderStatus.Reminded => ReminderStatus.Reminded,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static bool OptionalTime(string? value) => string.IsNullOrWhiteSpace(value) ||
        TimeOnly.TryParseExact(value, "HH:mm", out _);
}
