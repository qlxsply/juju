using Microsoft.Win32;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Toto.App.UI;

namespace Toto.App;

/// <summary>
/// 管理 WinForms 消息循环、通知区域图标、主窗口、全局热键和提醒调度器的应用上下文。
/// 它取代单一启动窗体，使关闭主窗体时进程仍可由托盘图标继续运行。
/// </summary>
internal sealed class TotoApplicationContext : ApplicationContext
{
    /// <summary>驻留通知区域并承载托盘菜单的图标。</summary>
    private readonly NotifyIcon tray;
    /// <summary>进行中及历史事项的数据访问入口。</summary>
    private readonly ItemRepository items;
    /// <summary>应用设置的数据访问入口。</summary>
    private readonly SettingsRepository settings;
    /// <summary>负责计算并触发提醒的调度器。</summary>
    private readonly ReminderScheduler scheduler;
    /// <summary>注册并监听系统范围快捷键的可释放服务。</summary>
    private readonly GlobalHotkeyService hotkey = new();

    /// <summary>将后台回调切回 WinForms UI 线程的同步上下文。</summary>
    private readonly SynchronizationContext ui = SynchronizationContext.Current ??
                                                  new WindowsFormsSynchronizationContext();

    /// <summary>创建此上下文的托管 UI 线程标识。</summary>
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    /// <summary>当前主窗口；窗口关闭并释放后可重新创建，因此允许为空。</summary>
    private MainForm? main;

    /// <summary>初始化应用服务，订阅系统事件并启动提醒调度。</summary>
    /// <param name="paths">应用文件路径。</param>
    /// <param name="log">启动和运行错误的日志写入器。</param>
    /// <param name="items">事项仓储。</param>
    /// <param name="settings">设置仓储。</param>
    /// <param name="calendarRepository">工作日日历仓储。</param>
    public TotoApplicationContext(AppPaths paths, AppLog log, ItemRepository items, SettingsRepository settings,
        WorkCalendarRepository calendarRepository)
    {
        this.items = items;
        this.settings = settings;
        var calendar = new WorkCalendarService(calendarRepository);
        scheduler = new ReminderScheduler(items, settings, calendar);
        // C# 事件是多播委托；+= 订阅回调而非覆盖一个 Java 风格的 listener 字段。
        scheduler.DueReminders += ShowReminders;
        scheduler.ScheduledPopup += kind => ShowWorkPopup(kind);
        tray = new NotifyIcon
        {
            Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "my_icon.ico")), Text = "toto", Visible = true,
            ContextMenuStrip = BuildTray(calendarRepository, calendar)
        };
        tray.DoubleClick += (_, _) => ShowMain();
        hotkey.Pressed += ShowMain;
        if (!hotkey.Register(settings.Load().GetValueOrDefault("hotkey", "Ctrl+Alt+Space")))
            log.Write("Global hotkey registration failed.");
        SystemEvents.SessionSwitch += SessionSwitch;
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) scheduler.Resume();
        };
        SystemEvents.TimeChanged += (_, _) => scheduler.Resume();
        scheduler.Reschedule();
    }

    /// <summary>创建通知区域图标使用的上下文菜单。</summary>
    /// <param name="repository">工作日日历的数据访问入口。</param>
    /// <param name="calendar">工作日计算服务。</param>
    /// <returns>已绑定点击处理程序的托盘菜单。</returns>
    private ContextMenuStrip BuildTray(WorkCalendarRepository repository, WorkCalendarService calendar)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 toto", null, (_, _) => ShowMain());
        menu.Items.Add("新增事项", null, (_, _) =>
        {
            ShowMain();
            main!.ShowEditor();
        });
        menu.Items.Add("历史事项", null, (_, _) => new HistoryForm(items).Show(main));
        menu.Items.Add("设置", null, (_, _) =>
        {
            // using 声明保证模态窗体关闭后释放其原生窗口句柄。
            using var form = new SettingsForm(settings, scheduler);
            if (form.ShowDialog(main) == DialogResult.OK) RegisterGlobalHotkey();
        });
        menu.Items.Add("工作日管理", null, (_, _) => new WorkCalendarForm(repository).ShowDialog(main));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        return menu;
    }

    /// <summary>显示、激活并刷新主窗口；从后台线程调用时先投递到 UI 线程。</summary>
    public void ShowMain()
    {
        if (Environment.CurrentManagedThreadId != uiThread)
        {
            ui.Post(_ => ShowMain(), null);
            return;
        }

        // nullable 模式匹配/空值检查与 Java Optional 不同，直接针对可空引用进行流分析。
        if (main is null || main.IsDisposed)
        {
            main = new MainForm(items, settings, scheduler, RegisterGlobalHotkey);
            main.FormClosed += (_, _) => main = null;
        }

        main.Show();
        main.Activate();
        main.RefreshItems();
    }

    /// <summary>按当前设置重新注册全局唤醒快捷键。</summary>
    private void RegisterGlobalHotkey() =>
        hotkey.Register(settings.Load().GetValueOrDefault("hotkey", "Ctrl+Alt+Space"));

    /// <summary>以模态窗口显示已到提醒时间的事项。</summary>
    /// <param name="due">需要提醒的只读事项集合。</param>
    private void ShowReminders(IReadOnlyList<TodoItem> due)
    {
        using var form = new ReminderForm(items, due, "toto 提醒");
        form.ShowDialog(main);
        main?.RefreshItems();
    }

    /// <summary>根据上班或下班计划显示全部进行中事项。</summary>
    /// <param name="kind">决定弹窗标题的计划类型。</param>
    private void ShowWorkPopup(ScheduledPopupKind kind)
    {
        using var form = new ReminderForm(items, items.GetActive(),
            kind == ScheduledPopupKind.WorkStart ? "toto - 上班事项提醒" : "toto - 下班事项提醒");
        form.ShowDialog(main);
        main?.RefreshItems();
    }

    /// <summary>响应 Windows 会话锁定和解锁，暂停或恢复提醒调度。</summary>
    /// <param name="sender">发布系统事件的对象。</param>
    /// <param name="e">包含会话切换原因的事件参数。</param>
    private void SessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            scheduler.SetLocked(true);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            scheduler.SetLocked(false);
            scheduler.Resume();
        }
    }

    /// <summary>在线程消息循环退出时取消订阅并释放所有应用级资源。</summary>
    // override 覆盖基类的生命周期钩子；必须显式调用 base 以完成 ApplicationContext 的退出流程。
    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= SessionSwitch;
        tray.Visible = false;
        tray.Dispose();
        hotkey.Dispose();
        scheduler.Dispose();
        base.ExitThreadCore();
    }
}
