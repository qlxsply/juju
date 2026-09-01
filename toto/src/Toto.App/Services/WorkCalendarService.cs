using Toto.App.Data;

namespace Toto.App.Services;

internal sealed class WorkCalendarService(WorkCalendarRepository repository)
{
    public bool IsWorkday(DateOnly date)
    {
        var day = repository.Get(date);
        return day is null ? date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday : !day.IsOffDay;
    }
}
