using System.Security.Cryptography;

namespace Juju.Core.Storage;

/// <summary>在用户主动更换数据目录时复制缺少的文件；源文件保留，单个文件验证后再发布。</summary>
public static class DataLayoutMigration
{
    public static async Task CopyMissingAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(source)) return;
        if (new DirectoryInfo(source).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Cannot migrate through a linked data directory.");
        foreach (var input in Directory.EnumerateFiles(source, "*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.Combine(destination, Path.GetRelativePath(source, input));
            if (File.Exists(output)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var temporary = output + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                await using (var from = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var to = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await from.CopyToAsync(to, cancellationToken);
                byte[] expected;
                byte[] actual;
                await using (var original = File.OpenRead(input))
                    expected = await SHA256.HashDataAsync(original, cancellationToken);
                await using (var copied = File.OpenRead(temporary))
                    actual = await SHA256.HashDataAsync(copied, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                    throw new IOException("Migrated data differs from the original: " + input);
                File.Move(temporary, output, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

}
