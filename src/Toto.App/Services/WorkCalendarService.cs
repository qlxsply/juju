using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.Services;

internal sealed class WorkCalendarService(WorkCalendarRepository repository)
{
    public bool IsWorkday(DateOnly date) => repository.Get(date)?.DayType == DayType.Workday || (repository.Get(date) is null && date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);
}
