using Juju.Core.Settings;
using Juju.Core.Storage;
using System.IO;

namespace Juju.App.Bootstrap;

public sealed class DataRootConfigurationService(DataRootService roots, IStorageManager storage, ISettingsService settings)
{
    public Task<DataRootValidationResult> ValidateAsync(string path, bool initialize, CancellationToken cancellationToken) => roots.ValidateAsync(path, initialize, cancellationToken);

    public async Task UseAsync(string path, CancellationToken cancellationToken)
    {
        var result = await roots.ValidateAsync(path, false, cancellationToken);
        if (!result.IsValid) throw new InvalidOperationException(result.Error);
        await storage.UseExistingDataRootAsync(result.Path!, cancellationToken);
        await settings.SaveAsync(settings.Current with { DataRoot = storage.DataRoot! }, cancellationToken);
    }

    public async Task MigrateAsync(string destination, CancellationToken cancellationToken)
    {
        await storage.MigrateToAsync(destination, cancellationToken);
        await settings.SaveAsync(settings.Current with { DataRoot = storage.DataRoot! }, cancellationToken);
    }
}
