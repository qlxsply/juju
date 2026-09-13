using System.Windows.Forms;
using Juju.Core.App;
using Juju.Core.Tools;

namespace Juju.App.Bootstrap;

// NotifyIcon 来自 WinForms 而非 WPF；此服务将原生托盘事件适配为窗口和工具管理接口调用。
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Drawing.Icon _trayImage;

    // 依赖和退出回调由 DI 组装。菜单 Click 是同步事件，因此退出回调在 App 中转发到异步关停流程。
    public TrayService(IWindowManager windows, IToolManager tools, Action exit)
    {
        using var stream =
            System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/juju.ico"))!.Stream;
        using var image = new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        _trayImage = (System.Drawing.Icon)image.Clone();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Launcher", null, (_, _) => windows.ShowLauncher());
        menu.Items.Add("JSON", null, (_, _) => tools.Open(ToolId.Json));
        menu.Items.Add("设置", null, (_, _) => windows.ShowSettings());
        menu.Items.Add("退出 juju", null, (_, _) => exit());
        _icon = new NotifyIcon { Text = "juju", Icon = _trayImage, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => windows.ShowLauncher();
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