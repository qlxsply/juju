using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Juju.App.Resources;
using Juju.Update;

namespace Juju.App.Settings;

/// <summary>当前版本、稳定版检查、下载进度和用户确认的更新入口。</summary>
public sealed class AboutSettingsSection : ISettingsSection
{
    private readonly GitHubUpdateService _updates;
    private readonly Func<PreparedUpdate, Task> _applyUpdate;
    private readonly string _currentVersion;
    private readonly TextBlock _status;
    private readonly TextBlock _releaseNotes;
    private readonly ProgressBar _progress;
    private readonly Button _checkButton;
    private readonly Button _updateButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _downloadCancellation;
    private AvailableUpdate? _available;

    public string Id => "about";
    public string DisplayName => "更新与版本";
    public FrameworkElement View { get; }

    public AboutSettingsSection(GitHubUpdateService updates, Func<PreparedUpdate, Task> applyUpdate)
    {
        _updates = updates;
        _applyUpdate = applyUpdate;
        _currentVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "未知";
        var page = SettingsViewFactory.Page();
        var current = SettingsViewFactory.Group(page, "当前版本");
        SettingsViewFactory.Row(current, "juju Toolbox", "版本 " + _currentVersion,
            new TextBlock
            {
                Text = "Windows x64",
                FontSize = 11,
                Foreground = AppResources.GetRequired<Brush>("ToolboxMuted")
            });

        var updateCard = SettingsViewFactory.Group(page, "软件更新");
        _status = new TextBlock
        {
            Text = "启动时每 24 小时检查一次稳定版更新。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            Margin = new Thickness(16, 12, 16, 6)
        };
        ((StackPanel) updateCard.Child).Children.Add(_status);
        _releaseNotes = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            Margin = new Thickness(16, 4, 16, 8),
            Visibility = Visibility.Collapsed
        };
        ((StackPanel) updateCard.Child).Children.Add(_releaseNotes);
        _progress = new ProgressBar { Height = 5, Margin = new Thickness(16, 0, 16, 10), Visibility = Visibility.Collapsed };
        ((StackPanel) updateCard.Child).Children.Add(_progress);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 4, 16, 14)
        };
        _checkButton = SettingsViewFactory.SoftButton("检查更新", CheckUpdate_Click);
        _updateButton = SettingsViewFactory.SoftButton("下载并更新", ApplyUpdate_Click);
        _updateButton.Margin = new Thickness(8, 0, 0, 0);
        _updateButton.Visibility = Visibility.Collapsed;
        _cancelButton = SettingsViewFactory.SoftButton("取消下载", (_, _) => _downloadCancellation?.Cancel());
        _cancelButton.Margin = new Thickness(8, 0, 0, 0);
        _cancelButton.Visibility = Visibility.Collapsed;
        buttons.Children.Add(_checkButton);
        buttons.Children.Add(_updateButton);
        buttons.Children.Add(_cancelButton);
        ((StackPanel) updateCard.Child).Children.Add(buttons);
        _updates.CheckCompleted += OnCheckCompleted;
        if ( _updates.LastResult is { } previous )
            ApplyCheckResult(previous);
        View = page;
    }

    public Task SaveAsync() => Task.CompletedTask;

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _checkButton.IsEnabled = false;
        _status.Text = "正在检查 GitHub 稳定版 Release…";
        try
        {
            await _updates.CheckAsync(_currentVersion, manual: true);
        }
        finally
        {
            _checkButton.IsEnabled = true;
        }
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if ( _available is not { } update )
            return;
        var answer = MessageBox.Show($"将下载并安装 juju {update.Manifest.Version}，应用会保存数据后关闭并自动重启。是否继续？",
            "juju 更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if ( answer != MessageBoxResult.Yes )
            return;

        _checkButton.IsEnabled = false;
        _updateButton.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _status.Text = "正在安全下载更新包…";
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        _cancelButton.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                if ( value.Percentage is { } percent )
                {
                    _progress.IsIndeterminate = false;
                    _progress.Value = percent;
                    _status.Text = $"正在下载更新：{percent:0}%";
                }
            });
            var package = await _updates.DownloadAsync(update, progress, cancellation.Token);
            _progress.Visibility = Visibility.Collapsed;
            _cancelButton.Visibility = Visibility.Collapsed;
            _status.Text = "下载和 SHA-256 校验完成，正在安全关闭并更新…";
            await _applyUpdate(package);
        }
        catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested )
        {
            _status.Text = "更新下载已取消。";
            _updateButton.IsEnabled = true;
            _checkButton.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
            _cancelButton.Visibility = Visibility.Collapsed;
        }
        catch ( Exception ex )
        {
            _status.Text = "更新未能启动：" + ex.Message;
            _updateButton.IsEnabled = true;
            _checkButton.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
            MessageBox.Show("更新未能启动，应用保持运行。\n\n" + ex.Message, "juju 更新",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _downloadCancellation = null;
        }
    }

    private void OnCheckCompleted(UpdateCheckResult result)
    {
        if ( Application.Current.Dispatcher.CheckAccess() )
            ApplyCheckResult(result);
        else
            Application.Current.Dispatcher.BeginInvoke(() => ApplyCheckResult(result));
    }

    private void ApplyCheckResult(UpdateCheckResult result)
    {
        _available = result.Update;
        _updateButton.Visibility = _available is null ? Visibility.Collapsed : Visibility.Visible;
        if ( result.Error is not null )
        {
            _status.Text = result.Error;
            _releaseNotes.Visibility = Visibility.Collapsed;
            return;
        }
        if ( _available is not { } update )
        {
            _status.Text = result.WasSkippedByCooldown
                ? "24 小时内已检查过；当前没有可用更新。"
                : "当前已是最新稳定版。";
            _releaseNotes.Visibility = Visibility.Collapsed;
            return;
        }

        _status.Text = $"发现稳定版 {update.Manifest.Version}（下载 {FormatBytes(update.PackageSize)}）。";
        _releaseNotes.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? $"发布说明：{update.ReleaseNotesUrl}"
            : update.ReleaseNotes.Length <= 1200 ? update.ReleaseNotes : update.ReleaseNotes[..1200] + "…";
        _releaseNotes.Visibility = Visibility.Visible;
        _updateButton.IsEnabled = true;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.0} MB",
        _ => $"{bytes / 1024d:0} KB"
    };
}
