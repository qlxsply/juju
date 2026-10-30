using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Calendar;
using Juju.Tools.Toto.Application.Items;
using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Infrastructure.Calendar;
using Juju.Tools.Toto.Infrastructure.Items;
using Juju.Tools.Toto.Infrastructure.Settings;
using Juju.Tools.Toto.Infrastructure.Storage;
using Toto.App.Domain;
using Xunit;

namespace Toto.App.Tests;

public sealed class TotoApplicationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toto-application-tests", Guid.NewGuid().ToString("N"));
    private readonly TotoPaths _paths;
    private readonly ItemRepository _items;
    private readonly SettingsRepository _settings;
    private readonly WorkCalendarRepository _calendar;
    private readonly RecordingRuntimeControl _runtime = new();
    private readonly TotoApplicationService _service;

    public TotoApplicationServiceTests()
    {
        _paths = new TotoPaths(_root);
        _items = new ItemRepository(_paths);
        _settings = new SettingsRepository(_paths);
        _calendar = new WorkCalendarRepository(new HolidayCalendarDownloadService(_paths));
        _items.EnsureFiles();
        _settings.EnsureExists();
        _service = new TotoApplicationService(_items, _settings, _calendar, _runtime);
    }

    [Fact]
    public void Item_use_cases_preserve_repository_lifecycle_and_history_paging()
    {
        var planned = DateTime.Today.AddHours(16);
        _service.AddItem(new NewTotoItem("准备评审", planned, planned.AddMinutes(-5), "待确认"));
        var added = Assert.Single(_service.GetActiveItems());
        Assert.Equal("准备评审", added.Content);
        Assert.Equal(1, added.CreatedSeq);
        Assert.Equal(TotoItemStatus.Active, added.Status);
        Assert.Equal(TotoReminderStatus.Pending, added.ReminderStatus);
        Assert.Equal(1, _runtime.RescheduleCount);

        Assert.True(_service.UpdateItem(added with
        {
            Content = "完成评审准备",
            RemindAt = planned.AddMinutes(-10),
            ReminderStatus = TotoReminderStatus.Pending,
            RemindedAt = null
        }));
        var updated = Assert.Single(_service.GetActiveItems());
        Assert.Equal("完成评审准备", updated.Content);
        Assert.Equal(planned.AddMinutes(-10), updated.RemindAt);

        Assert.True(_service.EndItem(updated.Id, TotoItemStatus.Completed, "已确认", DateTime.Now));
        Assert.Empty(_service.GetActiveItems());
        var history = _service.GetHistory(new TotoHistoryQuery { Status = TotoItemStatus.Completed }, 1, 10);
        var completed = Assert.Single(history.Items);
        Assert.Equal("已确认", completed.Note);
        Assert.Equal(1, history.TotalCount);
        Assert.False(_service.EndItem(updated.Id, TotoItemStatus.Cancelled, "", DateTime.Now));
        Assert.Equal(3, _runtime.RescheduleCount);
    }

    [Fact]
    public void Settings_save_validates_and_notifies_runtime_only_after_persistence_succeeds()
    {
        var changed = "";
        _service.HotkeyChanged += value => changed = value;
        var values = _service.Settings.ToDictionary(pair => pair.Key, pair => pair.Value);
        values["hotkey"] = "Ctrl+Alt+T";

        _service.SaveSettings(values);

        Assert.Equal("Ctrl+Alt+T", _service.GlobalHotkey);
        Assert.Equal("Ctrl+Alt+T", changed);
        Assert.Equal("Ctrl+Alt+T", _runtime.LastRegisteredHotkey);

        _runtime.AllowHotkey = false;
        values["hotkey"] = "Ctrl+Alt+J";
        Assert.Throws<InvalidOperationException>(() => _service.SaveSettings(values));
        Assert.Equal("Ctrl+Alt+T", _service.GlobalHotkey);
    }

    [Fact]
    public void Calendar_use_cases_delegate_to_the_existing_calendar_repository()
    {
        var year = DateTime.Today.Year;
        var holiday = new TotoHolidaySnapshot("应用服务测试日", new DateOnly(year, 6, 1), true);
        new HolidayCalendarDownloadService(_paths).SaveLocal(year, []);

        _service.SaveHoliday(holiday);

        Assert.Contains(holiday, _service.GetHolidays(year));
        _service.DeleteHoliday(holiday.Date);
        Assert.DoesNotContain(_service.GetHolidays(year), item => item.Date == holiday.Date);
        Assert.Equal(DateTime.Today >= new DateTime(year, 12, 1) ? year + 1 : year, _service.LatestCalendarYear);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingRuntimeControl : ITotoRuntimeControl
    {
        public bool CalendarReady { get; set; } = true;
        public bool AllowHotkey { get; set; } = true;
        public string LastRegisteredHotkey { get; private set; } = "None";
        public int RescheduleCount { get; private set; }
        public event Action<string>? HotkeyChanged;
        public event Func<string, bool>? HotkeyChanging;

        public bool TryRegisterHotkey(string candidate)
        {
            var accepted = HotkeyChanging?.Invoke(candidate) ?? AllowHotkey;
            if (accepted) LastRegisteredHotkey = candidate;
            return accepted;
        }

        public void SettingsSaved(string hotkey)
        {
            Reschedule();
            HotkeyChanged?.Invoke(hotkey);
        }

        public void Reschedule() => RescheduleCount++;
    }
}
