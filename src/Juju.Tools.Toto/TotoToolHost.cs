using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Application.Reminders;
using Juju.Tools.Toto.Infrastructure.Calendar;
using Juju.Tools.Toto.Infrastructure.Items;
using Juju.Tools.Toto.Infrastructure.Settings;
using Juju.Tools.Toto.Infrastructure.Storage;
using Microsoft.Win32;
using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App;

/// <summary>拥有 Toto 提醒调度器与 Windows 恢复事件；事项和设置用例经 Application 服务暴露。</summary>
public sealed class TotoToolHost : IDisposable, ITotoRuntimeControl
{
    private readonly ITotoItemRepository items;
    private readonly ITotoSettingsRepository settings;
    private readonly ITotoCalendarRepository repository;
    private readonly ReminderScheduler scheduler;
    private readonly AppLog log;
    private readonly SynchronizationContext ui;
    private readonly System.Threading.Timer retry;
    private bool disposed;
    private bool ready;
    private Exception? initializationError;

    public event Action<string>? HotkeyChanged;
    public event Func<string, bool>? HotkeyChanging;
    public event Action<IReadOnlyList<TotoItemSnapshot>>? DueReminders;
    public event Action<TotoScheduledPopupKind>? ScheduledPopup;

    public bool CalendarReady => ready;
    public string? InitializationError => initializationError?.Message;
    public bool EnsureReady() => TryStart();
    internal void Reschedule() { if (ready) scheduler.Reschedule(); }
    internal void Resume() { if (ready) scheduler.Resume(); }

    internal TotoToolHost(SynchronizationContext uiContext, TotoPaths paths)
        : this(uiContext, paths, new ItemRepository(paths), new SettingsRepository(paths),
            new WorkCalendarRepository(new HolidayCalendarDownloadService(paths)), new AppLog(paths.LogDirectory))
    {
    }

    internal TotoToolHost(SynchronizationContext uiContext, TotoPaths paths, ITotoItemRepository items,
        ITotoSettingsRepository settings, ITotoCalendarRepository repository, AppLog log)
    {
        ui = uiContext;
        this.items = items;
        this.settings = settings;
        this.repository = repository;
        this.log = log;
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

        scheduler = new ReminderScheduler(items, settings, ui);
        scheduler.DueReminders += due =>
            DueReminders?.Invoke(due.Select(TotoApplicationService.ToSnapshot).ToArray());
        scheduler.ScheduledPopup += kind => ScheduledPopup?.Invoke(kind == ScheduledPopupKind.WorkStart
            ? TotoScheduledPopupKind.WorkStart
            : TotoScheduledPopupKind.WorkEnd);
        // 网络下载失败时不能使 Juju/JSON 无法启动；Toto 保留严格日历校验并定时重试。
        retry = new System.Threading.Timer(_ => ui.Post(__ => { if (!disposed) TryStart(); }, null), null,
            Timeout.Infinite, Timeout.Infinite);
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.TimeChanged += TimeChanged;
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

    private bool TryRegisterHotkey(string candidate) => HotkeyChanging?.Invoke(candidate) ?? true;

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

    bool ITotoRuntimeControl.TryRegisterHotkey(string candidate) => TryRegisterHotkey(candidate);

    void ITotoRuntimeControl.SettingsSaved(string hotkey)
    {
        if (ready) scheduler.Reschedule();
        HotkeyChanged?.Invoke(hotkey);
    }

    void ITotoRuntimeControl.Reschedule()
    {
        if (ready) scheduler.Reschedule();
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
