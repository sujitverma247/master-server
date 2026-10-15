using MasterServer.Analytics;
using Xunit;

namespace MasterServer.Tests;

public class AnalyticsWindowTests
{
    [Fact]
    public void Clamp_days_to_one_through_one_year()
    {
        Assert.Equal(1, AnalyticsWindow.ClampDays(0));
        Assert.Equal(366, AnalyticsWindow.ClampDays(400));
        Assert.Equal(365, AnalyticsWindow.ClampDays(365));
        Assert.Equal(7, AnalyticsWindow.ClampDays(7));
    }

    [Fact]
    public void Custom_range_mirrors_length_for_previous_window()
    {
        Assert.True(AnalyticsWindow.TryParseDay("2026-10-01", out var from));
        Assert.True(AnalyticsWindow.TryParseDay("2026-10-10", out var to));
        Assert.Null(AnalyticsWindow.ValidateCustom(from, to));
        var (start, end, prev, days) = AnalyticsWindow.Custom(from, to);
        Assert.Equal(10, days);
        Assert.Equal(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc), prev);
        Assert.Equal(start, from);
    }

    [Fact]
    public void Series_is_daily_then_monthly_for_long_spans()
    {
        var from = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        var week = AnalyticsWindow.Series(from, from.AddDays(7));
        Assert.Equal(7, week.Count);
        Assert.Equal("2026-10-09", week[0].Label);

        var year = AnalyticsWindow.Series(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(12, year.Count);
        Assert.Equal("2026-01", year[0].Label);
        Assert.Equal("2026-12", year[^1].Label);
    }

    [Fact]
    public void Seven_day_window_is_inclusive_of_today()
    {
        var now = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);
        var (from, toExclusive, previousFrom) = AnalyticsWindow.Bounds(now, 7);
        Assert.Equal(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), toExclusive);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), previousFrom);
        Assert.Equal(7, AnalyticsWindow.Days(from, toExclusive).Count);
    }

    [Fact]
    public void Delta_percent_uses_previous_period()
    {
        Assert.Equal(100, AnalyticsWindow.DeltaPct(4, 2));
        Assert.Equal(-50, AnalyticsWindow.DeltaPct(1, 2));
        Assert.Equal(100, AnalyticsWindow.DeltaPct(3, 0));
        Assert.Equal(0, AnalyticsWindow.DeltaPct(0, 0));
        Assert.Equal(-3, AnalyticsWindow.Delta(1, 4));
    }
}
