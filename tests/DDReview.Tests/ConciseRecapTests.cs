using System.Data;
using DDReview.Core.Home;
using Xunit;

namespace DDReview.Tests;

public class ConciseRecapTests
{
    private static readonly string[] Measures = ["GrossSales", "Discounts", "Sales", "WeekToDate", "Covers", "WeekToDateCovers"];
    private static readonly string[] Suffixes = ["", "LastYear", "TwoYearsAgo"];

    private static DataTable NewTable()
    {
        var t = new DataTable();
        t.Columns.Add("RowSeq", typeof(long));
        t.Columns.Add("IsTotal", typeof(bool));
        t.Columns.Add("DisplayOrder", typeof(int));
        t.Columns.Add("StoreGroupName", typeof(string));
        t.Columns.Add("StoreNumber", typeof(int));
        t.Columns.Add("StoreName", typeof(string));
        foreach (var s in Suffixes) foreach (var m in Measures) t.Columns.Add(m + s, typeof(decimal));
        return t;
    }

    /// <summary>Adds a row; <paramref name="net"/> / <paramref name="lastYear"/> / <paramref name="twoYears"/> fill the Sales columns, null leaves the comparison blank.</summary>
    private static void Add(DataTable t, bool total, int order, string group, int number, string name, decimal net, decimal? lastYear, decimal? twoYears, decimal wtd = 0, decimal covers = 0)
    {
        var r = t.NewRow();
        r["RowSeq"] = (long)t.Rows.Count + 1; r["IsTotal"] = total; r["DisplayOrder"] = order; r["StoreGroupName"] = group; r["StoreNumber"] = number; r["StoreName"] = name;
        r["GrossSales"] = net == 0 ? 0m : net + 10; r["Discounts"] = net == 0 ? 0m : 10m; r["Sales"] = net; r["WeekToDate"] = wtd; r["Covers"] = covers; r["WeekToDateCovers"] = covers;
        if (lastYear is { } l) { r["SalesLastYear"] = l; r["GrossSalesLastYear"] = l; r["DiscountsLastYear"] = 0m; r["WeekToDateLastYear"] = l; r["CoversLastYear"] = l / 100; r["WeekToDateCoversLastYear"] = l / 100; }
        if (twoYears is { } y) { r["SalesTwoYearsAgo"] = y; r["GrossSalesTwoYearsAgo"] = y; r["DiscountsTwoYearsAgo"] = 0m; r["WeekToDateTwoYearsAgo"] = y; r["CoversTwoYearsAgo"] = y / 100; r["WeekToDateCoversTwoYearsAgo"] = y / 100; }
        t.Rows.Add(r);
    }

    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public void Dates_are_the_same_weekday_one_and_two_years_back()
    {
        var r = ConciseRecap.Empty(Day);
        Assert.Equal(new DateOnly(2025, 11, 11), r.LastYear);          // 364 days back
        Assert.Equal(new DateOnly(2024, 11, 12), r.TwoYearsAgo);       // 728 days back
        Assert.Equal(DayOfWeek.Tuesday, r.LastYear.DayOfWeek);
        Assert.Equal(DayOfWeek.Tuesday, r.TwoYearsAgo.DayOfWeek);
    }

    [Fact]
    public void Groups_follow_display_order_and_stores_follow_store_number()
    {
        var t = NewTable();
        Add(t, false, 2, "East", 7, "Seven", 100, 90, 80);
        Add(t, false, 1, "West", 5, "Five", 200, 190, 180);
        Add(t, false, 1, "West", 3, "Three", 300, 290, 280);
        Add(t, true, 99, "All Stores", 9999, "All Stores", 600, 570, 540);
        var r = ConciseRecap.From(t, Day);
        Assert.Equal(["West", "East"], r.Groups.Select(g => g.Name));
        Assert.Equal(["Three", "Five"], r.Groups[0].Stores.Select(s => s.StoreName));
        Assert.Equal(300m, r.Groups[0].Stores[0].Now.Net);
        Assert.Equal(290m, r.Groups[0].Stores[0].LastYear.Net);
        Assert.Equal(280m, r.Groups[0].Stores[0].TwoYearsAgo.Net);
        Assert.True(r.Total!.IsTotal);
        Assert.Equal(600m, r.Total.Now.Net);
    }

    [Fact]
    public void A_store_with_no_figures_at_all_is_left_out_and_so_is_its_empty_group()
    {
        var t = NewTable();
        Add(t, false, 1, "West", 3, "Three", 300, 290, 280);
        Add(t, false, 2, "Closed Group", 8, "Eight", 0, 0, 0);          // everything zero
        Add(t, true, 99, "All Stores", 9999, "All Stores", 300, 290, 280);
        var r = ConciseRecap.From(t, Day);
        Assert.Equal(["West"], r.Groups.Select(g => g.Name));
    }

