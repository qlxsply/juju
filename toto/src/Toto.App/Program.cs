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
        try { var items = new ItemRepository(paths); var settings = new SettingsRepository(paths); items.EnsureFiles(); settings.EnsureExists(); var calendar = new WorkCalendarRepository(new HolidayCalendarDownloadService(paths)); calendar.GetYear(DateTime.Today.Year); var schedulerWindowEnd = DateTime.Today.AddDays(7); if (schedulerWindowEnd.Year != DateTime.Today.Year) calendar.GetYear(schedulerWindowEnd.Year); using var context = new TotoApplicationContext(paths, log, items, settings, calendar); instance!.SetShowHandler(context.ShowMain); Application.Run(context); }
        catch (Exception ex) { log.Write($"Startup failed: {ex}"); MessageBox.Show(ex.Message, "toto", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
