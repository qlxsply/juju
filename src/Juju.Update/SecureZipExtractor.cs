using System.IO.Compression;

namespace Juju.Update;

public static class SecureZipExtractor
{
    public const long DefaultMaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    public const int DefaultMaximumEntries = 30_000;

    public static async Task ExtractAsync(string archivePath, string destinationDirectory,
        long maximumExpandedBytes = DefaultMaximumExpandedBytes, int maximumEntries = DefaultMaximumEntries,
        CancellationToken cancellationToken = default)
    {
        if ( maximumExpandedBytes <= 0 || maximumEntries <= 0 )
            throw new ArgumentOutOfRangeException(nameof(maximumExpandedBytes));

        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if ( archive.Entries.Count > maximumEntries )
            throw new InvalidDataException("Update archive contains too many entries.");

        long declaredExpandedBytes = 0;
        foreach ( var entry in archive.Entries )
        {
            if ( entry.Length < 0 || entry.Length > maximumExpandedBytes - declaredExpandedBytes )
                throw new InvalidDataException("Update archive exceeds the expanded size limit.");
            declaredExpandedBytes += entry.Length;
        }
        var driveRoot = Path.GetPathRoot(root);
        if ( !string.IsNullOrEmpty(driveRoot) )
        {
            try
            {
                var requiredBytes = checked(declaredExpandedBytes + 64L * 1024 * 1024);
                if ( new DriveInfo(driveRoot).AvailableFreeSpace < requiredBytes )
                    throw new IOException("磁盘空间不足，无法安全暂存更新版本。");
            }
            catch ( ArgumentException )
            {
                /* Non-filesystem roots are validated by the actual extraction writes. */
            }
        }

        long expanded = 0;
        foreach ( var entry in archive.Entries )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = ValidateEntryName(entry.FullName);
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if ( !target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) )
                throw new InvalidDataException("Update archive entry escapes the staging directory.");
            if ( !seen.Add(relative.TrimEnd('/')) )
                throw new InvalidDataException("Update archive contains a duplicate path.");
            RejectLinkOrSpecialFile(entry);

            if ( entry.Length < 0 || entry.Length > maximumExpandedBytes - expanded )
                throw new InvalidDataException("Update archive exceeds the expanded size limit.");
            expanded += entry.Length;

            if ( relative.EndsWith('/') )
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            long copied = 0;
            while ( true )
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if ( read == 0 ) break;
                copied = checked(copied + read);
                if ( copied > entry.Length )
                    throw new InvalidDataException("An update archive entry expands beyond its declared size.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if ( copied != entry.Length )
                throw new InvalidDataException("Extracted update file length does not match the archive entry.");
        }
    }

    private static string ValidateEntryName(string name)
    {
        if ( string.IsNullOrWhiteSpace(name) || name.Length > 240 || name.Contains('\0') )
            throw new InvalidDataException("Update archive contains an invalid path.");
        var normalized = name.Replace('\\', '/');
        var segments = normalized.TrimEnd('/').Split('/');
        if ( normalized.StartsWith('/') || Path.IsPathRooted(normalized) || segments.Any(segment => !IsSafeSegment(segment)) )
            throw new InvalidDataException("Update archive contains a rooted or traversing path.");
        return normalized;
    }

    private static bool IsSafeSegment(string segment)
    {
        if ( segment.Length == 0 || segment is ".." or "." || segment.EndsWith(' ') || segment.EndsWith('.') ||
             segment.Any(character => character < 32 || "<>:\"|?*".Contains(character)) )
            return false;
        var deviceName = segment.Split('.')[0];
        return !deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) &&
               !deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) &&
               !deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) &&
               !deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) &&
               !Enumerable.Range(1, 9).Any(number =>
                   deviceName.Equals($"COM{number}", StringComparison.OrdinalIgnoreCase) ||
                   deviceName.Equals($"LPT{number}", StringComparison.OrdinalIgnoreCase));
    }

    private static void RejectLinkOrSpecialFile(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        if ( (entry.ExternalAttributes & (int) FileAttributes.ReparsePoint) != 0 ||
             unixMode is 0xA000 or 0x6000 or 0x2000 or 0x1000 )
            throw new InvalidDataException("Update archive contains a link or special file.");
    }
}

public static class VersionPackageStager
{
    public static async Task<string> StageAsync(string packagePath, string installRoot, ReleaseManifest manifest,
        string launcherVersion, IPackageSignatureVerifier? signatureVerifier = null,
        CancellationToken cancellationToken = default)
    {
        manifest.Validate(manifest.Version);
        if ( !SemanticVersion.TryParse(launcherVersion, out var currentLauncher) ||
             currentLauncher < manifest.ParsedMinimumLauncherVersion )
            throw new InvalidDataException("Installed launcher is too old to apply this update.");

        await PackageHashVerifier.VerifyAsync(packagePath, manifest.Sha256, cancellationToken);
        await (signatureVerifier ?? new NoOpPackageSignatureVerifier()).VerifyAsync(packagePath, cancellationToken);

        var root = Path.GetFullPath(installRoot);
        var stagingRoot = Path.Combine(root, "staging");
        Directory.CreateDirectory(stagingRoot);
        var stage = Path.Combine(stagingRoot, $"{manifest.Version}.{Guid.NewGuid():N}.tmp");
        try
        {
            await SecureZipExtractor.ExtractAsync(packagePath, stage, cancellationToken: cancellationToken);
            if ( !File.Exists(Path.Combine(stage, "Juju.App.exe")) )
                throw new InvalidDataException("Update package does not contain Juju.App.exe.");
            return stage;
        }
        catch
        {
            if ( Directory.Exists(stage) ) Directory.Delete(stage, recursive: true);
            throw;
        }
    }
}
