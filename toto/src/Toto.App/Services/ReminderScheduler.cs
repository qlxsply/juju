using Toto.App.Data;
using Toto.App.Domain;
using Timer = System.Threading.Timer;

namespace Toto.App.Services;

/// <summary>计算下一次提醒或工作日弹窗，并在 UI 同步上下文中通知订阅者。</summary>
/// <remarks>计时器和同步上下文属于需释放或线程关联的资源；关闭时必须调用 <see cref="Dispose"/>。</remarks>
internal sealed class ReminderScheduler : IDisposable
{
    private readonly ItemRepository items;
    private readonly SettingsRepository settings;
    private readonly WorkCalendarService calendar;
    private readonly Timer timer;
    private readonly SynchronizationContext context;
    private bool locked;
    private bool disposed;
    /// <summary>当一个或多个事项到达提醒时间时触发。</summary>
    // event 仅允许类内部触发；订阅者使用 +=/-=，这比 Java 公开监听器集合更受封装保护。
    public event Action<IReadOnlyList<TodoItem>>? DueReminders;
    /// <summary>当工作日计划弹窗到期时触发。</summary>
    public event Action<ScheduledPopupKind>? ScheduledPopup;

    /// <summary>创建调度器并捕获当前 UI 同步上下文，用于将通知切回 UI 线程。</summary>
    public ReminderScheduler(ItemRepository items, SettingsRepository settings, WorkCalendarService calendar)
    {
        this.items = items;
        this.settings = settings;
        this.calendar = calendar;
        context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        // Timer 回调在线程池线程执行，不能直接操作 WinForms 控件；后续通过 context.Post 切回 UI 线程。
        timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>设置锁定状态；锁定时停止所有已安排的计时器回调。</summary>
    public void SetLocked(bool value)
    {
        locked = value;
        Reschedule();
    }

    /// <summary>根据下一次待提醒事项、工作日弹窗和兜底唤醒时间安排一次回调。</summary>
    public void Reschedule()
    {
        if (disposed) return;
        if (locked)
        {
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var now = DateTime.Now;
        var next = new[] { items.GetNextReminder(), NextWorkPopup(now) }.Where(x => x is not null).Select(x => x!.Value)
            .Append(now.AddHours(6)).Min();
        var delay = Math.Max(1000, (long)(next - now).TotalMilliseconds);
        timer.Change(delay, Timeout.Infinite);
    }

    /// <summary>立即执行一次检查，通常用于从暂停状态恢复。</summary>
    public void Resume() => Tick();

    /// <summary>查找未来七天内最早尚未显示的工作日计划弹窗时间。</summary>
    private DateTime? NextWorkPopup(DateTime now)
    {
        var values = settings.Load();
        var candidates = new List<DateTime>();
        for (var dayOffset = 0; dayOffset <= 7; dayOffset++)
        {
            var date = DateOnly.FromDateTime(now.Date.AddDays(dayOffset));
            if (!calendar.IsWorkday(date)) continue;
            foreach (var pair in new[]
                     {
                         ("work_start_popup_enabled", "work_start_time", ScheduledPopupKind.WorkStart),
                         ("work_end_popup_enabled", "work_end_time", ScheduledPopupKind.WorkEnd)
                     })
                if (values.GetValueOrDefault(pair.Item1) == "1" &&
                    TimeOnly.TryParse(values.GetValueOrDefault(pair.Item2), out var time))
                {
                    var due = date.ToDateTime(time);
                    if (due > now && !settings.WasScheduledPopupShown(date, pair.Item3)) candidates.Add(due);
                }
        }

        return candidates.Count == 0 ? null : candidates.Min();
    }

    /// <summary>处理到期提醒和计划弹窗，然后安排下一次检查。</summary>
    private void Tick()
    {
        if (disposed || locked) return;
        var now = DateTime.Now;
        var due = items.MarkDueReminders(now);
        if (due.Count > 0) context.Post(_ => DueReminders?.Invoke(due), null);
        var values = settings.Load();
        var date = DateOnly.FromDateTime(now);
        if (calendar.IsWorkday(date))
        {
            var candidates =
                new[]
                {
                    ("work_start_popup_enabled", "work_start_time", ScheduledPopupKind.WorkStart),
                    ("work_end_popup_enabled", "work_end_time", ScheduledPopupKind.WorkEnd)
                }.Where(x =>
                    values.GetValueOrDefault(x.Item1) == "1" &&
                    TimeOnly.TryParse(values.GetValueOrDefault(x.Item2), out var time) &&
                    now.TimeOfDay >= time.ToTimeSpan()).OrderBy(x => x.Item3).ToArray();
            var candidate = candidates.LastOrDefault();
            if (candidate != default && settings.TryMarkScheduledPopupShown(date, candidate.Item3, now))
                context.Post(_ => ScheduledPopup?.Invoke(candidate.Item3), null);
        }

        Reschedule();
    }

    /// <summary>停止并释放底层线程池计时器。</summary>
    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
    }
}
