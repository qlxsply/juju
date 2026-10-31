using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Juju.Core.Contracts.Platform;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.App.Views;
using TextBox = System.Windows.Controls.TextBox;

namespace Juju.App.Settings;

/// <summary>设置页统一使用当前设置窗口的遮罩弹层编辑快捷键、目录及组合配置。</summary>
public sealed class SettingsDialogService
{
    private readonly IFolderPicker _folderPicker;
    private SettingsWindow? _owner;

    public SettingsDialogService(IFolderPicker folderPicker) => _folderPicker = folderPicker;

    public void Attach(SettingsWindow owner) => _owner = owner;

    public void EditShortcut(string title, string current, bool allowNone, bool allowSingle,
        bool allowSequence, Func<string, string?> validate, Func<string, Task> commit, string? defaultValue = null,
        bool allowUnmodified = false)
    {
        Owner().OpenShortcutDialog(title, current, allowNone, allowSingle, allowSequence,
            validate, commit, defaultValue, allowUnmodified);
    }

    public void EditGroup(string title, string description, FrameworkElement draftContent, Func<Task> commit,
        double width = 460, string saveText = "保存") =>
        Owner().OpenEditorDialog(title, description, draftContent, commit, width, saveText);

    public async void EditFolder(string title, string current, Func<string, Task> stage)
    {
        var owner = Owner();
        var path = _folderPicker.PickFolder(title, current, new WindowInteropHelper(owner).Handle);
        if (path is null) return;
        try { await stage(path); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(owner, "保存目录设置失败：" + ex.Message, "Toolbox 设置",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private SettingsWindow Owner() => _owner ?? throw new InvalidOperationException("设置弹窗尚未连接到设置窗口。");
}
