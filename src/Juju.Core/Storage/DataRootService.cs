using System.Text.Json;
using Juju.Core.Errors;

namespace Juju.Core.Storage;

public sealed class DataRootService(IAtomicFileWriter writer)
{
    public async Task EnsureInitializedAsync(string dataRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "juju.json");
        if (!File.Exists(marker))
        {
            await writer.WriteTextAsync(marker, JsonSerializer.Serialize(new { schemaVersion = 1 }), cancellationToken);
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(marker, cancellationToken));
                if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1)
                    throw new JujuException(ErrorCode.DataRootInvalid, "The selected data root has an unsupported schema.");
            }
            catch (JsonException ex) { throw new JujuException(ErrorCode.DataRootInvalid, "The selected data root marker is invalid.", ex); }
        }
        Directory.CreateDirectory(Path.Combine(root, "json", "documents"));
        Directory.CreateDirectory(Path.Combine(root, "json", ".trash"));
    }
}
