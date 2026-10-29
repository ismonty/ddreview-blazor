using System.Data;
using DDReview.Core.Home;
using Xunit;

namespace DDReview.Tests;

public class HomeTests
{
    private static DataTable Summary()
    {
        var t = new DataTable();
        foreach (var c in new[] { "StoreNumber", "TotalSales", "PreviousTotalSales", "TotalSalesBudget", "TotalGrossSales", "TotalDiscounts", "TotalVoids", "TotalLabor", "TotalProductCost", "CheckCount", "PreviousCheckCount", "TotalPurchases", "DayPart1Sales", "PreviousDayPart1Sales" })
            t.Columns.Add(c, typeof(decimal));
        t.Columns.Add("StoreName", typeof(string));
        t.Rows.Add(1m, 7049.10m, 5013.43m, 0m, 7642.04m, -592.94m, 26.02m, 1131.93m, 32m, 77m, 70m, 0m, 100m, 50m, "Valentini's");
        return t;
    }

    [Fact]
    public void DailySummary_MatchesTheOldScreen()
    {
        var l = DailySummaryLine.From(Summary().Rows[0]);
        Assert.Equal("2,035.67", DailySummaryLine.Money(l.VsPrevious));
        Assert.Equal("40.60 %", DailySummaryLine.Pct(l.VsPreviousPct));
        Assert.Equal("0.00 %", DailySummaryLine.Pct(l.VsBudgetPct));           // no budget: 0, not a divide error
        Assert.Equal("-7.76 %", DailySummaryLine.Pct(l.OfGross(l.Discounts))); // percent of gross
        Assert.Equal("0.34 %", DailySummaryLine.Pct(l.OfGross(l.Voids)));
        Assert.Equal("14.81 %", DailySummaryLine.Pct(l.OfGross(l.Labor)));
        Assert.Equal("100.00 %", DailySummaryLine.Pct(l.DayPartPct(0)));
    }

    [Fact]
    public void Total_SumsStores_AndGrossIsSalesMinusNegativeDiscounts()
    {
        var t = Summary();
        t.Rows.Add(2m, 1000m, 900m, 0m, 1100m, -100m, 1m, 200m, 0m, 10m, 10m, 0m, 0m, 0m, "B");
        var tot = DailySummaryLine.Total(t.Rows.Cast<DataRow>().Select(DailySummaryLine.From));
        Assert.Equal(8049.10m, tot.Sales);
        Assert.Equal(8049.10m + 692.94m, tot.Gross);
        Assert.Equal(87m, tot.Checks);
    }

    [Fact]
    public void DashboardTotals_PercentOfGross_AndNiceScale()
    {
        var d = DashboardTotals.From(Summary());
        Assert.Equal(592.94m, d.Discounts);                                    // negated, as the old gauge code did
        Assert.InRange(d.DiscountPct, 7.75, 7.77);
        Assert.Equal(10, DashboardTotals.NiceMax(7.76));
        Assert.Equal(100, DashboardTotals.NiceMax(0));
        Assert.Equal(2.5, DashboardTotals.NiceMax(2.2), 6);
    }

    [Fact]
    public void WeekStart_IsMonday_AndDayHeadersStartOnMonday()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), HomeService.WeekStart(new DateOnly(2026, 10, 29)));
        Assert.Equal(new DateOnly(2026, 10, 26), HomeService.WeekStart(new DateOnly(2026, 11, 1)));
        var h = NumbersSection.DayHeaders(new DateTime(2026, 10, 26));
        Assert.Equal(("10/26", "Mon"), h[0]); Assert.Equal(("11/01", "Sun"), h[6]);
        var mid = NumbersSection.DayHeaders(new DateTime(2026, 10, 28));      // starts on a Wednesday: Mon/Tue columns wrap to next week
        Assert.Equal(("11/02", "Mon"), mid[0]); Assert.Equal(("10/28", "Wed"), mid[2]);
    }
}
