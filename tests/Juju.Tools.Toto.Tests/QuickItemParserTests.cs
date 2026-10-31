using Juju.Tools.Toto.Application.Items;
using Xunit;

namespace Juju.Tools.Toto.Tests;

public sealed class QuickItemParserTests
{
    private static readonly DateTime Reference = new(2026, 10, 22, 10, 0, 0);

    [Theory]
    [InlineData("评审 @1600", 2026, 10, 22, 16, 0)]
    [InlineData("评审 @261600", 2026, 10, 26, 16, 0)]
    [InlineData("评审 @11051600", 2026, 11, 5, 16, 0)]
    [InlineData("评审 @202702281600", 2027, 2, 28, 16, 0)]
    [InlineData("评审 @+1600", 2026, 10, 23, 16, 0)]
    [InlineData("评审 @++1600", 2026, 10, 24, 16, 0)]
    [InlineData("评审 @W11600", 2026, 10, 26, 16, 0)]
    [InlineData("评审 @WW51600", 2026, 11, 6, 16, 0)]
    [InlineData("评审 @WWW71600", 2026, 11, 15, 16, 0)]
    public void Parses_all_quick_time_forms_and_subtracts_default_reminder(string input, int year, int month, int day,
        int hour, int minute)
    {
        Assert.True(QuickItemParser.TryParse(input, 5, out var name, out var plan, out var remind,
            out var error, Reference), error);
        Assert.Equal("评审", name);
        Assert.Equal(new DateTime(year, month, day, hour, minute, 0), plan);
        Assert.Equal(plan!.Value.AddMinutes(-5), remind);
    }

    [Fact]
    public void No_plan_has_no_reminder()
    {
        Assert.True(QuickItemParser.TryParse("没有时间的事项", 5, out var name, out var plan, out var remind,
            out var error, Reference));
        Assert.Equal("没有时间的事项", name);
        Assert.Null(plan);
        Assert.Null(remind);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("评审 @+0030", 2025, 1, 1, 0, 30)]
    [InlineData("评审 @++0030", 2025, 1, 2, 0, 30)]
    [InlineData("评审 @202402290005", 2024, 2, 29, 0, 5)]
    [InlineData("评审 @W11600", 2025, 1, 6, 16, 0)]
    public void Calculates_cross_year_and_leap_day_reminder(string input, int year, int month, int day, int hour, int minute)
    {
        Assert.True(QuickItemParser.TryParse(input, 5, out _, out var plan, out var remind,
            out _, new DateTime(2024, 12, 31, 23, 0, 0)));
        Assert.Equal(new DateTime(year, month, day, hour, minute, 0), plan);
        Assert.Equal(plan!.Value.AddMinutes(-5), remind);
    }

    [Theory]
    [InlineData("评审 @")]
    [InlineData("@1600")]
    [InlineData("评审 @2460")]
    [InlineData("评审 @202702291600")]
    [InlineData("评审 @320900")]
    [InlineData("评审 @02300900")]
    [InlineData("评审 @W01600")]
    [InlineData("评审 @W81600")]
    [InlineData("评审 @+2500")]
    [InlineData("评审 @abc")]
    [InlineData("评审 @２０２６１０２２１６００")]
    public void Invalid_dates_and_times_do_not_silently_roll_forward(string input)
    {
        Assert.False(QuickItemParser.TryParse(input, 5, out _, out var plan, out var remind,
            out var error, Reference));
        Assert.Null(plan);
        Assert.Null(remind);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Explicit_reminder_minutes_override_configured_default()
    {
        Assert.True(QuickItemParser.TryParse("会议 @1600@10", 5, out _, out var plan, out var remind,
            out _, Reference));
        Assert.Equal(plan!.Value.AddMinutes(-10), remind);
    }
}
