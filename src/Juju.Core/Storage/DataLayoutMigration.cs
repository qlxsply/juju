using System.Security.Cryptography;

namespace Juju.Core.Storage;

/// <summary>旧版数据仅在新位置缺失时迁入；源文件保留，单个文件写完并验证后才发布。</summary>
public static class DataLayoutMigration
{
    public static async Task ImportLegacyAsync(IAtomicFileWriter writer, CancellationToken cancellationToken = default,
        string? installDirectory = null, string? dataRoot = null)
    {
        var install = installDirectory ?? ApplicationPaths.InstallDirectory;
        var root = dataRoot ?? ApplicationPaths.DataRoot;
        var marker = Path.Combine(root, "layout-migration-v1.json");
        if (File.Exists(marker)) return;
        var config = Path.Combine(root, "config.json");
        var oldConfig = Path.Combine(install, "config.json");
        if (File.Exists(oldConfig) && !File.Exists(config))
            await writer.WriteTextAsync(config,
                await File.ReadAllTextAsync(oldConfig, cancellationToken), cancellationToken);

        var oldRoot = Path.Combine(install, "data");
        foreach (var name in new[] { "json", "toto" })
            await CopyMissingAsync(Path.Combine(oldRoot, name),
                Path.Combine(root, "tools", name), cancellationToken);
        await writer.WriteTextAsync(marker, "{\"completed\":true}", cancellationToken);
    }

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
