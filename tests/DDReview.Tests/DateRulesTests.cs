using DDReview.Core;
using Xunit;

namespace DDReview.Tests;

public class DateRulesTests
{
    [Theory]
    [InlineData("2026-11-01", "2026-11-30")]   // first of a month: that month's last day
    [InlineData("2026-02-01", "2026-02-28")]
    [InlineData("2028-02-01", "2028-02-29")]   // leap year
    [InlineData("2026-12-01", "2026-12-31")]
    [InlineData("2026-11-02", "2026-11-08")]   // otherwise a week: six days later
    [InlineData("2026-11-28", "2026-12-04")]   // across a month end
    [InlineData("2026-12-30", "2027-01-05")]   // across a year end
    public void End_follows_start(string from, string to) =>
        Assert.Equal(DateTime.Parse(to), DateRules.EndFor(DateTime.Parse(from)));

    [Fact] public void A_cleared_date_leaves_the_end_alone() =>
        Assert.Equal(default, DateRules.EndFor(default(DateTime)));

    [Fact] public void Compare_date_keeps_its_distance()
    {
        Assert.Equal(new DateTime(2025, 11, 2), DateRules.CompareFor(new DateTime(2026, 11, 1), DateRules.DefaultCompareOffsetDays));   // 364 days: the same weekday a year before
        Assert.Equal(new DateTime(2026, 10, 25), DateRules.CompareFor(new DateTime(2026, 11, 1), 7));
    }

    [Fact] public void DateOnly_overload_matches() =>
        Assert.Equal(new DateOnly(2026, 11, 30), DateRules.EndFor(new DateOnly(2026, 11, 1)));
}
