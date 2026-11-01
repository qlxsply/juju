using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Juju.Update;

public sealed record ReleaseManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; }
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("channel")] public string Channel { get; init; } = "";
    [JsonPropertyName("rid")] public string Rid { get; init; } = "";
    [JsonPropertyName("packageAsset")] public string PackageAsset { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("minimumLauncherVersion")] public string MinimumLauncherVersion { get; init; } = "";
    [JsonPropertyName("releaseNotesUrl")] public string? ReleaseNotesUrl { get; init; }

    public SemanticVersion ParsedVersion => SemanticVersion.TryParse(Version, out var version)
        ? version
        : throw new InvalidDataException("Release manifest contains an invalid stable version.");
    public SemanticVersion ParsedMinimumLauncherVersion =>
        SemanticVersion.TryParse(MinimumLauncherVersion, out var version)
            ? version
            : throw new InvalidDataException("Release manifest contains an invalid minimum launcher version.");

    public void Validate(string expectedVersion, string expectedRid = "win-x64")
    {
        if ( SchemaVersion != 1 || Channel != "stable" || Rid != expectedRid ||
             !SemanticVersion.TryParse(Version, out _) || !string.Equals(Version, expectedVersion, StringComparison.Ordinal) ||
             !IsSafeAssetName(PackageAsset) || PackageAsset.Length == 0 || !IsSha256(Sha256) ||
             !SemanticVersion.TryParse(MinimumLauncherVersion, out _) )
            throw new InvalidDataException("Release manifest is invalid or does not match the requested release.");

        if ( ReleaseNotesUrl is not null &&
             (!Uri.TryCreate(ReleaseNotesUrl, UriKind.Absolute, out var notes) || notes.Scheme != Uri.UriSchemeHttps ||
              !string.Equals(notes.Host, "github.com", StringComparison.OrdinalIgnoreCase)) )
            throw new InvalidDataException("Release notes URL is not a trusted GitHub HTTPS URL.");
    }

    public static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    public static bool IsSafeAssetName(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 200 &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains('/') && !value.Contains('\\') && value is not "." and not "..";

    public static ReleaseManifest Deserialize(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<ReleaseManifest>(json) ?? throw new InvalidDataException("Release manifest is empty.");
}

public interface IPackageSignatureVerifier
{
    Task VerifyAsync(string packagePath, CancellationToken cancellationToken = default);
}

public sealed class NoOpPackageSignatureVerifier : IPackageSignatureVerifier
{
    public Task VerifyAsync(string packagePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class PackageHashVerifier
{
    public static async Task VerifyAsync(string packagePath, string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if ( !ReleaseManifest.IsSha256(expectedSha256) )
            throw new InvalidDataException("Expected SHA-256 value is invalid.");
        await using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        if ( !CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedSha256)) )
            throw new InvalidDataException("Downloaded update package SHA-256 does not match the release manifest.");
    }
}
