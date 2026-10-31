using System.Data;
using DDReview.Core.Receipts;
using Xunit;

namespace DDReview.Tests;

public class ReceiptsTotalsTests
{
    static DataTable Table()
    {
        var t = new DataTable();
        foreach (var c in new[] { "TotalSalesAmount", "TotalDiscountAmount", "TotalNetReceiptAmount", "TotalCheckCount", "AverageNetSalesPerTable" })
            t.Columns.Add(c, typeof(decimal));
        t.Rows.Add(100m, 10m, 90m, 3m, 30m);
        t.Rows.Add(300m, 30m, 270m, 9m, 30m);
        return t;
    }

    [Fact]
    public void Money_and_counts_are_summed_ratios_are_recomputed()
    {
        var sums = ReceiptsLayout.Sums(Table());
        var lines = ReceiptsLayout.Columns.SelectMany(c => c.Lines).GroupBy(l => l.Label).ToDictionary(g => g.Key, g => g.First());
        Assert.Equal("400.00", ReceiptsLayout.FormatTotal(sums, lines["Total"]));
        Assert.Equal("10.00%", ReceiptsLayout.FormatTotal(sums, lines["Disc%"]));
        Assert.Equal("12", ReceiptsLayout.FormatTotal(sums, lines["#Tables"]));
        Assert.Equal("30.00", ReceiptsLayout.FormatTotal(sums, lines["Net/Table"]));   // 360 / 12, not a sum of averages
    }
}
