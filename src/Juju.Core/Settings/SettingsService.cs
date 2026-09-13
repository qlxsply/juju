using System.Text.Json;
using Juju.Core.Storage;

namespace Juju.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public sealed class SettingsService(IAtomicFileWriter writer) : ISettingsService
{
    private static readonly JsonSerializerOptions Options = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _path = ApplicationPaths.ConfigurationFile;
    public AppSettings Current { get; private set; } = AppSettings.CreateDefault();

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

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        await writer.WriteTextAsync(_path, JsonSerializer.Serialize(settings, Options), cancellationToken);
    }
}