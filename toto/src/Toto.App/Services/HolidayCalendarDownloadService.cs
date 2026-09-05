using System.Net.Http.Json;
using System.Text.Json;
using Toto.App.Domain;

namespace Toto.App.Services;

/// <summary>下载、验证并以 JSON 文件保存中国法定节假日数据。</summary>
/// <remarks>主构造函数参数 <c>paths</c> 提供数据文件位置，等同于 Java 中手写依赖字段与构造函数。</remarks>
internal sealed class HolidayCalendarDownloadService(AppPaths paths)
{
    private const string UrlFormat = "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>确保指定年份的本地 JSON 数据存在；缺失时下载。</summary>
    public void EnsureYear(int year)
    {
        if (File.Exists(paths.CalendarPath(year))) return;
        DownloadAndSave(year);
    }

    /// <summary>下载指定年份的数据，验证其内容，然后原子保存到本地。</summary>
    public void DownloadAndSave(int year)
    {
        var url = string.Format(UrlFormat, year);
        try
        {
            // using 声明会在当前方法作用域结束时 Dispose HttpClient，类似 Java try-with-resources。
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            // System.Net.Http.Json 直接按 JsonSerializerOptions 反序列化响应，类似 Java 中 ObjectMapper.readValue。
            var source = client.GetFromJsonAsync<HolidaySource>(url, JsonOptions).GetAwaiter().GetResult();
            if (source is null || source.Year != year || source.Days is null)
                throw new InvalidOperationException("上游文件内容无效。");
            SaveLocal(year, source.Days.Select(day => ToLocalDay(day, year)).OrderBy(day => day.Date).ToArray());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException
                                              or InvalidOperationException)
        {
            throw new InvalidOperationException($"本地 {year}.json 不存在，且无法从 {url} 下载有效的法定节假日数据。请检查网络后重试，或手动创建该文件。",
                exception);
        }
    }

    /// <summary>读取并验证指定年份的本地节假日 JSON 数据。</summary>
    public IReadOnlyList<HolidayCalendarDay> ReadLocal(int year)
    {
        var path = paths.CalendarPath(year);
        try
        {
            // JsonSerializer 根据属性名策略在 JSON 和 C# record 属性之间映射字段。
            var days = JsonSerializer.Deserialize<HolidayCalendarDay[]>(File.ReadAllText(path), JsonOptions);
            if (days is null || days.Any(day => day.Date.Year != year || string.IsNullOrWhiteSpace(day.Name)))
                throw new JsonException();
            return days.OrderBy(day => day.Date).ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"本地节假日文件无效：{path}。请删除或修复该文件后重试。", exception);
        }
    }

    /// <summary>验证并原子保存指定年份的本地节假日数据。</summary>
    public void SaveLocal(int year, IReadOnlyList<HolidayCalendarDay> days)
    {
        if (days.Any(day => day.Date.Year != year || string.IsNullOrWhiteSpace(day.Name)))
            throw new InvalidOperationException($"{year} 年本地节假日数据格式无效。");
        var path = paths.CalendarPath(year);
        Directory.CreateDirectory(paths.DataDirectory);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(days.OrderBy(day => day.Date), JsonOptions));
        File.Move(temporary, path, true);
    }

    /// <summary>将远程数据项验证并转换为本地领域对象。</summary>
    private static HolidayCalendarDay ToLocalDay(HolidaySourceDay source, int year)
    {
        if (string.IsNullOrWhiteSpace(source.Name) || !DateOnly.TryParse(source.Date, out var date) ||
            date.Year != year || source.IsOffDay is null) throw new InvalidOperationException($"{year} 年节假日数据格式无效。");
        return new HolidayCalendarDay(source.Name, date, source.IsOffDay.Value);
    }

    /// <summary>表示远程 JSON 文档的顶层结构，仅用于反序列化。</summary>
    private sealed class HolidaySource
    {
        /// <summary>远程数据所属年份。</summary>
        public int Year { get; init; }

        /// <summary>远程日历项集合；缺失时为 <see langword="null"/>。</summary>
        public HolidaySourceDay[]? Days { get; init; }
    }

    /// <summary>表示远程 JSON 中的单个节假日或调休记录。</summary>
    private sealed class HolidaySourceDay
    {
        /// <summary>节假日或调休名称。</summary>
        public string? Name { get; init; }

        /// <summary>ISO 日期文本。</summary>
        public string? Date { get; init; }

        /// <summary>是否为休息日；缺失表示远程数据无效。</summary>
        public bool? IsOffDay { get; init; }
    }
}