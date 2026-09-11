using System.Security.Cryptography;
using System.Text;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Storage;

public sealed class DocumentRevisionService
{
    public async Task<DocumentRevision> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new(info.Length, info.LastWriteTimeUtc, hash);
    }
}
