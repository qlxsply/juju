using System.Windows.Forms;
using Juju.Core.App;
using Juju.Core.Tools;

namespace Juju.App.Bootstrap;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayService(IWindowManager windows, IToolManager tools, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Launcher", null, (_, _) => windows.ShowLauncher());
        menu.Items.Add("JSON", null, (_, _) => tools.Open(ToolId.Json));
        menu.Items.Add("设置", null, (_, _) => windows.ShowSettings());
        menu.Items.Add("退出 juju", null, (_, _) => exit());
        _icon = new NotifyIcon { Text = "juju", Visible = true, ContextMenuStrip = menu };
        _icon.DoubleClick += (_, _) => windows.ShowLauncher();
    }

    public void Dispose() => _icon.Dispose();
}
