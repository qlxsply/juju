using Juju.Tools.Toto.Application.Ports;
using Juju.Tools.Toto.Application.Reminders;
using Juju.Tools.Toto.Infrastructure.Calendar;
using Juju.Tools.Toto.Infrastructure.Items;
using Juju.Tools.Toto.Infrastructure.Settings;
using Juju.Tools.Toto.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Juju.Tools.Toto.Application;

public static class TotoApplicationRegistration
{
    public static IServiceCollection AddTotoApplicationServices(this IServiceCollection services, string dataDirectory)
    {
        services.AddSingleton(new TotoPaths(dataDirectory));
        services.AddSingleton<ItemRepository>();
        services.AddSingleton<ITotoItemRepository>(provider => provider.GetRequiredService<ItemRepository>());
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ITotoSettingsRepository>(provider => provider.GetRequiredService<SettingsRepository>());
        services.AddSingleton<HolidayCalendarDownloadService>();
        services.AddSingleton<WorkCalendarRepository>();
        services.AddSingleton<ITotoCalendarRepository>(provider => provider.GetRequiredService<WorkCalendarRepository>());
        services.AddSingleton<TotoReminderRuntime>(provider => new TotoReminderRuntime(
            provider.GetRequiredService<ITotoItemRepository>(),
            provider.GetRequiredService<ITotoSettingsRepository>(),
            provider.GetRequiredService<ITotoCalendarRepository>(),
            provider.GetRequiredService<Juju.Core.Contracts.Platform.ISystemLifecycleEvents>(),
            provider.GetRequiredService<ILogger<TotoReminderRuntime>>()));
        services.AddSingleton<ITotoRuntimeControl>(provider => provider.GetRequiredService<TotoReminderRuntime>());
        services.AddSingleton<ITotoApplicationService, TotoApplicationService>();
        return services;
    }
}
