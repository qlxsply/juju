using Microsoft.Win32;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Toto.App.UI;

namespace Toto.App;

internal sealed class TotoApplicationContext : ApplicationContext
{
    private readonly NotifyIcon tray; private readonly ItemRepository items; private readonly SettingsRepository settings; private readonly ReminderScheduler scheduler; private readonly GlobalHotkeyService hotkey = new(); private readonly SynchronizationContext ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext(); private readonly int uiThread = Environment.CurrentManagedThreadId; private MainForm? main;
    public TotoApplicationContext(AppPaths paths, AppLog log, ItemRepository items, SettingsRepository settings, WorkCalendarRepository calendarRepository)
    {
        this.items = items; this.settings = settings; var calendar = new WorkCalendarService(calendarRepository); scheduler = new ReminderScheduler(items, settings, calendar, calendarRepository); scheduler.DueReminders += ShowReminders; scheduler.ScheduledPopup += kind => ShowWorkPopup(kind); tray = new NotifyIcon { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "my_icon.ico")), Text = "toto", Visible = true, ContextMenuStrip = BuildTray(calendarRepository, calendar) }; tray.DoubleClick += (_, _) => ShowMain(); hotkey.Pressed += ShowMain; if (!hotkey.Register(settings.Load().GetValueOrDefault("hotkey", "Ctrl+Alt+Space"))) log.Write("Global hotkey registration failed."); SystemEvents.SessionSwitch += SessionSwitch; SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.Resume) scheduler.Resume(); }; SystemEvents.TimeChanged += (_, _) => scheduler.Resume(); scheduler.Reschedule();
    }
    private ContextMenuStrip BuildTray(WorkCalendarRepository repository, WorkCalendarService calendar) { var menu = new ContextMenuStrip(); menu.Items.Add("打开 toto", null, (_, _) => ShowMain()); menu.Items.Add("新增事项", null, (_, _) => { ShowMain(); main!.ShowEditor(); }); menu.Items.Add("历史事项", null, (_, _) => new HistoryForm(items).Show(main)); menu.Items.Add("设置", null, (_, _) => { using var form = new SettingsForm(settings, scheduler); if (form.ShowDialog(main) == DialogResult.OK) RegisterGlobalHotkey(); }); menu.Items.Add("工作日管理", null, (_, _) => new WorkCalendarForm(repository).ShowDialog(main)); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => ExitThread()); return menu; }
    public void ShowMain() { if (Environment.CurrentManagedThreadId != uiThread) { ui.Post(_ => ShowMain(), null); return; } if (main is null || main.IsDisposed) { main = new MainForm(items, settings, scheduler, RegisterGlobalHotkey); main.FormClosed += (_, _) => main = null; } main.Show(); main.Activate(); main.RefreshItems(); }
    private void RegisterGlobalHotkey() => hotkey.Register(settings.Load().GetValueOrDefault("hotkey", "Ctrl+Alt+Space"));
    private void ShowReminders(IReadOnlyList<TodoItem> due) { using var form = new ReminderForm(items, due, "toto 提醒"); form.ShowDialog(main); main?.RefreshItems(); }
    private void ShowWorkPopup(ScheduledPopupKind kind) { using var form = new ReminderForm(items, items.GetActive(), kind == ScheduledPopupKind.WorkStart ? "toto - 上班事项提醒" : "toto - 下班事项提醒"); form.ShowDialog(main); main?.RefreshItems(); }
    private void SessionSwitch(object? sender, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionLock) scheduler.SetLocked(true); else if (e.Reason == SessionSwitchReason.SessionUnlock) { scheduler.SetLocked(false); scheduler.Resume(); } }
    protected override void ExitThreadCore() { SystemEvents.SessionSwitch -= SessionSwitch; tray.Visible = false; tray.Dispose(); hotkey.Dispose(); scheduler.Dispose(); base.ExitThreadCore(); }
}
