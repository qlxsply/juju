using System.Windows;
using System.Windows.Controls;
using Juju.Core.Storage;
using Juju.Core.Settings;

namespace Juju.App.Settings;

/// <summary>仅管理工具箱数据根；config.json 位于选定根目录，工具目录在各工具自己的页面设置。</summary>
public sealed class StorageSettingsSection : ISettingsSection
{
    private readonly DataDirectoryChangeService _changes;
    private readonly SettingsDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly Action<string> _renderDirectory;
    public string Id => "storage";
    public string DisplayName => "存储设置";
    public FrameworkElement View { get; }

    public StorageSettingsSection(DataDirectoryChangeService changes, SettingsDialogService dialogs,
        ISettingsService settings)
    {
        _changes = changes;
        _dialogs = dialogs;
        _settings = settings;
        var page = SettingsViewFactory.Page();
        var card = SettingsViewFactory.Group(page, "数据存储");
        _renderDirectory = SettingsViewFactory.DirectoryRow(card, "应用根目录", changes.DisplayRoot, EditDirectory,
            () => SettingsViewFactory.OpenDirectory(_changes.DisplayRoot));
        View = page;
    }

    private void EditDirectory()
    {
        _dialogs.EditFolder("选择工具箱数据目录", _changes.DisplayRoot, async path =>
        {
            _changes.StageRoot(path);
            try { await _settings.SaveAsync(_settings.Current with { PendingDataRoot = _changes.PendingRoot }); }
            catch
            {
                _changes.LoadPending(_settings.Current.PendingDataRoot, _settings.Current.PendingToolDataDirectories);
                throw;
            }
            _renderDirectory(_changes.DisplayRoot);
        });
    }

    public Task SaveAsync() => Task.CompletedTask;
}
