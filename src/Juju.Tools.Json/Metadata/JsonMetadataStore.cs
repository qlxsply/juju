using System.Text.Json;
using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Tools.Json.Documents;

namespace Juju.Tools.Json.Metadata;

/// <summary>
/// 读写 JSON 文档元数据文件的存储边界。主构造函数把数据根目录和原子写入器固定在实例上，
/// 避免服务通过全局路径工作。
/// </summary>
public sealed class JsonMetadataStore(string dataRoot, IAtomicFileWriter writer)
{
    private static readonly JsonSerializerOptions Options = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    // 元数据“读-修改-写”必须作为整体串行化；SemaphoreSlim 可在 await 之间安全持有。
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Path => System.IO.Path.Combine(dataRoot, "json", "metadata.json");

    /// <summary>
    /// 异步读取并验证 schemaVersion。损坏或无法读取时会尽力保留带时间戳的副本，再抛出
    /// <see cref="ErrorCode.MetadataCorrupted"/>；取消未被捕获，按调用方请求传播。
    /// </summary>
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
                // 备份只是诊断辅助；不能掩盖原始元数据读取失败。
            }

            throw new JujuException(ErrorCode.MetadataCorrupted, "JSON document metadata could not be read.", ex);
        }
    }

    /// <summary>在异步锁内执行读取、纯转换函数和原子写入，避免并发更新丢失。</summary>
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

    /// <summary>在同一把异步锁内原子写入完整元数据快照。</summary>
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