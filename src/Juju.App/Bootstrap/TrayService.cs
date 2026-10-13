using System.Windows.Forms;
using Juju.Core.App;
using Juju.Core.Tools;
using Toto.App;
using Juju.Core.Settings;

namespace Juju.App.Bootstrap;

// NotifyIcon 来自 WinForms 而非 WPF；此服务将原生托盘事件适配为窗口和工具管理接口调用。
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Drawing.Icon _trayImage;

    // 依赖和退出回调由 DI 组装。菜单 Click 是同步事件，因此退出回调在 App 中转发到异步关停流程。
    public TrayService(IWindowManager windows, IToolManager tools, TotoToolHost toto,
        ISettingsService settings, Action exit)
    {
        using var stream =
            System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/juju.ico"))!.Stream;
        using var image = new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        _trayImage = (System.Drawing.Icon)image.Clone();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Launcher", null, (_, _) => windows.ShowLauncher());
        // 只有 Juju 自己维护 NotifyIcon；工具不再创建第二个托盘图标。
        menu.Items.Add("JSON", null, (_, _) => tools.Open(ToolId.Json));
        menu.Items.Add("Toto（日程提醒）", null, (_, _) => tools.Open(ToolId.Toto));
        var totoMenu = new ToolStripMenuItem("Toto 操作");
        totoMenu.DropDownItems.Add("新增事项", null, (_, _) => toto.ShowNewItem());
        totoMenu.DropDownItems.Add("历史事项", null, (_, _) => toto.ShowHistory());
        totoMenu.DropDownItems.Add("设置", null, (_, _) => toto.ShowSettings());
        totoMenu.DropDownItems.Add("工作日管理", null, (_, _) => toto.ShowCalendar());
        menu.Items.Add(totoMenu);
        menu.Items.Add("设置", null, (_, _) => windows.ShowSettings());
        menu.Items.Add("退出 juju", null, (_, _) => exit());
        _icon = new NotifyIcon { Text = "juju", Icon = _trayImage, ContextMenuStrip = menu, Visible = true };
        // 左键单击图标即显示工具入口；右键保留 Windows 托盘上下文菜单。
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) windows.ShowLauncher();
        };
        if (windows is DesktopWindowManager manager)
            manager.ToolHidden += tool =>
            {
                if (!settings.Current.ShowToolNotifications) return;
                _icon.ShowBalloonTip(1800, "juju Toolbox",
                    $"{(tool == ToolId.Json ? "JSON" : "Toto")} 已隐藏，仍可从托盘或快捷键打开。",
                    ToolTipIcon.Info);
            };
    }

    // 托盘图标是系统资源；必须先隐藏再释放，避免应用结束后通知区域留下失效图标。
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _trayImage.Dispose();
    }
}
