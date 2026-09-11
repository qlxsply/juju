using System.Text.Json;
using Juju.Core.Errors;

namespace Juju.Core.Storage;

public sealed class DataRootService(IAtomicFileWriter writer)
{
    public async Task<DataRootValidationResult> ValidateAsync(string dataRoot, bool initializeEmptyDirectory = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataRoot)) return new(null, false, false, "A data root is required.");
        try
        {
            var root = Path.GetFullPath(dataRoot);
            if (!Directory.Exists(root))
            {
                if (!initializeEmptyDirectory) return new(root, false, false, "The data root does not exist.");
                Directory.CreateDirectory(root);
            }

            var marker = Path.Combine(root, "juju.json");
            if (!File.Exists(marker))
            {
                if (Directory.EnumerateFileSystemEntries(root).Any())
                    return new(root, false, false, "A non-empty directory without juju.json cannot be initialized.");
                if (!initializeEmptyDirectory) return new(root, false, true, "The empty directory has not been initialized.");
                await writer.WriteTextAsync(marker, JsonSerializer.Serialize(new { schemaVersion = 1 }), cancellationToken);
            }
            else
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(marker, cancellationToken));
                    if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                        return new(root, false, false, "The selected data root has an unsupported schema.");
                }
                catch (JsonException) { return new(root, false, false, "The selected data root marker is invalid."); }
            }

            Directory.CreateDirectory(Path.Combine(root, "json", "documents"));
            Directory.CreateDirectory(Path.Combine(root, "json", ".trash"));
            return new(root, true, false, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(null, false, false, "The data root is unavailable.");
        }
    }

    public async Task EnsureInitializedAsync(string dataRoot, CancellationToken cancellationToken = default)
    {
        var result = await ValidateAsync(dataRoot, initializeEmptyDirectory: true, cancellationToken);
        if (result.IsValid) return;
        throw new JujuException(result.Path is null ? ErrorCode.DataRootUnavailable : ErrorCode.DataRootInvalid, result.Error ?? "The data root is invalid.");
    }
}

public sealed record DataRootValidationResult(string? Path, bool IsValid, bool IsEmpty, string? Error);
