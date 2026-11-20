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

public class DateRulesStartForTests
{
    [Fact] public void An_end_before_the_start_pulls_the_start_back_to_the_end() =>
        Assert.Equal(new DateTime(2026, 7, 3), DDReview.Core.DateRules.StartFor(new DateTime(2026, 7, 10), new DateTime(2026, 7, 3)));
    [Fact] public void An_end_on_or_after_the_start_leaves_the_start_alone()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DDReview.Core.DateRules.StartFor(new DateTime(2026, 7, 10), new DateTime(2026, 7, 10)));
        Assert.Equal(new DateTime(2026, 7, 10), DDReview.Core.DateRules.StartFor(new DateTime(2026, 7, 10), new DateTime(2026, 7, 31)));
    }
    [Fact] public void A_cleared_date_changes_nothing() =>
        Assert.Equal(new DateTime(2026, 7, 10), DDReview.Core.DateRules.StartFor(new DateTime(2026, 7, 10), default));
    [Fact] public void DateOnly_version_matches() =>
        Assert.Equal(new DateOnly(2026, 6, 30), DDReview.Core.DateRules.StartFor(new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30)));
}

public class DateRulesShortcutsTests
{
    static DateTime D(string s) => DateTime.Parse(s);
    static readonly DateTime Today = new(2026, 11, 8);   // yesterday = Nov 7

    [Fact] public void A_first_of_month_start_stops_at_yesterday_when_the_month_is_running()
    {
        Assert.Equal(D("2026-11-07"), DDReview.Core.DateRules.EndFor(D("2026-11-01"), D("2026-11-07")));
        Assert.Equal(D("2026-10-31"), DDReview.Core.DateRules.EndFor(D("2026-10-01"), D("2026-11-07")));
        Assert.Equal(D("2026-11-01"), DDReview.Core.DateRules.EndFor(D("2026-11-01"), D("2026-10-31")));   // today is the 1st: never before the start
        Assert.Equal(D("2026-11-08"), DDReview.Core.DateRules.EndFor(D("2026-11-02"), D("2026-11-30")));  // not the 1st: a week
    }

    [Fact] public void Previous_and_next_move_a_whole_month_by_calendar_months()
    {
        var y = D("2026-11-07");
        Assert.Equal((D("2026-09-01"), D("2026-09-30")), DDReview.Core.DateRules.Shift(D("2026-10-01"), D("2026-10-31"), -1, y));
        Assert.Equal((D("2026-02-01"), D("2026-02-28")), DDReview.Core.DateRules.Shift(D("2026-01-01"), D("2026-01-31"), 1, y));
        Assert.Equal((D("2026-11-01"), D("2026-11-07")), DDReview.Core.DateRules.Shift(D("2026-10-01"), D("2026-10-31"), 1, y));   // into the running month: stops at yesterday
        Assert.Equal((D("2026-10-01"), D("2026-10-31")), DDReview.Core.DateRules.Shift(D("2026-11-01"), D("2026-11-07"), -1, y));  // 1st..yesterday counts as a whole month
    }

    [Fact] public void Other_ranges_move_by_their_own_length()
    {
        var y = D("2026-11-07");
        Assert.Equal((D("2026-10-25"), D("2026-10-31")), DDReview.Core.DateRules.Shift(D("2026-11-01"), D("2026-11-07").AddDays(0), -1, D("2026-11-20")));
        Assert.Equal((D("2026-11-08"), D("2026-11-14")), DDReview.Core.DateRules.Shift(D("2026-11-01"), D("2026-11-07"), 1, D("2026-11-20")));
    }

    [Fact] public void Start_shortcuts_pull_the_end_to_what_fits_when_the_start_lands_after_it()
    {
        var r = DDReview.Core.DateRules.ApplyStart(DDReview.Core.DateRules.StartPreset.LastMonthStart, D("2026-09-30"), Today);
        Assert.Equal((D("2026-10-01"), D("2026-10-31")), r);
        r = DDReview.Core.DateRules.ApplyStart(DDReview.Core.DateRules.StartPreset.YearStart, D("2025-12-31"), Today);
        Assert.Equal((D("2026-01-01"), D("2026-11-07")), r);
        r = DDReview.Core.DateRules.ApplyStart(DDReview.Core.DateRules.StartPreset.Yesterday, D("2026-11-03"), Today);
        Assert.Equal((D("2026-11-07"), D("2026-11-07")), r);
        r = DDReview.Core.DateRules.ApplyStart(DDReview.Core.DateRules.StartPreset.YearStart, D("2026-11-05"), Today);   // already before the end: end left alone
        Assert.Equal((D("2026-01-01"), D("2026-11-05")), r);
    }

    [Fact] public void End_shortcuts_pull_the_start_to_the_first_of_that_month_when_it_lands_before_the_start()
    {
        Assert.Equal((D("2026-10-01"), D("2026-10-31")), DDReview.Core.DateRules.ApplyEnd(DDReview.Core.DateRules.EndPreset.LastMonthEnd, D("2026-11-03"), Today));
        Assert.Equal((D("2026-10-05"), D("2026-10-31")), DDReview.Core.DateRules.ApplyEnd(DDReview.Core.DateRules.EndPreset.LastMonthEnd, D("2026-10-05"), Today));
        Assert.Equal((D("2026-11-01"), D("2026-11-07")), DDReview.Core.DateRules.ApplyEnd(DDReview.Core.DateRules.EndPreset.Yesterday, D("2026-11-08"), Today));
    }
}
