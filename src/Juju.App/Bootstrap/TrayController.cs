using Juju.App.Contracts.Windows;
using Juju.Core.Contracts.Platform;
using Juju.Update;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Bootstrap;

/// <summary>把平台托盘命令协调到 App 的窗口和应用生命周期。</summary>
public sealed class TrayController : IDisposable
{
    private readonly ITrayIconService _tray;
    private readonly IAppWindowManager _windows;
    private readonly Action _exit;
    private readonly GitHubUpdateService _updates;
    private readonly string _currentVersion;
    private string? _notifiedVersion;
    private bool _disposed;

    public TrayController(ITrayIconService tray, IAppWindowManager windows, Action exit,
        GitHubUpdateService updates, string currentVersion)
    {
        _tray = tray;
        _windows = windows;
        _exit = exit;
        _updates = updates;
        _currentVersion = currentVersion;
        _tray.CommandRequested += OnCommandRequested;
        _updates.CheckCompleted += OnUpdateCheckCompleted;
    }

    private void OnCommandRequested(TrayCommand command)
    {
        switch ( command )
        {
            case TrayCommand.ShowLauncher:
                _windows.ShowLauncher();
                break;
            case TrayCommand.ShowSettings:
                _windows.ShowSettings();
                break;
            case TrayCommand.Help:
                ShowHelp();
                break;
            case TrayCommand.CheckForUpdates:
                _windows.ShowUpdateSettings();
                _ = _updates.CheckAsync(_currentVersion, manual: true);
                break;
            case TrayCommand.Exit:
                _exit();
                break;
        }
    }

    private static void ShowHelp() =>
        MessageBox.Show("juju Toolbox\n\n显示主界面可打开工具列表；应用设置集中管理工具箱和各工具选项。",
            "帮助", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    private void OnUpdateCheckCompleted(UpdateCheckResult result)
    {
        if ( result.Update is not { } update || _notifiedVersion == update.Manifest.Version )
            return;
        _notifiedVersion = update.Manifest.Version;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            _tray.ShowNotification("juju 有可用更新", $"稳定版 {update.Manifest.Version} 已发布，可在“更新与版本”页面查看。")));
    }

    public void Dispose()
    {
        if ( _disposed )
            return;
        _disposed = true;
        _tray.CommandRequested -= OnCommandRequested;
        _updates.CheckCompleted -= OnUpdateCheckCompleted;
        _tray.Dispose();
    }
}
