using Juju.Tools.Toto.Domain;

namespace Juju.Tools.Toto.Application.Ports;

internal interface ITotoItemRepository
{
    void EnsureFiles();
    IReadOnlyList<TodoItem> GetActive(QueryCriteria? criteria = null);
    HistoryPage GetHistory(QueryCriteria? criteria, int page, int pageSize);
    void ArchiveHistoryThrough(DateOnly cutoffMonth);
    TodoItem? Get(string id);
    void Add(TodoItem item);
    bool Update(TodoItem item);
    bool End(string id, ItemStatus status, string note, DateTime endedAt);
    DateTime? GetNextReminder();
    IReadOnlyList<TodoItem> MarkDueReminders(DateTime now);
}

internal interface ITotoSettingsRepository
{
    IReadOnlyDictionary<string, string> Load();
    void EnsureExists();
    void Save(IReadOnlyDictionary<string, string> settings);
    void SaveDailyReviewSettings(IReadOnlyDictionary<string, string> values, DateTime now);
    string ArchiveProgressMonth { get; }
    void SaveArchiveProgressMonth(string month);
    bool WasScheduledPopupShown(DateOnly date, ScheduledPopupKind kind);
    bool TryMarkScheduledPopupShown(DateOnly date, ScheduledPopupKind kind, DateTime shownAt);
}

internal interface ITotoCalendarRepository
{
    IReadOnlyList<HolidayCalendarDay> GetYear(int year);
    void Download(int year);
    void Upsert(HolidayCalendarDay holiday);
    void Delete(DateOnly date);
}

/// <summary>运行时与设置用例协作的窄端口。</summary>
public interface ITotoRuntimeControl
{
    bool CalendarReady { get; }
    event Action<string>? HotkeyChanged;
    event Func<string, bool>? HotkeyChanging;
    bool TryRegisterHotkey(string candidate);
    void SettingsSaved(string hotkey);
    void Reschedule();
}
