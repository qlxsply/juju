using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Xunit;

namespace Toto.App.Tests;

public sealed class ItemRepositoryTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "toto-tests", Guid.NewGuid().ToString("N"));
    private readonly ItemRepository repository;

    public ItemRepositoryTests()
    {
        repository = new ItemRepository(new AppPaths(dataDirectory));
        repository.EnsureFiles();
    }

    [Fact]
    public void Add_PreservesExistingActiveAndHistoryItems()
    {
        repository.Add(Item("active", "已有事项"));
        repository.Add(Item("completed", "已完成事项"));
        Assert.True(repository.End("completed", ItemStatus.Completed, "已有备注", DateTime.Now));

        repository.Add(Item("new", "新增事项"));

        var active = repository.GetActive();
        var history = repository.GetHistory(null, 1, 100).Items;
        Assert.Equal(2, active.Count);
        Assert.Contains(active, item => item.Id == "active" && item.Content == "已有事项");
        Assert.Contains(active, item => item.Id == "new" && item.Content == "新增事项" && item.Status == ItemStatus.Active);
        Assert.Single(history);
        Assert.Equal("已完成事项", history[0].Content);
        Assert.Equal("已有备注", history[0].Note);
    }

    [Fact]
    public void End_MovesOnlyTheSpecifiedItemAndKeepsContentSeparateFromNote()
    {
        repository.Add(Item("one", "事项一"));
        repository.Add(Item("two", "事项二"));
        repository.Add(Item("three", "事项三"));

        Assert.True(repository.End("two", ItemStatus.Completed, "完成备注", DateTime.Now));

        var active = repository.GetActive();
        var history = repository.GetHistory(null, 1, 100).Items;
        Assert.Equal(["one", "three"], active.Select(item => item.Id).Order().ToArray());
        var completed = Assert.Single(history);
        Assert.Equal("two", completed.Id);
        Assert.Equal("事项二", completed.Content);
        Assert.Equal("完成备注", completed.Note);
    }

    [Fact]
    public async Task ConcurrentAddsAndReminderUpdate_DoNotLoseOrRewriteItems()
    {
        repository.Add(Item("due", "到期事项", DateTime.Now.AddMinutes(-1)));
        var adds = Enumerable.Range(0, 30)
            .Select(index => Task.Run(() => repository.Add(Item($"item-{index}", $"事项 {index}"))));

        await Task.WhenAll(adds.Append(Task.Run(() => repository.MarkDueReminders(DateTime.Now))));

        var active = repository.GetActive();
        Assert.Equal(31, active.Count);
        Assert.Equal(31, active.Select(item => item.Id).Distinct().Count());
        Assert.Equal(ReminderStatus.Reminded, active.Single(item => item.Id == "due").ReminderStatus);
        Assert.Empty(repository.GetHistory(null, 1, 100).Items);
    }

    [Fact]
    public async Task ConcurrentAddEndAndReminderUpdate_KeepEachOperationScopedToItsItem()
    {
        repository.Add(Item("target", "待完成事项"));
        repository.Add(Item("due", "到期事项", DateTime.Now.AddMinutes(-1)));

        var results = await Task.WhenAll(
            Task.Run(() =>
            {
                repository.Add(Item("new", "并发新增事项"));
                return true;
            }),
            Task.Run(() => repository.End("target", ItemStatus.Completed, "完成备注", DateTime.Now)),
            Task.Run(() =>
            {
                repository.MarkDueReminders(DateTime.Now);
                return true;
            }));

        Assert.All(results, Assert.True);
        var active = repository.GetActive();
        Assert.Equal(["due", "new"], active.Select(item => item.Id).Order().ToArray());
        Assert.Equal("并发新增事项", active.Single(item => item.Id == "new").Content);
        Assert.Equal(ReminderStatus.Reminded, active.Single(item => item.Id == "due").ReminderStatus);
        var completed = Assert.Single(repository.GetHistory(null, 1, 100).Items);
        Assert.Equal("target", completed.Id);
        Assert.Equal("待完成事项", completed.Content);
        Assert.Equal("完成备注", completed.Note);
    }

    [Fact]
    public void Toto_window_icon_is_embedded_in_the_integrated_tool()
    {
        Assert.Contains("Juju.Tools.Toto.toto.ico", typeof(TotoToolHost).Assembly.GetManifestResourceNames());
    }

    [Fact]
    public void Work_calendar_retains_upserted_and_removed_holidays()
    {
        var downloader = new HolidayCalendarDownloadService(new AppPaths(dataDirectory));
        var calendar = new WorkCalendarRepository(downloader);
        var year = DateTime.Today.Year;
        downloader.SaveLocal(year, []);
        var holiday = new HolidayCalendarDay("测试休息日", new DateOnly(year, 6, 1), true);
        calendar.Upsert(holiday);
        Assert.Equal(holiday, calendar.Get(holiday.Date));
        calendar.Delete(holiday.Date);
        Assert.Null(calendar.Get(holiday.Date));
    }

    [Fact]
    public void Toto_global_direct_hotkey_defaults_to_none()
    {
        var repository = new SettingsRepository(new AppPaths(Path.Combine(dataDirectory, "settings")));
        repository.EnsureExists();
        Assert.Equal("None", repository.Load()["hotkey"]);
    }

    [Fact]
    public void Current_active_and_history_csv_keep_their_status_times_and_notes()
    {
        var activeFile = Path.Combine(dataDirectory, "toto_ing.csv");
        var historyFile = Path.Combine(dataDirectory, "toto_end.csv");
        File.WriteAllText(activeFile,
            "事项ID,事项内容,计划时间,提醒时间,创建时间,创建序号,提醒状态,响铃时间,备注\n" +
            "current,当前事项,2026-10-26 16:00:00,2026-10-26 15:55:00,2026-10-22 10:00:00,1,未提醒,,当前备注\n");
        File.WriteAllText(historyFile,
            "事项ID,事项内容,计划时间,提醒时间,创建时间,创建序号,结束状态,结束时间,备注\n" +
            "old,结束事项,,,2026-10-20 08:00:00,2,已取消,2026-10-21 09:00:00,结束备注\n");

        var current = Assert.Single(repository.GetActive());
        Assert.Equal(new DateTime(2026, 10, 26, 15, 55, 0), current.RemindAt);
        Assert.Equal(ReminderStatus.Pending, current.ReminderStatus);
        Assert.Equal("当前备注", current.Note);
        var history = Assert.Single(repository.GetHistory(null, 1, 10).Items);
        Assert.Equal(ItemStatus.Cancelled, history.Status);
        Assert.Equal(new DateTime(2026, 10, 21, 9, 0, 0), history.EndedAt);
        Assert.Equal("结束备注", history.Note);
    }

    [Fact]
    public void Settings_are_persisted_as_json_and_unknown_setting_keys_are_not_loaded()
    {
        var settings = new SettingsRepository(new AppPaths(dataDirectory));
        settings.EnsureExists();
        var path = Path.Combine(dataDirectory, "config.json");
        File.WriteAllText(path,
            "{\"settings\":{\"hotkey\":\"None\",\"shortcut_settings\":\"Alt+S\"},\"scheduledPopups\":{},\"archiveProgressMonth\":\"\"}");

        var values = settings.Load();
        Assert.False(values.ContainsKey("shortcut_settings"));
        Assert.Equal("Alt+A", values["shortcut_add"]);
        Assert.Equal("", settings.ArchiveProgressMonth);
        Assert.Contains("archiveProgressMonth", File.ReadAllText(path));
    }

    [Fact]
    public void Archive_progress_month_is_persisted_in_json()
    {
        var paths = new AppPaths(Path.Combine(dataDirectory, "archive-settings"));
        var settings = new SettingsRepository(paths);
        settings.EnsureExists();

        settings.SaveArchiveProgressMonth("202609");

        Assert.Equal("202609", settings.ArchiveProgressMonth);
        Assert.Contains("\"archiveProgressMonth\": \"202609\"", File.ReadAllText(paths.ConfigPath));
    }

    [Fact]
    public void Startup_archives_through_previous_month_and_records_progress()
    {
        var paths = new AppPaths(dataDirectory);
        var cutoff = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        var oldMonth = cutoff.AddMonths(-1);
        EndAt("startup-archive", oldMonth.AddDays(2).ToDateTime(new TimeOnly(12, 0)));
        var calendar = new HolidayCalendarDownloadService(paths);
        calendar.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendar.SaveLocal(DateTime.Today.Year + 1, []);

        using var host = new TotoToolHost(new SynchronizationContext(), paths);

        Assert.Equal(cutoff.ToString("yyyyMM"), host.Settings.ArchiveProgressMonth);
        Assert.Single(CsvFile.Read(paths.HistoryArchiveCsvPath(oldMonth.Year * 100 + oldMonth.Month)).Skip(1));
        Assert.Single(CsvFile.Read(paths.HistoryCsvPath));
    }

    [Fact]
    public void Changing_daily_review_time_clears_only_today_mark_when_new_time_is_still_future()
    {
        var settings = new SettingsRepository(new AppPaths(Path.Combine(dataDirectory, "daily-review-reschedule")));
        settings.EnsureExists();
        var initial = settings.Load().ToDictionary(item => item.Key, item => item.Value);
        initial["daily_review_start_time"] = "16:40";
        initial["daily_review_end_time"] = "16:38";
        settings.Save(initial);
        var now = new DateTime(2026, 10, 25, 16, 50, 0);
        var today = DateOnly.FromDateTime(now);
        Assert.True(settings.TryMarkScheduledPopupShown(today, ScheduledPopupKind.WorkStart, now));
        Assert.True(settings.TryMarkScheduledPopupShown(today, ScheduledPopupKind.WorkEnd, now));

        var changed = settings.Load().ToDictionary(item => item.Key, item => item.Value);
        changed["daily_review_start_time"] = "16:30";
        changed["daily_review_end_time"] = "18:00";
        settings.SaveDailyReviewSettings(changed, now);

        Assert.True(settings.WasScheduledPopupShown(today, ScheduledPopupKind.WorkStart));
        Assert.False(settings.WasScheduledPopupShown(today, ScheduledPopupKind.WorkEnd));
    }

    [Theory]
    [InlineData("Alt+A", "Alt+A")]
    [InlineData("alt+ctrl+a", "Ctrl+Alt+A")]
    [InlineData("Alt+NumPad7", "Alt+NumPad7")]
    [InlineData("Alt+OemPeriod", "Alt+OemPeriod")]
    [InlineData("Alt+PageUp", "Alt+PageUp")]
    public void Local_shortcuts_preserve_existing_modifier_and_numpad_bindings(string value, string expected)
    {
        Assert.True(WindowShortcut.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public async Task Scheduler_marks_a_due_reminder_once_without_an_open_window()
    {
        var paths = new AppPaths(dataDirectory);
        var calendar = new HolidayCalendarDownloadService(paths);
        calendar.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendar.SaveLocal(DateTime.Today.Year + 1, []);
        using var host = new TotoToolHost(new SynchronizationContext(), paths);
        Assert.True(host.EnsureReady());
        host.Items.Add(Item("due", "需要提醒", DateTime.Now.AddMinutes(-1)));
        var notifications = new List<TodoItem>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.DueReminders += due =>
        {
            lock (notifications) notifications.AddRange(due);
            delivered.TrySetResult();
        };
        host.Reschedule();
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Resume();
        lock (notifications) Assert.Single(notifications);
        Assert.Equal(ReminderStatus.Reminded, host.Items.GetActive().Single().ReminderStatus);
        Assert.Empty(host.Items.GetHistory(null, 1, 10).Items);
    }

    [Fact]
    public void History_filters_and_paginates_existing_csv_records()
    {
        var endedAt = new DateTime(2026, 10, 22, 12, 0, 0);
        for (var index = 0; index < 15; index++)
        {
            var id = $"historical-{index}";
            repository.Add(Item(id, index % 2 == 0 ? "项目会议" : "开发事项"));
            Assert.True(repository.End(id, index % 2 == 0 ? ItemStatus.Completed : ItemStatus.Cancelled,
                index % 2 == 0 ? "会议记录" : "开发记录", endedAt.AddMinutes(index)));
        }
        Assert.Equal(10, repository.GetHistory(null, 1, 10).Items.Count);
        Assert.Equal(5, repository.GetHistory(null, 2, 10).Items.Count);
        var meetings = repository.GetHistory(new QueryCriteria
        {
            Content = "会议", Note = "会议记录", Status = ItemStatus.Completed,
            EndedFrom = endedAt.Date, EndedTo = endedAt.Date.AddDays(1)
        }, 1, 10);
        Assert.Equal(8, meetings.TotalCount);
        Assert.All(meetings.Items, item => Assert.Equal(ItemStatus.Completed, item.Status));
    }

    [Fact]
    public void Archive_moves_records_through_cutoff_month_and_history_queries_all_months()
    {
        EndAt("aug-late", new DateTime(2026, 8, 30, 12, 0, 0));
        EndAt("aug-early", new DateTime(2026, 8, 2, 12, 0, 0));
        EndAt("sep", new DateTime(2026, 9, 15, 12, 0, 0));
        EndAt("oct", new DateTime(2026, 10, 2, 12, 0, 0));

        repository.ArchiveHistoryThrough(new DateOnly(2026, 9, 1));

        Assert.Equal(["aug-early", "aug-late"], CsvFile.Read(new AppPaths(dataDirectory)
            .HistoryArchiveCsvPath(202608)).Skip(1).Select(row => row[0]).ToArray());
        Assert.Equal(["sep"], CsvFile.Read(new AppPaths(dataDirectory)
            .HistoryArchiveCsvPath(202609)).Skip(1).Select(row => row[0]).ToArray());
        Assert.Equal(["oct"], CsvFile.Read(new AppPaths(dataDirectory).HistoryCsvPath)
            .Skip(1).Select(row => row[0]).ToArray());

        var history = repository.GetHistory(null, 1, 10);
        Assert.Equal(4, history.TotalCount);
        Assert.Equal(["oct", "sep", "aug-late", "aug-early"], history.Items.Select(item => item.Id).ToArray());
        var august = repository.GetHistory(new QueryCriteria
        {
            EndedFrom = new DateTime(2026, 8, 1), EndedTo = new DateTime(2026, 9, 1)
        }, 1, 10);
        Assert.Equal(["aug-late", "aug-early"], august.Items.Select(item => item.Id).ToArray());
    }

    private void EndAt(string id, DateTime endedAt)
    {
        repository.Add(Item(id, id));
        Assert.True(repository.End(id, ItemStatus.Completed, "", endedAt));
    }

    [Fact]
    public async Task Startup_resumes_an_existing_due_reminder_without_opening_a_window()
    {
        var paths = new AppPaths(dataDirectory);
        var calendar = new HolidayCalendarDownloadService(paths);
        calendar.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendar.SaveLocal(DateTime.Today.Year + 1, []);
        repository.Add(Item("restored", "重启后到期", DateTime.Now.AddMinutes(-1)));
        using var host = new TotoToolHost(new SynchronizationContext(), paths);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.DueReminders += due =>
        {
            if (due.Any(item => item.Id == "restored")) delivered.TrySetResult();
        };

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ReminderStatus.Reminded, repository.GetActive().Single().ReminderStatus);
        Assert.Null(repository.GetNextReminder());
    }

    [Fact]
    public async Task Daily_review_is_recorded_once_across_scheduler_resumes()
    {
        var paths = new AppPaths(dataDirectory);
        var calendar = new HolidayCalendarDownloadService(paths);
        var today = DateOnly.FromDateTime(DateTime.Today);
        calendar.SaveLocal(today.Year, []);
        if (DateTime.Today.AddDays(7).Year != today.Year)
            calendar.SaveLocal(today.Year + 1, []);
        using var host = new TotoToolHost(new SynchronizationContext(), paths);
        var values = host.ToolSettings.ToDictionary(item => item.Key, item => item.Value);
        values["daily_review_start_time"] = "00:00";
        values["daily_review_workdays"] = "0";
        host.SaveToolSettings(values);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        host.ScheduledPopup += kind =>
        {
            Interlocked.Increment(ref count);
            shown.TrySetResult();
        };
        host.Resume();
        await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Resume();
        Assert.Equal(1, count);
        Assert.True(host.Settings.WasScheduledPopupShown(today, ScheduledPopupKind.WorkStart));
    }

    [Fact]
    public async Task Start_and_end_daily_reviews_are_independent_when_both_are_due()
    {
        var paths = new AppPaths(Path.Combine(dataDirectory, "independent-daily-reviews"));
        var calendar = new HolidayCalendarDownloadService(paths);
        calendar.SaveLocal(DateTime.Today.Year, []);
        if (DateTime.Today.AddDays(7).Year != DateTime.Today.Year)
            calendar.SaveLocal(DateTime.Today.Year + 1, []);
        using var host = new TotoToolHost(new SynchronizationContext(), paths);
        var values = host.ToolSettings.ToDictionary(item => item.Key, item => item.Value);
        values["daily_review_start_time"] = "00:00";
        values["daily_review_end_time"] = "00:00";
        values["daily_review_workdays"] = "0";
        host.SaveToolSettings(values);

        var kinds = new System.Collections.Concurrent.ConcurrentBag<ScheduledPopupKind>();
        var bothDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ScheduledPopup += kind =>
        {
            kinds.Add(kind);
            if (kinds.Count >= 2) bothDelivered.TrySetResult();
        };
        host.Resume();
        await bothDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var today = DateOnly.FromDateTime(DateTime.Today);
        Assert.Contains(ScheduledPopupKind.WorkStart, kinds);
        Assert.Contains(ScheduledPopupKind.WorkEnd, kinds);
        Assert.True(host.Settings.WasScheduledPopupShown(today, ScheduledPopupKind.WorkStart));
        Assert.True(host.Settings.WasScheduledPopupShown(today, ScheduledPopupKind.WorkEnd));
        host.Resume();
        await Task.Delay(100);
        Assert.Equal(2, kinds.Count);
    }

    private static TodoItem Item(string id, string content, DateTime? remindAt = null) =>
        new(id, content, null, remindAt, DateTime.Now, 0, ItemStatus.Active,
            remindAt is null ? ReminderStatus.None : ReminderStatus.Pending, null, null, "");

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
    }
}
