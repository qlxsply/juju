using Juju.App.Bootstrap;
using Juju.App.Settings;
using Juju.App.Tools.Contracts;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;
using Juju.Tools.Toto.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Juju.App.Tools.Placeholders;

public static class PlaceholderToolModuleRegistration
{
    public static IServiceCollection AddPlaceholderToolModule(this IServiceCollection services, ToolId id,
        string name, string description, string glyph, string glyphColor, double glyphFontSize, char defaultKey)
    {
        var sectionId = id == ToolId.EchoHttp ? "echo-http" : "kairos";
        services.AddSingleton<PlaceholderToolSettingsSection>(provider => new PlaceholderToolSettingsSection(id,
            provider.GetRequiredService<ISettingsService>(), provider.GetRequiredService<DataDirectoryChangeService>(),
            (tool, shortcut) => provider.GetRequiredService<DesktopWindowManager>().TryRegisterToolHotkey(tool, shortcut),
            provider.GetRequiredService<SettingsDialogService>(), provider.GetRequiredService<IJsonToolSettingsService>(),
            provider.GetRequiredService<ITotoApplicationService>()));
        services.AddSingleton<IToolModule>(provider => new PlaceholderToolModule(id, name, description, glyph,
            glyphColor, glyphFontSize, defaultKey,
            provider.GetServices<PlaceholderToolSettingsSection>().Single(section => section.Id == sectionId)));
        return services;
    }
}
