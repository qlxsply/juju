using Juju.Core.Contracts.Platform;
using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Domain;
using Microsoft.Extensions.Logging;

namespace Juju.Tools.Toto.Application.Reminders;

/// <summary>平台无关的 Toto 后台提醒运行时；由 App 模块启动并负责将事件派发到 UI。</summary>
public sealed class TotoReminderRuntime : IDisposable, ITotoRuntimeControl
{
    private readonly ITotoItemRepository _items;
    private readonly ITotoSettingsRepository _settings;
    private readonly ITotoCalendarRepository _calendar;
    private readonly ISystemLifecycleEvents _systemEvents;
    private readonly ReminderScheduler _scheduler;
    private readonly ILogger<TotoReminderRuntime> _log;
    private readonly Timer _retry;
    private bool _started;
    private bool _disposed;
    private bool _ready;
    private Exception? _initializationError;

    internal TotoReminderRuntime(ITotoItemRepository items, ITotoSettingsRepository settings,
        ITotoCalendarRepository calendar, ISystemLifecycleEvents systemEvents,
        ILogger<TotoReminderRuntime> log)
    {
        _items = items;
        _settings = settings;
        _calendar = calendar;
        _systemEvents = systemEvents;
        _log = log;
        _scheduler = new ReminderScheduler(items, settings);
        _scheduler.DueReminders += due => DueReminders?.Invoke(due.Select(TotoApplicationService.ToSnapshot).ToArray());
        _scheduler.ScheduledPopup += kind => ScheduledPopup?.Invoke(kind == ScheduledPopupKind.WorkStart
            ? TotoScheduledPopupKind.WorkStart
            : TotoScheduledPopupKind.WorkEnd);
        _retry = new Timer(_ => { if (!_disposed) EnsureReady(); }, null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event Action<string>? HotkeyChanged;
    public event Func<string, bool>? HotkeyChanging;
    public event Action<IReadOnlyList<TotoItemSnapshot>>? DueReminders;
    public event Action<TotoScheduledPopupKind>? ScheduledPopup;

    public bool CalendarReady => _ready;
    public string? InitializationError => _initializationError?.Message;
    internal void Reschedule() { if (_ready) _scheduler.Reschedule(); }
    internal void Resume() { if (_ready) _scheduler.Resume(); }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) throw new ObjectDisposedException(nameof(TotoReminderRuntime));
        if (_started) return Task.CompletedTask;

        _items.EnsureFiles();
        _settings.EnsureExists();
        ArchivePreviousMonths();
        _systemEvents.Changed += OnSystemLifecycleChanged;
        _started = true;
        EnsureReady();
        return Task.CompletedTask;
    }

    public bool EnsureReady() => TryStart();

    private void ArchivePreviousMonths()
    {
        var cutoffMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var cutoffText = cutoffMonth.ToString("yyyyMM", System.Globalization.CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(_settings.ArchiveProgressMonth) &&
            string.CompareOrdinal(_settings.ArchiveProgressMonth, cutoffText) >= 0) return;

        try
        {
            _items.ArchiveHistoryThrough(cutoffMonth);
            _settings.SaveArchiveProgressMonth(cutoffText);
        }
        catch (Exception error)
        {
            _log.LogError(error, "Toto history archive failed; it will be retried on the next start");
        }
    }

    private bool TryStart()
    {
        if (_disposed) return false;
        if (_ready) return true;
        try
        {
            _calendar.GetYear(DateTime.Today.Year);
            var end = DateTime.Today.AddDays(7);
            if (end.Year != DateTime.Today.Year) _calendar.GetYear(end.Year);
            _scheduler.Reschedule();
            _ready = true;
            _initializationError = null;
            _retry.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return true;
        }
        catch (Exception error)
        {
            _initializationError = error;
            _log.LogError(error, "Toto calendar initialization failed; retry scheduled");
            _retry.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
            return false;
        }
    }

    private void OnSystemLifecycleChanged(SystemLifecycleSignal signal)
    {
        switch (signal)
        {
            case SystemLifecycleSignal.SessionLocked:
                _scheduler.SetLocked(true);
                break;
            case SystemLifecycleSignal.SessionUnlocked:
                _scheduler.SetLocked(false);
                if (TryStart()) _scheduler.Resume();
                break;
            case SystemLifecycleSignal.Resumed:
            case SystemLifecycleSignal.TimeChanged:
                if (TryStart()) _scheduler.Resume();
                break;
        }
    }

    bool ITotoRuntimeControl.TryRegisterHotkey(string candidate) => HotkeyChanging?.Invoke(candidate) ?? true;

    void ITotoRuntimeControl.SettingsSaved(string hotkey)
    {
        if (_ready) _scheduler.Reschedule();
        HotkeyChanged?.Invoke(hotkey);
    }

    void ITotoRuntimeControl.Reschedule()
    {
        if (_ready) _scheduler.Reschedule();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started) _systemEvents.Changed -= OnSystemLifecycleChanged;
        _retry.Dispose();
        _scheduler.Dispose();
    }
}
