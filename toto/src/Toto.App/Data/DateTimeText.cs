using System.Globalization;

namespace Toto.App.Data;

internal static class DateTimeText
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";
    public static string Text(DateTime? value) => value?.ToString(Format, CultureInfo.InvariantCulture) ?? "";
    public static DateTime? Read(string? value) => string.IsNullOrWhiteSpace(value) ? null : DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
}
