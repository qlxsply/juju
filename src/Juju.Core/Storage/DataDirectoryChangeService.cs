using System.Text.Json;
using Juju.Core.Settings;

namespace Juju.Core.Storage;

/// <summary>
/// 仅暂存数据根目录变更。正常关机时初始化新目录并更新安装目录中的指针，不迁移源目录文件。
/// </summary>
public sealed class DataDirectoryChangeService(DataRootService roots, IAtomicFileWriter writer,
    string? activeRoot = null, string? pointerFile = null)
{
    private string _activeRoot = Path.GetFullPath(activeRoot ?? ApplicationPaths.DataRoot);
    private readonly string _pointerFile = pointerFile ?? ApplicationPaths.DataRootPointerFile;
    private string? _nextRoot;

    public string DisplayRoot => _nextRoot ?? _activeRoot;
    public string? PendingRoot => _nextRoot;

    public string DisplayToolDirectory(string name) => Path.Combine(_activeRoot, "tools", name);

    /// <summary>设置下次启动的数据根目录；返回是否改变了待应用路径。</summary>
    public bool StageRoot(string path)
    {
        var target = Validate(path, _activeRoot, allowExistingValidRoot: true);
        if ( string.Equals(target, DisplayRoot, StringComparison.OrdinalIgnoreCase) )
            return false;

        _nextRoot = string.Equals(target, _activeRoot, StringComparison.OrdinalIgnoreCase) ? null : target;
        return true;
    }

    /// <summary>启动时从当前根目录配置中恢复尚未应用的路径变更。</summary>
    public void LoadPending(string? root)
    {
        _nextRoot = !string.IsNullOrWhiteSpace(root) &&
                    !string.Equals(Path.GetFullPath(root), _activeRoot, StringComparison.OrdinalIgnoreCase)
            ? Validate(root, _activeRoot, allowExistingValidRoot: true)
            : null;
    }

    private static string Validate(string path, string source, bool allowExistingValidRoot = false)
    {
        if ( string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) )
            throw new InvalidOperationException("请输入绝对目录路径。");
        var target = Path.GetFullPath(path.Trim());
        if ( target.StartsWith(Path.TrimEndingDirectorySeparator(source) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) )
            throw new InvalidOperationException("新目录不能位于当前目录内。");
        if ( !Directory.Exists(target) )
            return target;
        if ( new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint) )
            throw new InvalidOperationException("数据目录不能是符号链接或联接目录。");
        if ( string.Equals(target, source, StringComparison.OrdinalIgnoreCase) )
            return target;
        if ( Directory.EnumerateFileSystemEntries(target).Any() &&
             !(allowExistingValidRoot && HasValidMarker(target)) )
            throw new InvalidOperationException("目标目录必须为空或已有有效的 juju.json 标记。");
        return target;
    }

    private static bool HasValidMarker(string directory)
    {
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "juju.json")));
            return marker.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1;
        }
        catch { return false; }
    }

    public async Task CommitAsync(ISettingsService settings, CancellationToken cancellationToken)
    {
        if ( _nextRoot is not { } destinationRoot )
            return;

        var previous = settings.Current;
        await roots.EnsureInitializedAsync(destinationRoot, cancellationToken);

        // 清除旧根目录中的待处理设置后再提交指针。指针失败时恢复设置，留待下次重试。
        await settings.SaveAsync(previous with { PendingDataRoot = null }, cancellationToken);
        try
        {
            await writer.WriteTextAsync(_pointerFile,
                JsonSerializer.Serialize(new ApplicationPaths.DataRootPointer(destinationRoot)), cancellationToken);
            _activeRoot = destinationRoot;
            _nextRoot = null;
        }
        catch
        {
            try { await settings.SaveAsync(previous, CancellationToken.None); }
            catch { /* Keep the pointer unchanged and surface the original commit error. */ }
            throw;
        }
    }
}
