namespace Juju.Core.Contracts.Platform;

/// <summary>由宿主请求系统目录选择器；取消时返回空值。</summary>
public interface IFolderPicker
{
    string? PickFolder(string title, string? initialDirectory, IntPtr ownerHandle);
}
