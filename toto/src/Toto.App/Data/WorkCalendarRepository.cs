using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App.Data;

/// <summary>为工作日判断提供节假日数据的读取、更新和下载入口。</summary>
/// <remarks>该类型使用 C# 主构造函数保存下载服务依赖，效果类似 Java 中显式赋值的构造函数。</remarks>
internal sealed class WorkCalendarRepository(HolidayCalendarDownloadService downloader)
{
    /// <summary>确保指定年份的数据已存在，必要时下载。</summary>
    public void EnsureYear(int year) => downloader.EnsureYear(year);

    /// <summary>获取指定年份的所有节假日和调休记录。</summary>
    public IReadOnlyList<HolidayCalendarDay> GetYear(int year)
    {
        downloader.EnsureYear(year);
        return downloader.ReadLocal(year);
    }

    /// <summary>获取指定日期的覆盖记录；不存在时返回空值。</summary>
    public HolidayCalendarDay? Get(DateOnly date) => GetYear(date.Year).FirstOrDefault(day => day.Date == date);

    /// <summary>新增或替换某日的节假日记录。</summary>
    public void Upsert(HolidayCalendarDay item)
    {
        var days = GetYear(item.Date.Year).Where(day => day.Date != item.Date).Append(item).OrderBy(day => day.Date)
            .ToArray();
        Save(item.Date.Year, days);
    }

    /// <summary>删除指定日期的覆盖记录。</summary>
    public void Delete(DateOnly date) => Save(date.Year, GetYear(date.Year).Where(day => day.Date != date).ToArray());

    /// <summary>从远程数据源下载并覆盖指定年份的数据。</summary>
    public void Download(int year) => downloader.DownloadAndSave(year);

    /// <summary>将指定年份的记录委派给本地下载服务保存。</summary>
    private void Save(int year, IReadOnlyList<HolidayCalendarDay> days) => downloader.SaveLocal(year, days);
}
