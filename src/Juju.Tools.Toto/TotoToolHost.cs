using Microsoft.Win32;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Toto.App.UI;

namespace Toto.App;

/// <summary>
/// 将原独立 WinForms 应用适配为 Juju 内的常驻工具。只保留一份提醒调度器和一组窗体；
/// Juju 负责进程、托盘、单实例、开机启动和全局热键。
/// 必须在 Juju 的 UI 线程构造和释放本对象，因为 WinForms 窗体和系统事件依赖该线程。
/// </summary>
public sealed class TotoToolHost : IDisposable
{
    private readonly ItemRepository items;
    private readonly SettingsRepository settings;
    private readonly WorkCalendarRepository repository;
    private readonly WorkCalendarService calendar;
    private readonly ReminderScheduler scheduler;
    private readonly AppLog log;
    private readonly Func<bool, Task> setStartup;
    private readonly Func<bool> getStartup;
    private readonly List<ReminderForm> reminders = [];
    private readonly List<Form> auxiliaryForms = [];
    private readonly SynchronizationContext ui;
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    private readonly System.Threading.Timer retry;
    private MainForm? main;
    private bool disposed;
    private bool ready;
    private Exception? initializationError;

    public event Action<string>? HotkeyChanged;
    public event Action? MainWindowHidden;
    // 宿主先尝试注册候选热键；失败时设置页不落盘，避免下次启动丢失旧快捷键。
    public event Func<string, bool>? HotkeyChanging;
    public string Hotkey => settings.Load().GetValueOrDefault("hotkey", "Ctrl+Alt+Space");
    public bool MigratedAutoStartEnabled { get; }
    private bool TryRegisterHotkey(string candidate) => HotkeyChanging?.Invoke(candidate) ?? true;

