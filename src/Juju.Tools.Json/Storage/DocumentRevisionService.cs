using System.Security.Cryptography;
using System.Text;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Storage;

/// <summary>从文件内容和属性生成乐观并发控制所需的版本戳。</summary>
public sealed class DocumentRevisionService
{
    /// <summary>
    /// 异步读取文本并计算 UTF-8 内容的 SHA-256。长度和修改时间能快速提供文件属性信息，
    /// 哈希则避免仅凭时间戳或长度漏掉内容变化；取消令牌传给异步读取。
    /// </summary>
    public async Task<DocumentRevision> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new DocumentRevision(info.Length, info.LastWriteTimeUtc, hash);
    }
}