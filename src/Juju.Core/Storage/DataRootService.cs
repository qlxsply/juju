using System.Text.Json;
using Juju.Core.Errors;

namespace Juju.Core.Storage;

/// <summary>
/// 验证并初始化数据根目录的边界服务。主构造函数注入原子写入器，以保证创建标记文件时
/// 也遵循安全发布语义。
/// </summary>
public sealed class DataRootService(IAtomicFileWriter writer)
{
    /// <summary>
    /// 将输入规范化为绝对路径，检查或创建 schema 标记，并确保 JSON 所需目录存在。
    /// 预期的取消会原样传播；可预见的文件系统和路径异常则转化为验证结果，供 UI 展示而非中断流程。
    /// </summary>
    public async Task<DataRootValidationResult> ValidateAsync(string dataRoot, bool initializeEmptyDirectory = false,
        CancellationToken cancellationToken = default)
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
                {
                    if (!initializeEmptyDirectory || !IsRecognizedLegacyDataRoot(root))
                        return new DataRootValidationResult(root, false, false,
                            $"The non-empty directory '{root}' is not a recognized Juju data root and cannot be initialized.");
                    // Older Juju installations used this layout before the root marker was introduced.
                    // Add the marker in place; existing user data is left untouched.
                    await writer.WriteTextAsync(marker, JsonSerializer.Serialize(new { schemaVersion = 1 }),
                        cancellationToken);
                }
                else if (!initializeEmptyDirectory)
                    return new DataRootValidationResult(root, false, true,
                        "The empty directory has not been initialized.");
                else
                    await writer.WriteTextAsync(marker, JsonSerializer.Serialize(new { schemaVersion = 1 }),
                        cancellationToken);
            }
            else
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(marker, cancellationToken));
                    if (!document.RootElement.TryGetProperty("schemaVersion", out var version) ||
                        version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                        return new DataRootValidationResult(root, false, false,
                            "The selected data root has an unsupported schema.");
                }
                catch (JsonException)
                {
                    return new DataRootValidationResult(root, false, false,
                        "The selected data root marker is invalid.");
                }
            }

            Directory.CreateDirectory(Path.Combine(root, "tools", "json", "documents"));
            Directory.CreateDirectory(Path.Combine(root, "tools", "json", ".trash"));
            return new DataRootValidationResult(root, true, false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return new DataRootValidationResult(null, false, false, "The data root is unavailable.");
        }
    }

    /// <summary>要求目录有效；验证失败时将结果提升为带领域错误码的异常。</summary>
    public async Task EnsureInitializedAsync(string dataRoot, CancellationToken cancellationToken = default)
    {
        var result = await ValidateAsync(dataRoot, initializeEmptyDirectory: true, cancellationToken);
        if (result.IsValid) return;
        throw new JujuException(result.Path is null ? ErrorCode.DataRootUnavailable : ErrorCode.DataRootInvalid,
            result.Error ?? "The data root is invalid.");
    }

    private static bool IsRecognizedLegacyDataRoot(string root)
    {
        var entries = Directory.EnumerateFileSystemEntries(root).ToArray();
        var allowedRootEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "config.json", "tools", "app", "layout-migration-v1.json" };
        if (entries.Any(entry => !allowedRootEntries.Contains(Path.GetFileName(entry)))) return false;

        var config = Path.Combine(root, "config.json");
        var tools = Path.Combine(root, "tools");
        if (!File.Exists(config) || !Directory.Exists(tools)) return false;
        var toolDirectories = Directory.EnumerateFileSystemEntries(tools).ToArray();
        var supportedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "json", "toto", "echo-http", "kairos" };
        if (toolDirectories.Length == 0 || toolDirectories.Any(path =>
                !Directory.Exists(path) || !supportedTools.Contains(Path.GetFileName(path)))) return false;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(config));
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("theme", out _) &&
                   document.RootElement.TryGetProperty("toolKeys", out var keys) &&
                   keys.ValueKind == JsonValueKind.Object;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>目录验证的无异常结果值；<c>Path</c> 为 null 通常表示路径本身无法访问或规范化。</summary>
public sealed record DataRootValidationResult(string? Path, bool IsValid, bool IsEmpty, string? Error);
