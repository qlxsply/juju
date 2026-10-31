using System.Globalization;
using System.Text.RegularExpressions;

namespace Juju.Tools.Toto.Application.Items;

public static class QuickItemParser
{
    public static bool TryParse(string input, int defaultMinutes, out string content, out DateTime? plannedAt,
        out DateTime? remindAt, out string error, DateTime? referenceTime = null)
    {
        content = "";
        plannedAt = null;
        remindAt = null;
        error = "";
        if (string.IsNullOrWhiteSpace(input) || input.Contains('\r') || input.Contains('\n'))
        {
            error = "事项内容不能为空且不能包含换行。";
            return false;
        }
        var parts = input.Trim().Split('@');
        if (parts.Length > 3 || string.IsNullOrWhiteSpace(parts[0]))
        {
            error = "请输入 @ 前面的事项内容。";
            return false;
        }
        content = parts[0].Trim();
        if (parts.Length == 1) return true;
        var token = parts[1].Trim();
        if (token.Length == 0)
        {
            error = "请输入计划时间。";
            return false;
        }
        if (!TryParsePlan(token, referenceTime ?? DateTime.Now, out var plan, out error)) return false;

        var minutes = defaultMinutes;
        // An optional third segment overrides the configured default reminder lead time.
        if (parts.Length == 3 && (!int.TryParse(parts[2], out minutes) || minutes < 0))
        {
            error = "提前提醒分钟数必须是非负整数。";
            return false;
        }
        if (minutes < 0 || plan <= DateTime.MinValue.AddMinutes(minutes))
        {
            error = "提前提醒分钟数超出有效范围。";
            return false;
        }
        plannedAt = plan;
        remindAt = plan.AddMinutes(-minutes);
        return true;
    }

    private static bool TryParsePlan(string token, DateTime now, out DateTime plannedAt, out string error)
    {
        plannedAt = default;
        error = "";
        var calendarDate = now.Date;
        string clock;
        try
        {
            var weekly = Regex.Match(token, @"^(W+)([1-7])([0-9]{4})$", RegexOptions.IgnoreCase);
            var daily = Regex.Match(token, @"^(\++)([0-9]{4})$");
            if (weekly.Success)
            {
                var mondayOffset = ((int)now.DayOfWeek + 6) % 7;
                calendarDate = calendarDate.AddDays(-mondayOffset + weekly.Groups[1].Length * 7 +
                                                    int.Parse(weekly.Groups[2].Value) - 1);
                clock = weekly.Groups[3].Value;
            }
            else if (daily.Success)
            {
                calendarDate = calendarDate.AddDays(daily.Groups[1].Length);
                clock = daily.Groups[2].Value;
            }
            else if (Regex.IsMatch(token, @"^[0-9]{4}$")) clock = token;
            else if (Regex.IsMatch(token, @"^[0-9]{6}$"))
            {
                if (!TryDate(now.Year, now.Month, int.Parse(token[..2]), out calendarDate))
                {
                    error = "日期不合法。";
                    return false;
                }
                clock = token[2..];
            }
            else if (Regex.IsMatch(token, @"^[0-9]{8}$"))
            {
                if (!TryDate(now.Year, int.Parse(token[..2]), int.Parse(token[2..4]), out calendarDate))
                {
                    error = "日期不合法。";
                    return false;
                }
                clock = token[4..];
            }
            else if (Regex.IsMatch(token, @"^[0-9]{12}$"))
            {
                if (!TryDate(int.Parse(token[..4]), int.Parse(token[4..6]), int.Parse(token[6..8]),
                        out calendarDate))
                {
                    error = "日期不合法。";
                    return false;
                }
                clock = token[8..];
            }
            else
            {
                error = "不支持的时间格式。";
                return false;
            }

            if (!TimeOnly.TryParseExact(clock, "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var time))
            {
                error = "时间必须使用 24 小时制（00:00–23:59）。";
                return false;
            }
            plannedAt = calendarDate.Add(time.ToTimeSpan());
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "日期不合法。";
            return false;
        }
    }

    private static bool TryDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;
        date = new DateTime(year, month, day);
        return true;
    }
}
