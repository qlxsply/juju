using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Juju.Update;

public sealed record InstallerPayloadEntry
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
}

public sealed record InstallerPayloadManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("entries")] public IReadOnlyList<InstallerPayloadEntry> Entries { get; init; } = [];

    public static async Task VerifyAsync(string installRoot, string stagedVersionDirectory, string manifestPath,
        string expectedManifestSha256, string version, CancellationToken cancellationToken = default)
    {
        if ( !ReleaseManifest.IsSha256(expectedManifestSha256) )
            throw new InvalidDataException("Embedded installer manifest digest is invalid.");
        var root = InstallRootLock.NormalizeRoot(installRoot);
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var actualDigest = SHA256.HashData(manifestBytes);
        if ( !CryptographicOperations.FixedTimeEquals(actualDigest, Convert.FromHexString(expectedManifestSha256)) )
            throw new InvalidDataException("Installer payload manifest does not match its embedded SHA-256 digest.");

        var manifest = JsonSerializer.Deserialize<InstallerPayloadManifest>(manifestBytes) ??
                       throw new InvalidDataException("Installer payload manifest is empty.");
        if ( manifest.SchemaVersion != 1 || manifest.Version != version ||
             !SemanticVersion.TryParse(version, out _) || manifest.Entries.Count == 0 )
            throw new InvalidDataException("Installer payload manifest identity is invalid.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedStagedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasLauncher = false;
        var hasUpdater = false;
        var hasApplication = false;
        foreach ( var entry in manifest.Entries )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = ValidateRelativePath(entry.Path);
            if ( !seen.Add(relative) || entry.Size < 0 || !ReleaseManifest.IsSha256(entry.Sha256) )
                throw new InvalidDataException($"Installer payload manifest entry is invalid: {entry.Path}");

            string destination;
            if ( string.Equals(relative, "Juju.Launcher.exe", StringComparison.OrdinalIgnoreCase) )
            {
                destination = Path.Combine(root, relative);
                hasLauncher = true;
            }
            else if ( string.Equals(relative, "Juju.Updater.exe", StringComparison.OrdinalIgnoreCase) )
            {
                destination = Path.Combine(root, relative);
                hasUpdater = true;
            }
            else if ( relative.StartsWith($"versions/{version}/", StringComparison.OrdinalIgnoreCase) )
            {
                var appRelative = relative[($"versions/{version}/").Length..];
                destination = Path.Combine(stagedVersionDirectory, appRelative);
                expectedStagedFiles.Add(Path.GetFullPath(destination));
                if ( string.Equals(appRelative, "Juju.App.exe", StringComparison.OrdinalIgnoreCase) )
                    hasApplication = true;
            }
            else if ( !relative.Contains('/') )
            {
                destination = Path.Combine(root, relative);
            }
            else
            {
                throw new InvalidDataException($"Installer manifest path is outside the expected payload: {entry.Path}");
            }

            InstallRootLock.EnsureNotReparsePoint(root, destination);
            var file = new FileInfo(destination);
            if ( !file.Exists || file.Length != entry.Size )
                throw new InvalidDataException($"Installer payload file is missing or has the wrong size: {entry.Path}");
            await using var input = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
            if ( !CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(entry.Sha256)) )
                throw new InvalidDataException($"Installer payload SHA-256 verification failed: {entry.Path}");
        }

        if ( !hasLauncher || !hasUpdater || !hasApplication )
            throw new InvalidDataException("Installer manifest omits a required executable.");

        InstallRootLock.EnsureNotReparsePoint(root, stagedVersionDirectory);
        var stagedFileCount = 0;
        foreach ( var file in EnumerateFilesSafely(root, stagedVersionDirectory) )
        {
            stagedFileCount++;
            if ( !expectedStagedFiles.Contains(Path.GetFullPath(file)) )
                throw new InvalidDataException($"Unexpected stale file is present in the staged payload: {file}");
        }
        if ( stagedFileCount != expectedStagedFiles.Count )
            throw new InvalidDataException("Staged payload does not contain exactly the manifest file set.");
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root, string directory)
    {
        InstallRootLock.EnsureNotReparsePoint(root, directory);
        foreach ( var entry in Directory.EnumerateFileSystemEntries(directory) )
        {
            InstallRootLock.EnsureNotReparsePoint(root, entry);
            if ( Directory.Exists(entry) )
            {
                foreach ( var nested in EnumerateFilesSafely(root, entry) )
                    yield return nested;
            }
            else if ( File.Exists(entry) )
                yield return entry;
        }
    }

    private static string ValidateRelativePath(string path)
    {
        if ( string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') ||
             path.Contains('\\') || path.Split('/').Any(part => part is "" or "." or "..") ||
             path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 )
            throw new InvalidDataException($"Unsafe installer payload path: {path}");
        return path;
    }
}
