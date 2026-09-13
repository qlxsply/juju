using Juju.Core.Errors;
using Juju.Core.Storage;
using Juju.Core.Telemetry;
using Juju.Tools.Json.Metadata;
using Juju.Tools.Json.Storage;

namespace Juju.Tools.Json.Documents;

/// <summary>
/// JSON 文档的列表、编辑、导入与文件系统同步契约。异步方法均接收可选取消令牌；实现应将正常的
/// <see cref="OperationCanceledException"/> 原样传播，而非包装成业务错误。
/// </summary>
public interface IJsonDocumentService
{
    /// <summary>当外部进程改变已知文档时触发。</summary>
    event EventHandler<JsonDocumentExternalChange>? ExternalChanged;

    /// <summary>协调磁盘和元数据后按显示顺序返回文档列表。</summary>
    Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>读取文档内容及其当前乐观并发版本。</summary>
    Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default);

    /// <summary>创建默认内容的新文档及对应元数据。</summary>
    Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>仅当当前版本仍等于 <paramref name="expectedRevision"/> 时保存，防止覆盖外部修改。</summary>
    Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision,
        CancellationToken cancellationToken = default);

    /// <summary>无视版本冲突直接覆盖内容，供用户明确确认后的恢复操作使用。</summary>
    Task OverwriteAsync(JsonDocumentId id, string content, CancellationToken cancellationToken = default);

    /// <summary>验证文件名并重命名文档，同时更新元数据。</summary>
    Task RenameAsync(JsonDocumentId id, string name, CancellationToken cancellationToken = default);

    /// <summary>将文档移入回收目录并从元数据移除。</summary>
    Task DeleteAsync(JsonDocumentId id, CancellationToken cancellationToken = default);

    /// <summary>将文档移至有效的零基顺序位置并重编号全部文档。</summary>
    Task ReorderAsync(JsonDocumentId id, int order, CancellationToken cancellationToken = default);

    /// <summary>导入多个 JSON 文件，必要时为重名文件添加后缀。</summary>
    Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件型 JSON 文档仓储的应用服务，协调内容、metadata.json、修订版本和文件观察器。
/// <see cref="SemaphoreSlim"/> 将每个多文件业务操作串行化，避免元数据与文件状态交叉更新；
/// 它是可跨 <c>await</c> 使用的异步锁，而不是 Java synchronized/C# lock。
/// </summary>
public sealed class JsonDocumentService : IJsonDocumentService, IAsyncDisposable
{
    private readonly string _documents;
    private readonly string _trash;
    private readonly JsonMetadataStore _metadata;
    private readonly IAtomicFileWriter _writer;

    private readonly DocumentRevisionService _revisions;

    // 创建、保存、重命名、删除、导入、排序和协调共享这把锁，保护跨文件的不变量。
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly JsonDocumentWatcher _watcher;
    private readonly IPerformanceTelemetry _telemetry;

    /// <summary>构造路径、协作者和文件观察器；未提供遥测时采用空对象，避免调用点判空。</summary>
    public JsonDocumentService(string dataRoot, JsonMetadataStore metadata, IAtomicFileWriter writer,
        DocumentRevisionService revisions, IPerformanceTelemetry? telemetry = null)
    {
        _documents = Path.Combine(dataRoot, "json", "documents");
        _trash = Path.Combine(dataRoot, "json", ".trash");
        _metadata = metadata;
        _writer = writer;
        _revisions = revisions;
        _telemetry = telemetry ?? NullPerformanceTelemetry.Instance;
        _watcher = new JsonDocumentWatcher(_documents, revisions, FindIdByFileName);
        _watcher.Changed += (sender, change) => ExternalChanged?.Invoke(this, change);
    }

    /// <summary>透传已由 <see cref="JsonDocumentWatcher"/> 确认的外部变更。</summary>
    public event EventHandler<JsonDocumentExternalChange>? ExternalChanged;

