using Juju.Core.Contracts.Platform;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Reminders;
using Juju.Tools.Toto.Domain;
using Juju.Tools.Toto.Infrastructure.Calendar;
using Juju.Tools.Toto.Infrastructure.Items;
using Juju.Tools.Toto.Infrastructure.Settings;
using Juju.Tools.Toto.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Juju.Tools.Toto.Tests;

public sealed class TotoReminderRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toto-runtime-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Lock_pauses_due_reminders_and_unlock_rechecks_immediately()
    {
        var paths = new TotoPaths(_root);
        var items = new ItemRepository(paths);
        var settings = new SettingsRepository(paths);
        var calendars = new HolidayCalendarDownloadService(paths);
        items.EnsureFiles();
        settings.EnsureExists();
        calendars.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendars.SaveLocal(DateTime.Today.Year + 1, []);
        items.Add(new TodoItem("due", "锁屏期间到期", null, DateTime.Now.AddMinutes(-1), DateTime.Now, 0,
            ItemStatus.Active, ReminderStatus.Pending, null, null, ""));

        var systemEvents = new FakeSystemLifecycleEvents();
        using var runtime = new TotoReminderRuntime(items, settings, new WorkCalendarRepository(calendars),
            systemEvents, NullLogger<TotoReminderRuntime>.Instance);
        var delivered = new TaskCompletionSource<IReadOnlyList<TotoItemSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DueReminders += due => delivered.TrySetResult(due);
        await runtime.StartAsync();
        systemEvents.Raise(SystemLifecycleSignal.SessionLocked);

        await Task.Delay(1200);

        Assert.False(delivered.Task.IsCompleted);
        systemEvents.Raise(SystemLifecycleSignal.SessionUnlocked);
        var dueItems = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("due", Assert.Single(dueItems).Id);
        Assert.Equal(ReminderStatus.Reminded, Assert.Single(items.GetActive()).ReminderStatus);
    }

    [Fact]
    public async Task Runtime_subscribes_once_and_releases_the_platform_lifecycle_subscription()
    {
        var paths = new TotoPaths(_root);
        var items = new ItemRepository(paths);
        var settings = new SettingsRepository(paths);
        var calendars = new HolidayCalendarDownloadService(paths);
        calendars.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendars.SaveLocal(DateTime.Today.Year + 1, []);
        var systemEvents = new FakeSystemLifecycleEvents();
        var runtime = new TotoReminderRuntime(items, settings, new WorkCalendarRepository(calendars),
            systemEvents, NullLogger<TotoReminderRuntime>.Instance);

        await runtime.StartAsync();
        await runtime.StartAsync();
        Assert.Equal(1, systemEvents.SubscriberCount);

        runtime.Dispose();

        Assert.Equal(0, systemEvents.SubscriberCount);
    }

    [Fact]
    public async Task Resume_and_time_change_signals_immediately_recheck_due_items()
    {
        var paths = new TotoPaths(_root);
        var items = new ItemRepository(paths);
        var settings = new SettingsRepository(paths);
        var calendars = new HolidayCalendarDownloadService(paths);
        items.EnsureFiles();
        settings.EnsureExists();
        calendars.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendars.SaveLocal(DateTime.Today.Year + 1, []);
        var systemEvents = new FakeSystemLifecycleEvents();
        using var runtime = new TotoReminderRuntime(items, settings, new WorkCalendarRepository(calendars),
            systemEvents, NullLogger<TotoReminderRuntime>.Instance);
        await runtime.StartAsync();

        items.Add(new TodoItem("resume", "恢复后检查", null, DateTime.Now.AddMinutes(-1), DateTime.Now, 0,
            ItemStatus.Active, ReminderStatus.Pending, null, null, ""));
        var resumed = new TaskCompletionSource<IReadOnlyList<TotoItemSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DueReminders += due =>
        {
            if (due.Any(item => item.Id == "resume")) resumed.TrySetResult(due);
        };
        systemEvents.Raise(SystemLifecycleSignal.Resumed);
        Assert.Contains(await resumed.Task.WaitAsync(TimeSpan.FromSeconds(2)), item => item.Id == "resume");

        items.Add(new TodoItem("time-change", "改时后检查", null, DateTime.Now.AddMinutes(-1), DateTime.Now, 0,
            ItemStatus.Active, ReminderStatus.Pending, null, null, ""));
        var timeChanged = new TaskCompletionSource<IReadOnlyList<TotoItemSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.DueReminders += due =>
        {
            if (due.Any(item => item.Id == "time-change")) timeChanged.TrySetResult(due);
        };
        systemEvents.Raise(SystemLifecycleSignal.TimeChanged);

        Assert.Contains(await timeChanged.Task.WaitAsync(TimeSpan.FromSeconds(2)), item => item.Id == "time-change");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeSystemLifecycleEvents : ISystemLifecycleEvents
    {
        public event Action<SystemLifecycleSignal>? Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        public void Raise(SystemLifecycleSignal signal) => Changed?.Invoke(signal);
    }
}
