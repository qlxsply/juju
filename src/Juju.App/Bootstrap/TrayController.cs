using Juju.App.Contracts.Windows;
using Juju.Core.Contracts.Platform;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Bootstrap;

/// <summary>把平台托盘命令协调到 App 的窗口和应用生命周期。</summary>
public sealed class TrayController : IDisposable
{
    private readonly ITrayIconService _tray;
    private readonly IAppWindowManager _windows;
    private readonly Action _exit;
    private bool _disposed;

    public TrayController(ITrayIconService tray, IAppWindowManager windows, Action exit)
    {
        _tray = tray;
        _windows = windows;
        _exit = exit;
        _tray.CommandRequested += OnCommandRequested;
    }

    private void OnCommandRequested(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.ShowLauncher: _windows.ShowLauncher(); break;
            case TrayCommand.ShowSettings: _windows.ShowSettings(); break;
            case TrayCommand.Help: ShowHelp(); break;
            case TrayCommand.CheckForUpdates: ShowUpdateStatus(); break;
            case TrayCommand.Exit: _exit(); break;
        }
    }

    private static void ShowHelp() =>
        MessageBox.Show("juju Toolbox\n\n显示主界面可打开工具列表；应用设置集中管理工具箱和各工具选项。",
            "帮助", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    private static void ShowUpdateStatus() =>
        MessageBox.Show("当前版本没有配置在线更新服务。请使用 Toolbox 安装包更新应用。",
            "检查更新", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.CommandRequested -= OnCommandRequested;
        _tray.Dispose();
    }
}
