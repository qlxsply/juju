using Toto.App.Data;
using Toto.App.Services;

namespace Toto.App;

/// <summary>
/// 定义 WinForms 应用程序的进程入口，并在消息循环开始前组装基础设施对象。
/// </summary>
internal static class Program
{
    /// <summary>启动应用程序的 UI 线程。</summary>
    // 与 Java 的普通 main 线程不同，WinForms 的 COM UI 线程必须标记为 STA。
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var paths = new AppPaths();
        var log = new AppLog(paths.LogDirectory);
        // C# 事件通过委托订阅；lambda 是该委托的匿名实现，类似 Java 的监听器回调。
        Application.ThreadException += (_, e) => log.Write($"Unhandled UI exception: {e.Exception.Message}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Write($"Unhandled exception: {e.ExceptionObject}");
        if (!SingleInstanceService.TryCreate(out var instance)) return;
        // using 在离开作用域时调用 IDisposable.Dispose，类似 Java 的 try-with-resources。
        using (instance)
            try
            {
                var items = new ItemRepository(paths);
                var settings = new SettingsRepository(paths);
                items.EnsureFiles();
                settings.EnsureExists();
                var calendar = new WorkCalendarRepository(new HolidayCalendarDownloadService(paths));
                calendar.GetYear(DateTime.Today.Year);
                var schedulerWindowEnd = DateTime.Today.AddDays(7);
                if (schedulerWindowEnd.Year != DateTime.Today.Year) calendar.GetYear(schedulerWindowEnd.Year);
                // using 声明会在当前 try 块结束时释放 ApplicationContext 及其托管资源。
                using var context = new TotoApplicationContext(paths, log, items, settings, calendar);
                instance!.SetShowHandler(context.ShowMain);
                Application.Run(context);
            }
            catch (Exception ex)
            {
                log.Write($"Startup failed: {ex}");
                MessageBox.Show(ex.Message, "toto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
    }
}
