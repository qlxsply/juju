using Juju.Core.Contracts.Platform;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace Juju.Platform.Windows.Dialogs;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string title, string? initialDirectory, IntPtr ownerHandle)
    {
        using var picker = new FolderBrowserDialog
        {
            SelectedPath = initialDirectory ?? "",
            UseDescriptionForTitle = true,
            Description = title
        };
        var owner = ownerHandle == IntPtr.Zero ? null : new WindowOwner(ownerHandle);
        return picker.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? picker.SelectedPath : null;
    }

    private sealed class WindowOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; } = handle;
    }
}
