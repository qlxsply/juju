using System.IO;
using System.Windows;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;

namespace Juju.App.Services;

public sealed class JsonFileClipboard(IAtomicFileWriter writer) : IJsonFileClipboard
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "juju", "cache", "clipboard");

    public async Task CopySnapshotAsync(JsonDocumentSnapshot snapshot, string fileName, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        await writer.WriteTextAsync(path, snapshot.Content, cancellationToken);
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { path });
        System.Windows.Clipboard.SetDataObject(data, copy: true);
    }

    public Task CleanExpiredSnapshotsAsync(TimeSpan maximumAge, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return Task.CompletedTask;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - maximumAge)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
        return Task.CompletedTask;
    }
}
