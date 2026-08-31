using System.Globalization;
using Toto.App.Domain;

namespace Toto.App.Services;

internal static class QuickItemParser
{
    public static bool TryParse(string input, int defaultMinutes, out string content, out DateTime? plannedAt, out DateTime? remindAt, out string error)
    {
        content = string.Empty; plannedAt = null; remindAt = null; error = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Contains('\r') || input.Contains('\n')) { error = "事项内容不能为空且不能包含换行。"; return false; }
        var parts = input.Trim().Split('@'); if (parts.Length > 3 || string.IsNullOrWhiteSpace(parts[0])) { error = "事项内容不能包含 @，输入最多有两个 @ 分隔符。"; return false; }
        content = parts[0].Trim(); if (parts.Length == 1) return true; if (!TryParsePlan(parts[1].Trim(), out var plan)) { error = "计划时间无效或已过去。"; return false; }
        var minutes = defaultMinutes; if (parts.Length == 3 && (!int.TryParse(parts[2], out minutes) || minutes < 0)) { error = "提前提醒分钟数必须是非负整数。"; return false; }
        plannedAt = plan; remindAt = plan.AddMinutes(-minutes); return true;
    }

    private static bool TryParsePlan(string value, out DateTime date)
    {
        date = default; var now = DateTime.Now; if (value.Length >= 5 && value.TrimStart('+').Length == 4 && value.TakeWhile(x => x == '+').Any()) { var plus = value.TakeWhile(x => x == '+').Count(); return DateTime.TryParseExact(now.Date.AddDays(plus).ToString("yyyyMMdd") + value[plus..], "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date > now; }
        var prefix = value.Length switch { 4 => now.ToString("yyyyMMdd"), 6 => now.ToString("yyyyMM"), 8 => now.ToString("yyyy"), 12 => "", _ => null }; if (prefix is null || !DateTime.TryParseExact(prefix + value, "yyyyMMddHHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false; return date > now;
    }
}