    /// <summary>先协调文件系统和元数据，再按持久化顺序返回不可修改的数组快照。</summary>
    public async Task<IReadOnlyList<JsonDocumentMetadata>> ListAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileAsync(cancellationToken);
        return (await _metadata.ReadAsync(cancellationToken)).Documents.OrderBy(document => document.Order).ToArray();
    }

    /// <summary>
    /// 读取指定文档及其修订戳，并测量加载耗时。底层 IO/权限错误包装为 DocumentReadFailed，
    /// 但取消会自然穿过异常过滤器。
    /// </summary>
    public async Task<JsonDocumentSnapshot> OpenAsync(JsonDocumentId id, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentLoad);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            if (!File.Exists(path))
                throw new JujuException(ErrorCode.DocumentNotFound, "The document file no longer exists.");
            return new JsonDocumentSnapshot(id, await File.ReadAllTextAsync(path, cancellationToken),
                await _revisions.GetAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JujuException(ErrorCode.DocumentReadFailed, "The document could not be read.", ex);
        }
    }

    /// <summary>
    /// 在操作锁内选择当天未占用的递增文件名，原子写入 <c>{}</c> 后保存元数据。
    /// 元数据保存失败时尽力删除新文件，实现局部补偿而非留下孤立文档。
    /// </summary>
    public async Task<JsonDocumentMetadata> CreateAsync(CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var next = metadata.Sequence.Date == today ? metadata.Sequence.Last + 1 : 1;
            string fileName;
            do
            {
                fileName = $"{today}{next:D3}.json";
                next++;
            } while (File.Exists(DocumentPath(fileName)));

            var sequence = next - 1;
            var now = DateTimeOffset.UtcNow;
            var document = new JsonDocumentMetadata(JsonDocumentId.New(), fileName, metadata.Documents.Count, now, now,
                new("created"));
            var path = DocumentPath(fileName);
            _watcher.MarkSelfWrite(path);
            await _writer.WriteTextAsync(path, "{}", cancellationToken);
            try
            {
                await _metadata.SaveAsync(
                    metadata with { Sequence = new(today, sequence), Documents = [.. metadata.Documents, document] },
                    cancellationToken);
            }
            catch
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignored
                }

                throw;
            }

            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be created.", ex);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>
    /// 执行乐观并发保存：写入前读取实际修订戳，若与编辑器打开时的版本不同则报告冲突。
    /// 成功后更新元数据时间并登记自身写入，以免观察器将它误报为外部修改。
    /// </summary>
    public async Task SaveAsync(JsonDocumentId id, string content, DocumentRevision expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            if (!Equals(await _revisions.GetAsync(path, cancellationToken), expectedRevision))
                throw new JujuException(ErrorCode.ExternalModificationConflict,
                    "The file changed outside juju and was not overwritten.");
            _watcher.MarkSelfWrite(path);
            await _writer.WriteTextAsync(path, content, cancellationToken);
            await _metadata.UpdateAsync(
                current => current with
                {
                    Documents = current.Documents.Select(item =>
                        item.Id == id ? item with { UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList()
                }, cancellationToken);
            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be saved.", ex);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>跳过版本比较直接原子保存，适用于用户显式选择覆盖外部修改的场景。</summary>
    public async Task OverwriteAsync(JsonDocumentId id, string content, CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentSave);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var path = DocumentPath(document.FileName);
            await _writer.WriteTextAsync(path, content, cancellationToken);
            _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            await _metadata.UpdateAsync(
                current => current with
                {
                    Documents = current.Documents.Select(item =>
                        item.Id == id ? item with { UpdatedAtUtc = DateTimeOffset.UtcNow } : item).ToList()
                }, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new JujuException(ErrorCode.DocumentWriteFailed, "The document could not be saved.", ex);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>
    /// 验证新文件名、移动文件并更新元数据。后续元数据操作失败时尽力将文件移回，
    /// 以维持文件名和元数据的一致性；无法恢复的 IO 错误带有 DocumentRenameFailed 错误码。
    /// </summary>
    public async Task RenameAsync(JsonDocumentId id, string name, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var targetName = JsonDocumentName.Normalize(name);
            if (string.Equals(targetName, document.FileName, StringComparison.OrdinalIgnoreCase)) return;
            var oldPath = DocumentPath(document.FileName);
            var targetPath = DocumentPath(targetName);
            if (File.Exists(targetPath))
                throw new JujuException(ErrorCode.DocumentAlreadyExists, "A document with that name already exists.");
            try
            {
                File.Move(oldPath, targetPath);
                await _metadata.UpdateAsync(
                    current => current with
                    {
                        Documents = current.Documents.Select(item =>
                            item.Id == id
                                ? item with { FileName = targetName, UpdatedAtUtc = DateTimeOffset.UtcNow }
                                : item).ToList()
                    }, cancellationToken);
                _watcher.RegisterSelfWrite(targetPath, await _revisions.GetAsync(targetPath, cancellationToken));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(targetPath) && !File.Exists(oldPath)) File.Move(targetPath, oldPath);
                }
                catch
                {
                    // ignored
                }

                throw new JujuException(ErrorCode.DocumentRenameFailed, "The document could not be renamed.", ex);
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>
    /// 将文件移至带时间戳的回收目录而非直接删除，随后删除元数据并重编号。
    /// 若更新元数据失败，尽力把文件移回源路径。
    /// </summary>
    public async Task DeleteAsync(JsonDocumentId id, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var document = await GetAsync(id, cancellationToken);
            var source = DocumentPath(document.FileName);
            var trash = Path.Combine(_trash, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}__{document.FileName}");
            try
            {
                File.Move(source, trash);
                await _metadata.UpdateAsync(
                    current => current with
                    {
                        Documents = current.Documents.Where(item => item.Id != id)
                            .Select((item, order) => item with { Order = order }).ToList()
                    }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(trash) && !File.Exists(source)) File.Move(trash, source);
                }
                catch
                {
                    // ignored
                }

                throw new JujuException(ErrorCode.DocumentDeleteFailed, "The document could not be deleted.", ex);
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>
    /// 在单一操作锁内导入扩展名为 .json 的文件。逐项检查取消、解决重名并原子复制内容；
    /// 最终元数据提交失败时删除已复制文件作为补偿。
    /// </summary>
    public async Task<IReadOnlyList<JsonDocumentMetadata>> ImportAsync(IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentImport);
        var imported = new List<JsonDocumentMetadata>();
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var copiedPaths = new List<string>();
            foreach (var source in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetExtension(source), ".json", StringComparison.OrdinalIgnoreCase)) continue;
                var baseName = JsonDocumentName.Normalize(Path.GetFileName(source));
                var candidate = baseName;
                for (var suffix = 2;
                     File.Exists(DocumentPath(candidate)) ||
                     metadata.Documents.Any(item =>
                         string.Equals(item.FileName, candidate, StringComparison.OrdinalIgnoreCase)) ||
                     imported.Any(item => string.Equals(item.FileName, candidate, StringComparison.OrdinalIgnoreCase));
                     suffix++) candidate = $"{Path.GetFileNameWithoutExtension(baseName)}-{suffix}.json";
                try
                {
                    await _writer.WriteTextAsync(DocumentPath(candidate),
                        await File.ReadAllTextAsync(source, cancellationToken), cancellationToken);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new JujuException(ErrorCode.ImportFailed, "A document could not be imported.", ex);
                }

                copiedPaths.Add(DocumentPath(candidate));
                var now = DateTimeOffset.UtcNow;
                var item = new JsonDocumentMetadata(JsonDocumentId.New(), candidate,
                    metadata.Documents.Count + imported.Count, now, now, new("imported", Path.GetFileName(source)));
                imported.Add(item);
            }

            if (imported.Count > 0)
            {
                try
                {
                    await _metadata.SaveAsync(metadata with { Documents = [.. metadata.Documents, .. imported] },
                        cancellationToken);
                }
                catch
                {
                    foreach (var path in copiedPaths)
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                            // ignored
                        }

                    throw;
                }

                foreach (var path in copiedPaths)
                    _watcher.RegisterSelfWrite(path, await _revisions.GetAsync(path, cancellationToken));
            }

            return imported;
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>校验目标索引后移动列表元素，并持久化连续的零基显示顺序。</summary>
    public async Task ReorderAsync(JsonDocumentId id, int order, CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            var metadata = await _metadata.ReadAsync(cancellationToken);
            var documents = metadata.Documents.OrderBy(item => item.Order).ToList();
            var current = documents.FindIndex(item => item.Id == id);
            if (current < 0)
                throw new JujuException(ErrorCode.DocumentNotFound, "The selected document no longer exists.");
            if (order < 0 || order >= documents.Count)
                throw new JujuException(ErrorCode.InvalidDocumentOrder,
                    "The document order is outside the available range.");
            var document = documents[current];
            documents.RemoveAt(current);
            documents.Insert(order, document);
            await _metadata.SaveAsync(
                metadata with { Documents = documents.Select((item, index) => item with { Order = index }).ToList() },
                cancellationToken);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>从当前元数据查找文档，不存在时以稳定领域错误码失败。</summary>
    private async Task<JsonDocumentMetadata> GetAsync(JsonDocumentId id, CancellationToken cancellationToken) =>
        (await _metadata.ReadAsync(cancellationToken)).Documents.SingleOrDefault(document => document.Id == id)
        ?? throw new JujuException(ErrorCode.DocumentNotFound, "The selected document no longer exists.");

    /// <summary>二次规范化文件名并在完整路径层再次阻止逃出 documents 目录。</summary>
    private string DocumentPath(string fileName)
    {
        var safeName = JsonDocumentName.Normalize(fileName);
        var path = Path.GetFullPath(Path.Combine(_documents, safeName));
        if (!path.StartsWith(Path.GetFullPath(_documents) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new JujuException(ErrorCode.InvalidDocumentName, "Document path escapes the documents directory.");
        return path;
    }

    /// <summary>
    /// 在列表前以磁盘事实修复元数据：丢失文件被移除，未知文件被恢复登记，序号和显示顺序随之重建。
    /// 损坏元数据会视为空快照恢复，原文件的诊断备份由元数据存储负责。
    /// </summary>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var _ = _telemetry.Measure(PerformanceOperation.DocumentReconcile);
        await _operations.WaitAsync(cancellationToken);
        try
        {
            JsonMetadata metadata;
            try
            {
                metadata = await _metadata.ReadAsync(cancellationToken);
            }
            catch (JujuException ex) when (ex.Code == ErrorCode.MetadataCorrupted)
            {
                metadata = JsonMetadata.Empty();
            }

            var files = Directory.EnumerateFiles(_documents, "*.json").Select(Path.GetFileName).OfType<string>()
                .OrderBy(file => File.GetCreationTimeUtc(DocumentPath(file)), Comparer<DateTime>.Default)
                .ThenBy(file => file, StringComparer.Ordinal).ToArray();
            var existing = metadata.Documents
                .Where(document => files.Contains(document.FileName, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var file in files.Where(file =>
                         existing.All(document =>
                             !string.Equals(document.FileName, file, StringComparison.OrdinalIgnoreCase))))
            {
                var stamp = new DateTimeOffset(File.GetCreationTimeUtc(DocumentPath(file)));
                existing.Add(new(JsonDocumentId.New(), file, existing.Count, stamp, stamp, new("recovered")));
            }

            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var recoveredLast = files.Select(file => TryGetSequence(file, today)).DefaultIfEmpty(0).Max();
            var sequence = new JsonSequence(today,
                metadata.Sequence.Date == today ? Math.Max(metadata.Sequence.Last, recoveredLast) : recoveredLast);
            var reconciled = metadata with
            {
                Sequence = sequence,
                Documents = existing.Select((document, order) => document with { Order = order }).ToList()
            };
            if (!Equals(metadata, reconciled)) await _metadata.SaveAsync(reconciled, cancellationToken);
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>从当天默认命名的文件中提取数值序号；格式不匹配时返回零。</summary>
    private static int TryGetSequence(string fileName, string date) =>
        fileName.StartsWith(date, StringComparison.Ordinal) &&
        int.TryParse(Path.GetFileNameWithoutExtension(fileName)[date.Length..], out var value)
            ? value
            : 0;

    /// <summary>
    /// 为同步 FileSystemWatcher 回调查找文档 ID。观察器委托不是异步签名，故在此同步等待元数据任务；
    /// 任何失败返回 null，让调用方忽略无法可靠归属的通知。
    /// </summary>
    private JsonDocumentId? FindIdByFileName(string fileName)
    {
        try
        {
            return _metadata.ReadAsync().GetAwaiter().GetResult().Documents.SingleOrDefault(item =>
                string.Equals(item.FileName, fileName, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 先异步释放文件观察器，再释放操作锁。该顺序阻止已销毁的协作者仍向服务发出回调；
    /// <c>await using</c> 会在作用域结束时调用此方法。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _watcher.DisposeAsync();
        _operations.Dispose();
    }
}