    [Fact]
    public void A_store_with_only_comparison_figures_stays_in()
    {
        var t = NewTable();
        Add(t, false, 1, "West", 3, "Three", 0, 250, null);
        Add(t, true, 99, "All Stores", 9999, "All Stores", 0, 250, null);
        var r = ConciseRecap.From(t, Day);
        Assert.True(r.HasActivity);
        Assert.Equal(250m, r.Groups[0].Stores[0].LastYear.Net);
    }

    [Fact]
    public void A_missing_comparison_is_null_and_shows_blank()
    {
        var t = NewTable();
        Add(t, false, 1, "West", 3, "Three", 300, null, null);
        Add(t, true, 99, "All Stores", 9999, "All Stores", 300, null, null);
        var r = ConciseRecap.From(t, Day);
        var s = r.Groups[0].Stores[0];
        Assert.Null(s.LastYear.Net);
        Assert.Equal("", ConciseRecap.Money(s.LastYear.Net));
        Assert.Equal("", ConciseRecap.Count(s.TwoYearsAgo.Covers));
        Assert.Equal("300.00", ConciseRecap.Money(s.Now.Net));
    }

    [Fact]
    public void Zero_rows_do_not_crash_and_show_no_activity()
    {
        var r = ConciseRecap.From(NewTable(), Day);
        Assert.False(r.HasActivity);
        Assert.Null(r.Total);
        Assert.Empty(r.Groups);
    }

    [Fact]
    public void A_table_without_the_comparison_columns_still_loads()
    {
        var t = new DataTable();
        foreach (var c in new[] { "IsTotal" }) t.Columns.Add(c, typeof(bool));
        t.Columns.Add("DisplayOrder", typeof(int)); t.Columns.Add("StoreGroupName", typeof(string));
        t.Columns.Add("StoreNumber", typeof(int)); t.Columns.Add("StoreName", typeof(string)); t.Columns.Add("Sales", typeof(decimal));
        t.Rows.Add(false, 1, "West", 3, "Three", 12.5m);
        var r = ConciseRecap.From(t, Day);
        Assert.Equal(12.5m, r.Groups[0].Stores[0].Now.Net);
        Assert.Null(r.Groups[0].Stores[0].LastYear.Net);
    }

    [Fact]
    public void Money_and_counts_use_the_same_format_as_the_other_home_tabs()
    {
        Assert.Equal("1,234.50", ConciseRecap.Money(1234.5m));
        Assert.Equal("1,234", ConciseRecap.Count(1234m));
        Assert.Equal("-592.94", ConciseRecap.Money(-592.94m));
    }

    [Fact]
    public void Recap_script_is_embedded_limits_to_the_listed_stores_and_totals_only_those()
    {
        var s = DDReview.Core.Updater.ClientScripts.All.Single(x => x.Id == "016");
        var sql = s.ReadSql();
        Assert.Contains("dbo.fn_ConciseDailyRecapBlazor", sql);
        Assert.Contains("dbo.fn_ConciseDailyRecapInnerCore", sql);
        Assert.Contains("DATEADD(day, -364, @EndingDate)", sql);
        Assert.Contains("DATEADD(day, -728, @EndingDate)", sql);
        Assert.Contains("c.StoreNumber IN (SELECT StoreNumber FROM Allowed)", sql);       // the store filter is in the SQL, on all three dates
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(sql, @"IN \(SELECT StoreNumber FROM Allowed\)").Count);
        Assert.NotEmpty(DDReview.Core.Updater.SqlBatches.Split(sql));
    }

    [Fact]
    public void Lean_recap_script_reads_sales_once_for_three_days_and_filters_the_stores_inside_the_query()
    {
        var s = DDReview.Core.Updater.ClientScripts.All.Single(x => x.Id == "017");
        var sql = s.ReadSql();
        Assert.Contains("-- LEAN RECAP", sql);                                         // the marker the updater's check looks for
        Assert.Contains("ALTER FUNCTION dbo.fn_ConciseDailyRecapBlazor", sql);
        Assert.DoesNotContain("fn_ConciseDailyRecapInnerCore(", sql);                  // no longer calls the slow function
        Assert.Contains("DATEADD(day, -364, @EndingDate)", sql);
        Assert.Contains("DATEADD(day, -728, @EndingDate)", sql);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, @"FROM\s+Dates\s+dn\s+INNER JOIN dbo\.tblCashAndSalesSales"));   // one pass over the sales for all three days
        Assert.Contains("INNER JOIN StoreList sl ON sl.StoreID = m.DestinationStoreID", sql);                                                 // only the user's stores are read
        Assert.Contains("css.MainTypeID IN (1, 2, 3)", sql);
        Assert.Contains("tss.ExcludeFromSales = 0", sql);
        Assert.Contains("sg.DisplayOrder <> 0", sql);
        Assert.Contains("NOT csb.StoreID IN (1, 4, 11)", sql);
        Assert.NotEmpty(DDReview.Core.Updater.SqlBatches.Split(sql));
    }
}
