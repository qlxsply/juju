using Microsoft.Win32;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Juju.Core.Tools;

namespace Toto.App;

/// <summary>
/// 将原独立 Toto 适配为 Juju 内的常驻工具。只保留一份提醒调度器；
/// Juju 负责进程、托盘、单实例、开机启动和全局热键。
/// 提醒 UI 由 Juju 的 WPF 窗口管理器订阅，调度器不依赖任何窗口。
/// </summary>
public sealed class TotoToolHost : IDisposable
{
    private readonly ItemRepository items;
    private readonly SettingsRepository settings;
    private readonly WorkCalendarRepository repository;
    private readonly ReminderScheduler scheduler;
    private readonly AppLog log;
    private readonly SynchronizationContext ui;
    private readonly System.Threading.Timer retry;
    private bool disposed;
    private bool ready;
    private Exception? initializationError;

    public event Action<string>? HotkeyChanged;
    internal event Action<IReadOnlyList<TodoItem>>? DueReminders;
    internal event Action<ScheduledPopupKind>? ScheduledPopup;
    // 宿主先尝试注册候选热键；失败时设置页不落盘，避免下次启动丢失旧快捷键。
    public event Func<string, bool>? HotkeyChanging;
    public string Hotkey => settings.Load().GetValueOrDefault("hotkey", GlobalHotkeyPolicy.None);
    public bool IsValidWindowShortcut(string value) => WindowShortcut.TryNormalize(value, out _);
    public bool CalendarReady => ready;
    public IReadOnlyDictionary<string, string> ToolSettings => settings.Load();
    internal ItemRepository Items => items;
    internal SettingsRepository Settings => settings;
    internal void Reschedule() { if (ready) scheduler.Reschedule(); }
    internal void Resume() { if (ready) scheduler.Resume(); }
    public string? InitializationError => initializationError?.Message;
    public bool EnsureReady() => TryStart();
    private bool TryRegisterHotkey(string candidate) => HotkeyChanging?.Invoke(candidate) ?? true;
    private static bool OptionalTime(string? value) => string.IsNullOrWhiteSpace(value) ||
        TimeOnly.TryParseExact(value, "HH:mm", out _);

    /// <summary>统一设置窗口保存 Toto JSON 配置，并更新后台提醒；不再创建第二个设置窗口。</summary>
    public void SaveToolSettings(IReadOnlyDictionary<string, string> values)
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
        if (!TryRegisterHotkey(newHotkey)) throw new InvalidOperationException("Toto 全局直达热键无效或已占用。");
        try
        {
            var next = values.ToDictionary(item => item.Key, item => item.Value);
            next["hotkey"] = newHotkey;
            settings.SaveDailyReviewSettings(next, DateTime.Now);
            if (ready) scheduler.Reschedule();
            HotkeyChanged?.Invoke(newHotkey);
        }
        catch
        {
            TryRegisterHotkey(previous.GetValueOrDefault("hotkey", GlobalHotkeyPolicy.None));
            throw;
        }
    }

    public int LatestCalendarYear => DateTime.Today >= new DateTime(DateTime.Today.Year, 12, 1)
        ? DateTime.Today.Year + 1 : DateTime.Today.Year;

    public IReadOnlyList<TotoHoliday> GetHolidays(int year) => repository.GetYear(year)
        .Select(day => new TotoHoliday(day.Name, day.Date, day.IsOffDay)).ToArray();

    public async Task DownloadHolidaysAsync(int year)
    {
        if (year > LatestCalendarYear) throw new InvalidOperationException("该年份的节假日尚未开放下载。");
        await Task.Run(() => repository.Download(year));
        if (ready) scheduler.Reschedule();
    }

    public void SaveHoliday(TotoHoliday holiday)
    {
        repository.Upsert(new HolidayCalendarDay(holiday.Name, holiday.Date, holiday.IsOffDay));
        if (ready) scheduler.Reschedule();
    }

    public void DeleteHoliday(DateOnly date)
    {
        repository.Delete(date);
        if (ready) scheduler.Reschedule();
    }

    public TotoToolHost(SynchronizationContext uiContext) : this(uiContext, new AppPaths()) { }

    internal TotoToolHost(SynchronizationContext uiContext, AppPaths paths)
    {
        ui = uiContext;
        log = new AppLog(paths.LogDirectory);
        items = new ItemRepository(paths);
        settings = new SettingsRepository(paths);
        items.EnsureFiles();
        settings.EnsureExists();
        var cutoffMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var cutoffText = cutoffMonth.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(settings.ArchiveProgressMonth) ||
            string.CompareOrdinal(settings.ArchiveProgressMonth, cutoffText) < 0)
        {
            try
            {
                items.ArchiveHistoryThrough(cutoffMonth);
                settings.SaveArchiveProgressMonth(cutoffText);
            }
            catch (Exception ex)
            {
                log.Write($"Toto history archive failed; it will be retried on the next start: {ex}");
            }
        }
        repository = new WorkCalendarRepository(new HolidayCalendarDownloadService(paths));
        scheduler = new ReminderScheduler(items, settings, ui);
        scheduler.DueReminders += due => DueReminders?.Invoke(due);
        scheduler.ScheduledPopup += kind => ScheduledPopup?.Invoke(kind);
        // 网络下载失败时不能使 Juju/JSON 无法启动；Toto 保留严格日历校验并定时重试。
        retry = new System.Threading.Timer(_ => ui.Post(__ => { if (!disposed) TryStart(); }, null), null,
            Timeout.Infinite, Timeout.Infinite);
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.TimeChanged += TimeChanged;
        // 由 Juju.App 的 TotoToolModule.StartAsync 启动；构造只组装资源，不隐式运行后台调度。
    }

    private bool TryStart()
    {
        if (disposed) return false;
        if (ready) return true;
        try
        {
            repository.GetYear(DateTime.Today.Year);
            var end = DateTime.Today.AddDays(7);
            if (end.Year != DateTime.Today.Year) repository.GetYear(end.Year);
            scheduler.Reschedule();
            ready = true;
            initializationError = null;
            retry.Change(Timeout.Infinite, Timeout.Infinite);
            return true;
        }
        catch (Exception ex)
        {
            initializationError = ex;
            log.Write($"Toto calendar initialization failed; retry scheduled: {ex}");
            retry.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
            return false;
        }
    }

    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) scheduler.SetLocked(true);
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            scheduler.SetLocked(false);
            if (TryStart()) scheduler.Resume();
        }
    }
    private void PowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && TryStart()) scheduler.Resume();
    }
    private void TimeChanged(object? sender, EventArgs e)
    {
        if (TryStart()) scheduler.Resume();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        SystemEvents.TimeChanged -= TimeChanged;
        retry.Dispose();
        scheduler.Dispose();
    }
}

/// <summary>把原 Toto 内部领域记录作为 WPF 设置页可读的公共只读快照。</summary>
public sealed record TotoHoliday(string Name, DateOnly Date, bool IsOffDay);
