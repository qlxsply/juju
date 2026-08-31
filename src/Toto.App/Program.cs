using Toto.App.Data;
using Toto.App.Services;

namespace Toto.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var paths = new AppPaths(); var log = new AppLog(paths.LogDirectory); Application.ThreadException += (_, e) => log.Write($"Unhandled UI exception: {e.Exception.Message}"); AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Unhandled exception: {e.ExceptionObject}");
        if (!SingleInstanceService.TryCreate(out var instance)) return;
        using (instance)
        try { var database = new TotoDatabase(paths); new DatabaseInitializer(database).Initialize(); var items = new ItemRepository(database); var settings = new SettingsRepository(database); new LegacyMigrationService(paths, database, items, log).MigrateIfNeeded(); using var context = new TotoApplicationContext(paths, log, items, settings, new WorkCalendarRepository(database)); instance!.ShowRequested += context.ShowMain; Application.Run(context); }
        catch (Exception ex) { log.Write($"Startup failed: {ex}"); MessageBox.Show(ex.Message, "toto", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
