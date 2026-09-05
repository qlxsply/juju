using Toto.App.Data;

namespace Toto.App.Services;

/// <summary>根据周末规则和节假日覆盖记录判断某日是否为工作日。</summary>
/// <remarks>主构造函数参数 <c>repository</c> 由编译器保存供实例成员使用，避免 Java 风格的样板构造函数。</remarks>
internal sealed class WorkCalendarService(WorkCalendarRepository repository)
{
    /// <summary>如果该日不是周末且没有休息日覆盖，或存在上班日覆盖，则返回 <see langword="true"/>。</summary>
    public bool IsWorkday(DateOnly date)
    {
        var day = repository.Get(date);
        return !day?.IsOffDay ?? date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
    }
}
