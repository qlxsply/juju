using System.Text.Json;
using Juju.Core.Settings;

namespace Juju.Core.Storage;

/// <summary>
/// 目录变更先在内存中排队；退出时各工具停止写文件之后才复制并切换，下次启动加载新路径。
/// 复制失败不会提交路径指针或工具覆盖配置，现有数据继续可用。
/// </summary>
public sealed class DataDirectoryChangeService(DataRootService roots, IAtomicFileWriter writer,
    string? activeRoot = null, string? pointerFile = null)
{
    private readonly string _activeRoot = activeRoot ?? ApplicationPaths.DataRoot;
    private readonly string _pointerFile = pointerFile ?? ApplicationPaths.DataRootPointerFile;
    private string? _nextRoot;
    private readonly Dictionary<string, string> _nextTools = new(StringComparer.OrdinalIgnoreCase);
    private bool _pendingRequested;
    public string DisplayRoot => _nextRoot ?? _activeRoot;
    public string? PendingRoot => _nextRoot;
    public IReadOnlyDictionary<string, string> PendingTools => _nextTools;
    public string DisplayToolDirectory(string name) => _nextTools.TryGetValue(name, out var path)
        ? path : ToolDirectory(name);

    private string ToolDirectory(string name)
    {
        var configured = activeRoot is null
            ? ApplicationPaths.ToolDataDirectory(name)
            : Path.Combine(_activeRoot, "tools", name);
        var currentDefault = Path.Combine(_activeRoot, "tools", name);
        // 数据根预定迁移时，默认工具路径随根目录联动；显式自定义目录保持原样。
        return _nextRoot is not null && string.Equals(configured, currentDefault, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_nextRoot, "tools", name)
            : configured;
    }

    public void StageRoot(string path)
    {
        _pendingRequested = true;
        var target = Validate(path, _activeRoot, allowExistingValidRoot: true);
        if (new[] { "json", "toto", "echohttp", "kairos" }.Any(name =>
                string.Equals(target, DisplayToolDirectory(name), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("工具箱数据根目录不能与工具数据目录相同。");
        _nextRoot = string.Equals(target, _activeRoot, StringComparison.OrdinalIgnoreCase)
            ? null : target;
    }

    public void StageTool(string name, string path)
    {
        _pendingRequested = true;
        var current = ToolDirectory(name);
        var target = ValidateToolCandidate(name, path);
        if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase)) _nextTools.Remove(name);
        else _nextTools[name] = target;
    }

    public void ValidateTool(string name, string path) => _ = ValidateToolCandidate(name, path);

    /// <summary>启动时恢复 config.json 中的迁移意图；仍在当前根目录运行，退出时安全复制并切换。</summary>
    public void LoadPending(string? root, IReadOnlyDictionary<string, string>? tools)
    {
        _nextRoot = !string.IsNullOrWhiteSpace(root) &&
                    !string.Equals(Path.GetFullPath(root), _activeRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(root) : null;
        _nextTools.Clear();
        if (tools is not null)
            foreach (var (name, path) in tools)
                if (!string.IsNullOrWhiteSpace(path) &&
                    !string.Equals(path, ToolDirectory(name), StringComparison.OrdinalIgnoreCase))
                    _nextTools[name] = Path.GetFullPath(path);
        _pendingRequested = !string.IsNullOrWhiteSpace(root) || (tools?.Count ?? 0) > 0;
    }

    private string ValidateToolCandidate(string name, string path)
    {
        var target = Validate(path, ToolDirectory(name), allowEmptySubdirectories: true, allowExistingToolData: true);
        foreach (var other in new[] { "json", "toto", "echohttp", "kairos" }.Where(other => other != name))
            if (string.Equals(target, DisplayToolDirectory(other), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不同工具不能共用同一个数据目录。");
        if (string.Equals(target, DisplayRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("工具数据目录不能直接使用工具箱数据根目录。");
        return target;
    }

    private static string Validate(string path, string source, bool allowEmptySubdirectories = false,
        bool allowExistingValidRoot = false, bool allowExistingToolData = false)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("请输入绝对目录路径。");
        var target = Path.GetFullPath(path.Trim());
        if (target.StartsWith(Path.GetFullPath(source) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("新目录不能位于当前目录内。");
        if (!Directory.Exists(target)) return target;
        if (new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("数据目录不能是符号链接或联接目录。");
        if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) return target;
        var hasEntries = allowEmptySubdirectories
            ? Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any()
            : Directory.EnumerateFileSystemEntries(target).Any();
        if (hasEntries && !allowExistingToolData && !(allowExistingValidRoot && HasValidMarker(target)))
            throw new InvalidOperationException("工具箱数据根必须为空或已有有效的 juju.json 标记。");
        return target;
    }

    private static bool HasValidMarker(string directory)
    {
        try
        {
            using var marker = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(directory, "juju.json")));
            return marker.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1;
        }
        catch { return false; }
    }

    public async Task CommitAsync(ISettingsService settings, CancellationToken cancellationToken)
    {
        if (!_pendingRequested && _nextRoot is null && _nextTools.Count == 0) return;
        var destinationRoot = _nextRoot ?? _activeRoot;
        var previous = settings.Current;
        try
        {
            if (_nextRoot is not null)
            {
                await roots.EnsureInitializedAsync(destinationRoot, cancellationToken);
                await DataLayoutMigration.CopyMissingAsync(_activeRoot, destinationRoot, cancellationToken);
            }
            var overrides = new Dictionary<string, string>(previous.ToolDataDirectories, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, destination) in _nextTools)
            {
                var source = ToolDirectory(name);
                await DataLayoutMigration.CopyMissingAsync(source, destination, cancellationToken);
                if (string.Equals(destination, Path.Combine(destinationRoot, "tools", name),
                        StringComparison.OrdinalIgnoreCase)) overrides.Remove(name);
                else overrides[name] = destination;
            }
            // 自定义工具目录未变但整个根移动时，仍要复制外部工具目录吗？它保留原路径，不需要复制。
            var nextSettings = previous with
            {
                ToolDataDirectories = overrides,
                PendingDataRoot = null,
                PendingToolDataDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            await settings.SaveAsync(nextSettings, cancellationToken);
            if (_nextRoot is not null)
            {
                // 目标 config.json 必须包含刚提交的工具覆盖目录，指针必须最后落盘。
                await writer.WriteTextAsync(Path.Combine(destinationRoot, "config.json"),
                    await File.ReadAllTextAsync(Path.Combine(_activeRoot, "config.json"), cancellationToken), cancellationToken);
                await writer.WriteTextAsync(_pointerFile,
                    JsonSerializer.Serialize(new ApplicationPaths.DataRootPointer(destinationRoot)), cancellationToken);
            }
        }
        catch
        {
            try { await settings.SaveAsync(previous, CancellationToken.None); }
            catch { /* 保留原始目录迁移错误；源目录与数据文件不会被删除。 */ }
            throw;
        }
    }
}
