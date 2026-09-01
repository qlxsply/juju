using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App.Data;

internal sealed class WorkCalendarRepository(HolidayCalendarDownloadService downloader)
{
    public void EnsureYear(int year) => downloader.EnsureYear(year);
    public IReadOnlyList<HolidayCalendarDay> GetYear(int year) { downloader.EnsureYear(year); return downloader.ReadLocal(year); }
    public HolidayCalendarDay? Get(DateOnly date) => GetYear(date.Year).FirstOrDefault(day => day.Date == date);
    public void Upsert(HolidayCalendarDay item) { var days = GetYear(item.Date.Year).Where(day => day.Date != item.Date).Append(item).OrderBy(day => day.Date).ToArray(); Save(item.Date.Year, days); }
    public void Delete(DateOnly date) => Save(date.Year, GetYear(date.Year).Where(day => day.Date != date).ToArray());
    public void Download(int year) => downloader.DownloadAndSave(year);
    private void Save(int year, IReadOnlyList<HolidayCalendarDay> days) => downloader.SaveLocal(year, days);
}
