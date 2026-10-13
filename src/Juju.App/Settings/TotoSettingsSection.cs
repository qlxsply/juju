using System.Windows;
using Juju.Core.Tools;
using Toto.App;

namespace Juju.App.Settings;

/// <summary>保留 Toto 原 WinForms 专属设置窗；WPF 页面提供与其他工具一致的发现入口。</summary>
public sealed class TotoSettingsSection : ISettingsSection
{
    private readonly TotoToolHost _toto;
    private readonly System.Windows.Controls.TextBlock _hotkey;
    public string Id => "toto";
    public string DisplayName => "Toto 日程提醒";
    public FrameworkElement View
    {
        get { _hotkey.Text = "当前全局快捷键：" + _toto.Hotkey; return _view; }
    }
    private readonly FrameworkElement _view;

    public TotoSettingsSection(TotoToolHost toto, Action<ToolId> openTool)
    {
        _toto = toto;
        var page = SettingsViewFactory.Page("TOOL SETTINGS / T", "Toto 日程提醒",
            "事项、工作日日历和提醒调度都由 Toto 工具单独管理，不影响 JSON 编辑器。");
        SettingsViewFactory.Profile(page, "Toto 日程提醒", "日程管理与自动弹窗提醒 · 启动键 T",
            (System.Windows.Media.ImageSource)System.Windows.Application.Current.Resources["TotoToolIcon"],
            "#E7F8F3", (_, _) => openTool(ToolId.Toto));
        var options = SettingsViewFactory.Group(page, "专属功能设置");
        _hotkey = new System.Windows.Controls.TextBlock { Text = "当前全局快捷键：" + toto.Hotkey,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["ToolboxMuted"],
            FontSize = 11 };
        SettingsViewFactory.Row(options, "Toto 专属快捷键", "在原 Toto 设置窗口中编辑热键和应用内按键。", _hotkey);
        SettingsViewFactory.Row(options, "日程及提醒", "提前提醒分钟数、上班/下班提醒和工作日配置。",
            SettingsViewFactory.SoftButton("打开 Toto 设置", (_, _) => toto.ShowSettings()));
        SettingsViewFactory.Note(page, "关闭 Toto 主窗口不会停止提醒；已到期事项仍会独立弹窗。");
        _view = page;
    }

    public Task SaveAsync() => Task.CompletedTask;
}
