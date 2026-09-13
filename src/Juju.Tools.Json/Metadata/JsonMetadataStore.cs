using System.Text.Json;
using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Metadata;

public sealed class JsonMetadataStore(string dataRoot, IAtomicFileWriter writer)
{
    private static readonly JsonSerializerOptions Options = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Path => System.IO.Path.Combine(dataRoot, "json", "metadata.json");

    public async Task<JsonMetadata> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path)) return JsonMetadata.Empty();
        try
        {
            var metadata =
                JsonSerializer.Deserialize<JsonMetadata>(await File.ReadAllTextAsync(Path, cancellationToken), Options);
            return metadata is { SchemaVersion: 1 }
                ? metadata
                : throw new JsonException("Unsupported metadata schema.");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            var backup = Path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            try
            {
                File.Copy(Path, backup, overwrite: false);
            }
            catch
            {
                // ignored
            }

            throw new JujuException(ErrorCode.MetadataCorrupted, "JSON document metadata could not be read.", ex);
        }
    }

    public async Task UpdateAsync(Func<JsonMetadata, JsonMetadata> update,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteTextAsync(Path,
                JsonSerializer.Serialize(update(await ReadAsync(cancellationToken)), Options), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(JsonMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteTextAsync(Path, JsonSerializer.Serialize(metadata, Options), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}