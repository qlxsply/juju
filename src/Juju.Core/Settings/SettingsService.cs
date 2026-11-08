using System.Text.Json;
using Juju.Core.Storage;
using Juju.Core.Tools;

namespace Juju.Core.Settings;

/// <summary>应用设置的加载和原子保存契约。</summary>
public interface ISettingsService
{
    /// <summary>内存中的当前设置快照。</summary>
    AppSettings Current { get; }

    /// <summary>从磁盘加载设置；首次运行时创建默认配置。</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>更新内存快照并异步持久化设置。</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>以 camelCase、缩进 JSON 序列化应用设置的文件实现。</summary>
public sealed class SettingsService(IAtomicFileWriter writer, string? configurationFile = null) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _path = configurationFile ?? ApplicationPaths.ConfigurationFile;
    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    /// <summary>异步读取配置；不存在时写入默认值，取消令牌贯穿读取和首次保存。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if ( !File.Exists(_path) )
        {
            await SaveAsync(Current, cancellationToken);
            return;
        }

        var json = await File.ReadAllTextAsync(_path, cancellationToken);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options) ??
                     throw new InvalidDataException("应用配置文件为空或无效。");
        Current = Normalize(loaded);
        await WriteCanonicalConfigurationIfChangedAsync(json, cancellationToken);
    }

    /// <summary>先更新当前快照，再委托原子写入器避免磁盘上出现部分 JSON。</summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(normalized, Options), cancellationToken);
        Current = normalized;
    }

    private static AppSettings Normalize(AppSettings settings) => settings with
    {
        ToolKeys = ToolLaunchKeys.Normalize(settings.ToolKeys),
        LeaderShortcut = GlobalHotkeyPolicy.Normalize(settings.LeaderShortcut),
        ToolGlobalShortcuts = NormalizeExtraShortcuts(settings.ToolGlobalShortcuts)
    };

    private async Task WriteCanonicalConfigurationIfChangedAsync(string original,
        CancellationToken cancellationToken)
    {
        var normalized = JsonSerializer.Serialize(Current, Options);
        if ( !string.Equals(original, normalized, StringComparison.Ordinal) )
            await writer.WriteTextAsync(_path, normalized, cancellationToken);
    }

    private static Dictionary<string, string> NormalizeExtraShortcuts(IReadOnlyDictionary<string, string>? values) =>
        new[] { Juju.Core.Tools.ToolId.EchoHttp, Juju.Core.Tools.ToolId.Kairos }
            .ToDictionary(tool => tool.ToString(), tool => GlobalHotkeyPolicy.Normalize(
                values is not null && values.TryGetValue(tool.ToString(), out var key) ? key : null,
                optional: true), StringComparer.OrdinalIgnoreCase);
}
