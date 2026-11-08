using System.Windows;
using System.Windows.Controls;
using Juju.Core.Settings;
using Juju.Core.Storage;

namespace Juju.App.Settings;

/// <summary>仅管理应用数据根；各工具目录由数据根固定派生，并在各工具页面只读显示。</summary>
public sealed class StorageSettingsSection : ISettingsSection
{
    private readonly DataDirectoryChangeService _changes;
    private readonly SettingsDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly Func<Task> _restartApplication;
    private readonly Action<string> _renderDirectory;
    private readonly TextBlock _pendingNotice;
    public string Id => "storage";
    public string DisplayName => "存储设置";
    public FrameworkElement View { get; }

    public StorageSettingsSection(DataDirectoryChangeService changes, SettingsDialogService dialogs,
        ISettingsService settings, Func<Task> restartApplication)
    {
        _changes = changes;
        _dialogs = dialogs;
        _settings = settings;
        _restartApplication = restartApplication;
        var page = SettingsViewFactory.Page();
        var card = SettingsViewFactory.Group(page, "数据存储");
        _renderDirectory = SettingsViewFactory.DirectoryRow(card, "应用数据根目录", changes.DisplayRoot, EditDirectory,
            () => SettingsViewFactory.OpenDirectory(_changes.DisplayRoot));
        _pendingNotice = new TextBlock
        {
            Text = "新目录将在重启后生效；不会迁移当前目录中的文件。",
            Margin = new Thickness(20, 0, 16, 12),
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.Gray,
            Visibility = changes.PendingRoot is null ? Visibility.Collapsed : Visibility.Visible
        };
        var contents = new StackPanel();
        var directoryLayout = card.Child ?? throw new InvalidOperationException("数据目录卡片缺少目录行布局。");
        card.Child = null;
        contents.Children.Add(directoryLayout);
        contents.Children.Add(_pendingNotice);
        card.Child = contents;
        View = page;
    }

    private void EditDirectory()
    {
        _dialogs.EditFolder("选择应用数据根目录", _changes.DisplayRoot, async path =>
        {
            if ( !_changes.StageRoot(path) )
            {
                if ( _changes.PendingRoot is not null && _dialogs.ConfirmDataRootRestart(_changes.PendingRoot) )
                    await _restartApplication();
                return;
            }
            try
            { await _settings.SaveAsync(_settings.Current with { PendingDataRoot = _changes.PendingRoot }); }
            catch
            {
                _changes.LoadPending(_settings.Current.PendingDataRoot);
                throw;
            }
            _renderDirectory(_changes.DisplayRoot);
            _pendingNotice.Visibility = _changes.PendingRoot is null ? Visibility.Collapsed : Visibility.Visible;
            if ( _changes.PendingRoot is not null && _dialogs.ConfirmDataRootRestart(_changes.PendingRoot) )
                await _restartApplication();
        });
    }

    public Task SaveAsync() => Task.CompletedTask;
}
