using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.Services;

internal sealed class ReminderScheduler : IDisposable
{
    private readonly ItemRepository items; private readonly SettingsRepository settings; private readonly WorkCalendarService calendar; private readonly WorkCalendarRepository calendarRepository; private readonly System.Threading.Timer timer; private readonly SynchronizationContext context;
    private bool locked; private bool disposed;
    public event Action<IReadOnlyList<TodoItem>>? DueReminders;
    public event Action<ScheduledPopupKind>? ScheduledPopup;
    public ReminderScheduler(ItemRepository items, SettingsRepository settings, WorkCalendarService calendar, WorkCalendarRepository calendarRepository)
    {
        this.items = items; this.settings = settings; this.calendar = calendar; this.calendarRepository = calendarRepository; context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext(); timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void SetLocked(bool value) { locked = value; Reschedule(); }
    public void Reschedule() { if (disposed) return; if (locked) { timer.Change(Timeout.Infinite, Timeout.Infinite); return; } var now = DateTime.Now; var next = new[] { items.GetNextReminder(), NextWorkPopup(now) }.Where(x => x is not null).Select(x => x!.Value).Append(now.AddHours(6)).Min(); var delay = Math.Max(1000, (long)(next - now).TotalMilliseconds); timer.Change(delay, Timeout.Infinite); }
    public void Resume() => Tick();
    private DateTime? NextWorkPopup(DateTime now)
    {
        var values = settings.Load(); var candidates = new List<DateTime>();
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++) { var date = DateOnly.FromDateTime(now.Date.AddDays(dayOffset)); if (!calendar.IsWorkday(date)) continue; foreach (var pair in new[] { ("work_start_popup_enabled", "work_start_time", ScheduledPopupKind.WorkStart), ("work_end_popup_enabled", "work_end_time", ScheduledPopupKind.WorkEnd) }) if (values.GetValueOrDefault(pair.Item1) == "1" && TimeOnly.TryParse(values.GetValueOrDefault(pair.Item2), out var time)) { var due = date.ToDateTime(time); if (due > now && !calendarRepository.WasShown(date, pair.Item3)) candidates.Add(due); } }
        return candidates.Count == 0 ? null : candidates.Min();
    }
    private void Tick()
    {
        if (disposed || locked) return; var now = DateTime.Now; var due = items.MarkDueReminders(now); if (due.Count > 0) context.Post(_ => DueReminders?.Invoke(due), null);
        var values = settings.Load(); var date = DateOnly.FromDateTime(now); if (calendar.IsWorkday(date)) { var candidates = new[] { ("work_start_popup_enabled", "work_start_time", ScheduledPopupKind.WorkStart), ("work_end_popup_enabled", "work_end_time", ScheduledPopupKind.WorkEnd) }.Where(x => values.GetValueOrDefault(x.Item1) == "1" && TimeOnly.TryParse(values.GetValueOrDefault(x.Item2), out var time) && now.TimeOfDay >= time.ToTimeSpan()).OrderBy(x => x.Item3).ToArray(); var candidate = candidates.LastOrDefault(); if (candidate != default && calendarRepository.TryMarkShown(date, candidate.Item3, now)) context.Post(_ => ScheduledPopup?.Invoke(candidate.Item3), null); }
        Reschedule();
    }
    public void Dispose() { disposed = true; timer.Dispose(); }
}
