using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Infrastructure.Calendar;
using Juju.Tools.Toto.Infrastructure.Items;
using Juju.Tools.Toto.Infrastructure.Settings;
using Juju.Tools.Toto.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Toto.App;
using Toto.App.Services;

namespace Juju.Tools.Toto.Application;

public static class TotoApplicationRegistration
{
    public static IServiceCollection AddTotoApplicationServices(this IServiceCollection services,
        string dataDirectory, Func<IServiceProvider, SynchronizationContext> synchronizationContextFactory)
    {
        services.AddSingleton(new TotoPaths(dataDirectory));
        services.AddSingleton<ItemRepository>();
        services.AddSingleton<ITotoItemRepository>(provider => provider.GetRequiredService<ItemRepository>());
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ITotoSettingsRepository>(provider => provider.GetRequiredService<SettingsRepository>());
        services.AddSingleton<HolidayCalendarDownloadService>();
        services.AddSingleton<WorkCalendarRepository>();
        services.AddSingleton<ITotoCalendarRepository>(provider => provider.GetRequiredService<WorkCalendarRepository>());
        services.AddSingleton<TotoToolHost>(provider =>
        {
            var paths = provider.GetRequiredService<TotoPaths>();
            return new TotoToolHost(synchronizationContextFactory(provider), paths,
                provider.GetRequiredService<ITotoItemRepository>(),
                provider.GetRequiredService<ITotoSettingsRepository>(),
                provider.GetRequiredService<ITotoCalendarRepository>(), new AppLog(paths.LogDirectory));
        });
        services.AddSingleton<ITotoRuntimeControl>(provider => provider.GetRequiredService<TotoToolHost>());
        services.AddSingleton<ITotoApplicationService, TotoApplicationService>();
        return services;
    }
}
