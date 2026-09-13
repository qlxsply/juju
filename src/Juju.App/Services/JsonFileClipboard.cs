using System.IO;
using Juju.App.Views;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;

namespace Juju.App.Services;

// 将 Core 的 JSON 快照适配为 Windows 文件拖放剪贴板格式；平台 API 保持在 App 层。
public sealed class JsonFileClipboard(IAtomicFileWriter writer) : IJsonFileClipboard
{
    private readonly string _directory = Path.Combine(ApplicationPaths.ToolDataDirectory("json"), "cache", "clipboard");

    // 先原子写入临时副本，再把路径而非文本放入 FileDrop，供资源管理器等消费者粘贴为文件。
    public async Task CopySnapshotAsync(JsonDocumentSnapshot snapshot, string fileName,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        await writer.WriteTextAsync(path, snapshot.Content, cancellationToken);
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { path });
        Clipboard.SetDataObject(data, copy: true);
    }

    // 清理是同步文件枚举操作，但接口返回已完成 Task，以与调用方的异步生命周期保持一致。
    public Task CleanExpiredSnapshotsAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return Task.CompletedTask;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow - maximumAge) continue;
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }

        return Task.CompletedTask;
    }
}