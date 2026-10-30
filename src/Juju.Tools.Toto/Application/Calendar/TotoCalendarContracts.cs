namespace Juju.Tools.Toto.Application.Calendar;

public sealed record TotoHolidaySnapshot(string Name, DateOnly Date, bool IsOffDay);

public interface ITotoCalendarApplicationService
{
    bool CalendarReady { get; }
    int LatestCalendarYear { get; }
    IReadOnlyList<TotoHolidaySnapshot> GetHolidays(int year);
    Task DownloadHolidaysAsync(int year);
    void SaveHoliday(TotoHolidaySnapshot holiday);
    void DeleteHoliday(DateOnly date);
}
