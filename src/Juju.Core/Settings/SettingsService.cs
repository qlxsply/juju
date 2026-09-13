using System.Text.Json;
using Juju.Core.Storage;

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
public sealed class SettingsService(IAtomicFileWriter writer) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _path = ApplicationPaths.ConfigurationFile;
    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

    /// <summary>异步读取配置；不存在时写入默认值，取消令牌贯穿读取和首次保存。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            await SaveAsync(Current, cancellationToken);
            return;
        }

        var loaded =
            JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(_path, cancellationToken), Options);
        if (loaded is not null) Current = loaded;
    }

    /// <summary>先更新当前快照，再委托原子写入器避免磁盘上出现部分 JSON。</summary>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(settings, Options), cancellationToken);
    }
}