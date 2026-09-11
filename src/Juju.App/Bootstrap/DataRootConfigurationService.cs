using Juju.Core.Settings;
using Juju.Core.Storage;
using System.IO;

namespace Juju.App.Bootstrap;

public sealed class DataRootConfigurationService(DataRootService roots, ISettingsService settings)
{
    public Task<DataRootValidationResult> ValidateAsync(string path, bool initialize, CancellationToken cancellationToken) => roots.ValidateAsync(path, initialize, cancellationToken);

    public async Task UseAsync(string path, CancellationToken cancellationToken)
    {
        var result = await roots.ValidateAsync(path, false, cancellationToken);
        if (!result.IsValid) throw new InvalidOperationException(result.Error);
        await settings.SaveAsync(settings.Current with { DataRoot = result.Path! }, cancellationToken);
    }

    public async Task MigrateAsync(string destination, CancellationToken cancellationToken)
    {
        var target = await roots.ValidateAsync(destination, true, cancellationToken);
        if (!target.IsValid) throw new InvalidOperationException(target.Error);
        var source = settings.Current.DataRoot;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var output = Path.Combine(target.Path!, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using var input = File.OpenRead(file);
            await using var outputStream = File.Create(output);
            await input.CopyToAsync(outputStream, cancellationToken);
        }
        await UseAsync(target.Path!, cancellationToken);
    }
}