    public TotoToolHost(Func<bool, Task> setStartup, Func<bool> getStartup)
    {
        this.setStartup = setStartup;
        this.getStartup = getStartup;
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var paths = new AppPaths();
        log = new AppLog(paths.LogDirectory);
        var importedSettings = ImportLegacyData(paths);
        items = new ItemRepository(paths);
        settings = new SettingsRepository(paths);
        items.EnsureFiles();
        settings.EnsureExists();
        MigratedAutoStartEnabled = importedSettings && settings.Load().GetValueOrDefault("auto_start") == "1";
        if (importedSettings) RemoveLegacyStartupShortcut();
        repository = new WorkCalendarRepository(new HolidayCalendarDownloadService(paths));
        calendar = new WorkCalendarService(repository);
        scheduler = new ReminderScheduler(items, settings, calendar);
        scheduler.DueReminders += ShowReminders;
        scheduler.ScheduledPopup += ShowWorkPopup;
        // 网络下载失败时不能使 Juju/JSON 无法启动；Toto 保留严格日历校验并定时重试。
        retry = new System.Threading.Timer(_ => ui.Post(__ => { if (!disposed) TryStart(); }, null), null,
            Timeout.Infinite, Timeout.Infinite);
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += PowerModeChanged;
        SystemEvents.TimeChanged += TimeChanged;
        Application.ThreadException += ThreadException;
        TryStart();
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

    /// <summary>从 Juju Launcher、托盘或工具专属全局热键直接打开原 Toto 主窗口。</summary>
    public void ShowMain()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != uiThread)
        {
            ui.Post(_ => ShowMain(), null);
            return;
        }
        if (!TryStart())
        {
            MessageBox.Show($"节假日日历尚未就绪，Toto 将继续重试：\n{initializationError?.Message}", "Toto",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (main is null || main.IsDisposed)
        {
            main = new MainForm(items, settings, scheduler, OnSettingsSaved, setStartup, getStartup,
                TryRegisterHotkey);
            main.FormClosed += (_, _) => main = null;
            main.VisibleChanged += (_, _) =>
            {
                if (main is { Visible: false } && !disposed) MainWindowHidden?.Invoke();
            };
        }
        main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
        main.RefreshItems();
    }

    /// <summary>在主窗口尚未打开时也可从 Juju 托盘进入原有新增、历史、设置和工作日功能。</summary>
    public void ShowNewItem() { ShowMain(); main?.ShowEditor(); }
    public void ShowHistory()
    {
        if (TryStart()) ShowAuxiliary(new HistoryForm(items, settings), main);
        else ShowMain();
    }
    public void ShowSettings()
    {
        if (!TryStart()) { ShowMain(); return; }
        var form = new SettingsForm(settings, scheduler, setStartup, getStartup, TryRegisterHotkey);
        form.SettingsSaved += OnSettingsSaved;
        ShowAuxiliary(form);
    }
    public void ShowCalendar()
    {
        if (TryStart()) ShowAuxiliary(new WorkCalendarForm(repository, settings));
        else ShowMain();
    }

    private void ShowAuxiliary(Form form, Form? owner = null)
    {
        auxiliaryForms.Add(form);
        form.FormClosed += (_, _) => auxiliaryForms.Remove(form);
        if (owner is { IsDisposed: false, Visible: true }) form.Show(owner);
        else form.Show();
    }

    private void OnSettingsSaved() => HotkeyChanged?.Invoke(Hotkey);

    private void ShowReminders(IReadOnlyList<TodoItem> due) =>
        ShowReminder(new ReminderForm(items, due, settings, "toto 提醒"));

    private void ShowWorkPopup(ScheduledPopupKind kind) =>
        ShowReminder(new ReminderForm(items, items.GetActive(), settings,
            kind == ScheduledPopupKind.WorkStart ? "toto - 上班事项提醒" : "toto - 下班事项提醒"));

    private void ShowReminder(ReminderForm form)
    {
        reminders.Add(form);
        form.FormClosed += (_, _) => { reminders.Remove(form); main?.RefreshItems(); };
        form.Show();
        form.Activate();
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

    private void ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e) =>
        log.Write($"WinForms UI exception: {e.Exception}");

    /// <summary>只复制目标目录中尚不存在的旧数据，保留原 .toto 文件作为迁移备份。</summary>
    internal static bool ImportLegacyData(AppPaths paths, string? oldDataDirectory = null)
    {
        var old = oldDataDirectory ??
                  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toto");
        if (!Directory.Exists(old)) return false;
        Directory.CreateDirectory(paths.DataDirectory);
        var importedSettings = false;
        foreach (var name in new[] { "config.ini", "toto_ing.csv", "toto_end.csv" })
        {
            var source = Path.Combine(old, name);
            var target = Path.Combine(paths.DataDirectory, name);
            if (File.Exists(source) && !File.Exists(target))
            {
                File.Copy(source, target);
                if (name == "config.ini") importedSettings = true;
            }
        }
        foreach (var source in Directory.EnumerateFiles(old, "????.json"))
        {
            var target = Path.Combine(paths.DataDirectory, Path.GetFileName(source));
            if (!File.Exists(target)) File.Copy(source, target);
        }
        return importedSettings;
    }

    private void RemoveLegacyStartupShortcut()
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "toto.lnk");
        if (!File.Exists(shortcutPath)) return;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            var target = (string)shortcut.TargetPath;
            // 只移除旧 Toto 程序自己建立的入口，不删除同名但指向其他程序的快捷方式。
            if (string.Equals(Path.GetFileName(target), "toto.exe", StringComparison.OrdinalIgnoreCase))
                File.Delete(shortcutPath);
        }
        catch (Exception ex)
        {
            log.Write($"Could not remove legacy toto startup shortcut: {ex}");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.SessionSwitch -= SessionSwitch;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        SystemEvents.TimeChanged -= TimeChanged;
        Application.ThreadException -= ThreadException;
        retry.Dispose();
        scheduler.Dispose();
        foreach (var form in reminders.ToArray()) form.Close();
        foreach (var form in auxiliaryForms.ToArray()) form.Close();
        main?.Close();
        main?.Dispose();
    }
}
