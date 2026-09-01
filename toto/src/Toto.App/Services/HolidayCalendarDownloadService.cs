using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Toto.App.Domain;

namespace Toto.App.Services;

internal sealed class HolidayCalendarDownloadService(AppPaths paths)
{
    private const string UrlFormat = "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public void EnsureYear(int year)
    {
        if (File.Exists(paths.CalendarPath(year))) return;
        DownloadAndSave(year);
    }

    public void DownloadAndSave(int year)
    {
        var url = string.Format(UrlFormat, year);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var source = client.GetFromJsonAsync<HolidaySource>(url, JsonOptions).GetAwaiter().GetResult();
            if (source is null || source.Year != year || source.Days is null) throw new InvalidOperationException("上游文件内容无效。");
            SaveLocal(year, source.Days.Select(day => ToLocalDay(day, year)).OrderBy(day => day.Date).ToArray());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"本地 {year}.json 不存在，且无法从 {url} 下载有效的法定节假日数据。请检查网络后重试，或手动创建该文件。", exception);
        }
    }

    public IReadOnlyList<HolidayCalendarDay> ReadLocal(int year)
    {
        var path = paths.CalendarPath(year);
        try
        {
            var days = JsonSerializer.Deserialize<HolidayCalendarDay[]>(File.ReadAllText(path), JsonOptions);
            if (days is null || days.Any(day => day.Date.Year != year || string.IsNullOrWhiteSpace(day.Name))) throw new JsonException();
            return days.OrderBy(day => day.Date).ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"本地节假日文件无效：{path}。请删除或修复该文件后重试。", exception);
        }
    }

    public void SaveLocal(int year, IReadOnlyList<HolidayCalendarDay> days)
    {
        if (days.Any(day => day.Date.Year != year || string.IsNullOrWhiteSpace(day.Name))) throw new InvalidOperationException($"{year} 年本地节假日数据格式无效。");
        var path = paths.CalendarPath(year); Directory.CreateDirectory(paths.DataDirectory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(days.OrderBy(day => day.Date), JsonOptions));
        File.Move(temporary, path, true);
    }

    private static HolidayCalendarDay ToLocalDay(HolidaySourceDay source, int year)
    {
        if (string.IsNullOrWhiteSpace(source.Name) || !DateOnly.TryParse(source.Date, out var date) || date.Year != year || source.IsOffDay is null) throw new InvalidOperationException($"{year} 年节假日数据格式无效。");
        return new HolidayCalendarDay(source.Name, date, source.IsOffDay.Value);
    }

    private sealed class HolidaySource { public int Year { get; init; } public HolidaySourceDay[]? Days { get; init; } }
    private sealed class HolidaySourceDay { public string? Name { get; init; } public string? Date { get; init; } public bool? IsOffDay { get; init; } }
}
