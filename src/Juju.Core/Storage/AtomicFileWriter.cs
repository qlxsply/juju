namespace Juju.Core.Storage;

/// <summary>以“写入临时文件后替换”方式持久化文本的抽象，避免读者看到半写入文件。</summary>
public interface IAtomicFileWriter
{
    /// <summary>异步写入文本；取消令牌会传入支持取消的写入和刷新操作。</summary>
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}

/// <summary>面向本地文件系统的原子文本写入器。</summary>
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    /// <summary>
    /// 先将内容写至同目录临时文件并强制落盘，再用替换或移动发布它。<c>await using</c>
    /// 异步释放流，作用相当于 Java 的 try-with-resources，但可等待异步清理。
    /// </summary>
    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path) ??
                        throw new ArgumentException("A file path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // 目标文件不会在临时文件持久化前被截断，因此崩溃时仍保留旧版本。
                File.Replace(temporary, path, null, ignoreMetadataErrors: true);
            }
            else File.Move(temporary, path);
        }
        finally
        {
            // 无论写入、替换或取消如何结束，都尽力移除未发布的临时文件。
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}