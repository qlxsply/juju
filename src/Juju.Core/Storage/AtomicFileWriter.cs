namespace Juju.Core.Storage;

public interface IAtomicFileWriter { Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default); }

public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("A file path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // Replace does not truncate the destination before the temporary file is durable.
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
