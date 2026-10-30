using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Tools.Toto;

public static class TotoToolModuleRegistration
{
    public static IServiceCollection AddTotoToolModule(this IServiceCollection services)
    {
        services.AddSingleton<TotoSettingsSection>();
        services.AddSingleton<IToolModule, TotoToolModule>();
        return services;
    }
}
