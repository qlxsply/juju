using System.Globalization;

namespace Toto.App.Data;

/// <summary>在持久化文件中以固定、与区域设置无关的格式读写日期时间。</summary>
internal static class DateTimeText
{
    /// <summary>CSV 和 INI 文件使用的日期时间格式。</summary>
    public const string Format = "yyyy-MM-dd HH:mm:ss";
    /// <summary>将可选日期时间格式化为空字符串或固定文本。</summary>
    // C# 的 DateTime? 是可空值类型；不同于 Java 引用，未设置时不分配对象。
    public static string Text(DateTime? value) => value?.ToString(Format, CultureInfo.InvariantCulture) ?? "";

    /// <summary>将固定格式文本解析为日期时间；空白或无效文本返回空值。</summary>
    public static DateTime? Read(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : DateTime.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
}
