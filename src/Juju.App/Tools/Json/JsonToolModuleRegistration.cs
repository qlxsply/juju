using Juju.App.Tools.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Tools.Json;

public static class JsonToolModuleRegistration
{
    public static IServiceCollection AddJsonToolModule(this IServiceCollection services)
    {
        services.AddSingleton<IToolModule, JsonToolModule>();
        return services;
    }
}
