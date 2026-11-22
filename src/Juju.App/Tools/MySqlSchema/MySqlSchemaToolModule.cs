using Juju.App.Contracts.Windows;
using Juju.App.Contracts.Settings;
using Juju.App.Presentation.MySqlSchema;
using Juju.App.Tools.Contracts;
using Juju.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Tools.MySqlSchema;

public sealed class MySqlSchemaToolModule(IMySqlSchemaWindowFactory window) : IToolModule
{
    public ToolDescriptor Descriptor { get; } = new(ToolId.MySqlSchema, BackgroundCapability.Unsupported,
        StorageCapability.File);

    public LauncherToolMetadata Launcher { get; } = new(ToolId.MySqlSchema, 2, "MySQL Schema",
        "管理 MySQL 连接、结构备份与差异对比", "打开 MySQL Schema", null, "DB", "#5275D8", 15,
        ToolLaunchKeys.MySqlSchema.ToString(), true);

    public IReadOnlyCollection<ISettingsSection> SettingsSections { get; } = [];
    public ToolReadiness CheckReadiness() => new(true);
    public void Open() => window.Show();
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class MySqlSchemaToolModuleRegistration
{
    public static IServiceCollection AddMySqlSchemaToolModule(this IServiceCollection services)
    {
        services.AddSingleton<IMySqlSchemaWindowFactory, MySqlSchemaWindowFactory>();
        services.AddSingleton<IToolModule, MySqlSchemaToolModule>();
        return services;
    }
}
