using System.Data;
using DDReview.Core.Drill;
using DDReview.Core.Labor;
using DDReview.Core.Receipts;
using DDReview.Core.Sales;
using Xunit;

namespace DDReview.Tests;

public class DrillTests
{
    static readonly IReadOnlyList<DrillColumn> Cat = DrillCatalogs.Sales;

    [Fact]
    public void RowClick_filters_to_the_row_and_moves_on_to_the_next_dimension()
    {
        var s = new DrillState(); s.Flags.Add("Store");
        s.RowClick(Cat, "Store", "12");
        Assert.Equal("12", s.Filters["Store"]);
        Assert.DoesNotContain("Store", s.Flags);
        Assert.Contains("MainType", s.Flags);
    }

    [Fact]
    public void RowClick_on_a_dimension_that_leads_to_itself_keeps_it_on()
    {
        var s = new DrillState(); s.Flags.Add("Day");
        s.RowClick(Cat, "Day", "5000");
        Assert.Contains("Day", s.Flags);
        Assert.Equal("5000", s.Filters["Day"]);
    }

    [Fact]
    public void Header_title_shows_only_that_dimension_and_keeps_filters()
    {
        var s = new DrillState(); s.Flags.Add("Store"); s.Flags.Add("MainType"); s.Filters["Store"] = "3";
        s.ShowOnly("MainType");
        Assert.Equal(["MainType"], s.Flags.ToArray());
        Assert.Equal("3", s.Filters["Store"]);
    }

    [Fact]
    public void Drill_up_right_and_show_all()
    {
        var s = new DrillState(); s.Flags.Add("Store"); s.Filters["Store"] = "3";
        s.DrillUp("MainType");
        s.DrillRight(Cat, "MainType");           // next after MainType is SaleType
        Assert.True(s.Flags.SetEquals(["Store", "MainType", "SaleType"]));
        s.ShowAll("Store");
        Assert.False(s.Filters.ContainsKey("Store"));
    }

    [Fact]
    public void Sort_toggles_and_numbers_start_high_to_low()
    {
        var s = new DrillState();
        s.Sort("Net", numeric: true);
        Assert.True(s.SortDescending);
        s.Sort("Net", numeric: true);
        Assert.False(s.SortDescending);
        s.Sort("Store Name", numeric: false);
        Assert.Equal("Store Name", s.SortHeader);
        Assert.False(s.SortDescending);
    }

    [Fact]
    public void Dimensions_round_trip_through_the_enum_and_ignore_other_keys()
    {
        var s = new DrillState(); s.SetDimensions(SalesDimensions.Store | SalesDimensions.Day);
        Assert.Equal(SalesDimensions.Store | SalesDimensions.Day, s.Dimensions<SalesDimensions>());
        s.Flags.Add("NotADimension");
        Assert.Equal(SalesDimensions.Store | SalesDimensions.Day, s.Dimensions<SalesDimensions>());
        var l = new DrillState(); l.Flags.Add("PunchDetail"); l.Flags.Add("Employee");
        Assert.Equal(LaborDimensions.PunchDetail | LaborDimensions.Employee, l.Dimensions<LaborDimensions>());
    }

    [Fact]
    public void A_store_filter_can_only_narrow_the_stores_the_user_already_has()
    {
        var s = new DrillState(); s.Filters["Store"] = "2,99";            // 99 is not one of the user's stores
        var (ids, lists) = DrillFilters.Split(s, Cat, [1, 2, 3]);
        Assert.Equal([2], ids);
        Assert.Empty(lists);
    }

    [Fact]
    public void Other_filters_pass_through_under_their_request_names()
    {
        var s = new DrillState(); s.Filters["MenuItem"] = "1:5 2:5"; s.Filters["Store"] = "1";
        var (ids, lists) = DrillFilters.Split(s, Cat, [1, 2]);
        Assert.Equal([1], ids);
        Assert.Equal("1:5 2:5", lists["MenuItem"]);
        var l = new DrillState(); l.Filters["OriginalPayrollID"] = "77";
        Assert.Equal("77", DrillFilters.Split(l, DrillCatalogs.Labor, [1]).Lists["PayrollID"]);
    }

    [Fact]
    public void Every_catalog_key_matches_a_dimension_member_or_filter_the_service_accepts()
    {
        foreach (var c in DrillCatalogs.Sales) Assert.True(Enum.TryParse<SalesDimensions>(c.Key, out _), c.Key);
        foreach (var c in DrillCatalogs.Receipts) Assert.True(Enum.TryParse<ReceiptsDimensions>(c.Key, out _), c.Key);
        foreach (var c in DrillCatalogs.Labor) Assert.True(Enum.TryParse<LaborDimensions>(c.Key, out _), c.Key);
        foreach (var cat in new[] { DrillCatalogs.Sales, DrillCatalogs.Receipts, DrillCatalogs.Labor })
            foreach (var c in cat) Assert.NotNull(DrillState.Find(cat, c.NextKey));
    }

    [Fact]
    public void Filter_names_exist_as_generator_fields()
    {
        // The services set <Filter>ListCSV by reflection; a catalog typo would only show up at run time.
        foreach (var c in DrillCatalogs.Sales.Where(c => c.Key != "Store"))
            Assert.NotNull(typeof(DDReview.Core.Sales.Legacy.DataAccessSalesReview).GetField(c.Filter + "ListCSV"));
        foreach (var c in DrillCatalogs.Receipts.Where(c => c.Key != "Store"))
            Assert.NotNull(typeof(DDReview.Core.Receipts.Legacy.DataAccessReceiptsReview).GetField(c.Filter + "ListCSV"));
        foreach (var c in DrillCatalogs.Labor.Where(c => c.Key != "Store"))
            Assert.NotNull(typeof(DDReview.Core.Labor.Legacy.DataAccessLaborReview).GetField(c.Filter + "ListCSV"));
    }

    [Fact]
    public void GridSort_orders_numbers_text_nulls_and_ratios()
    {
        var t = new DataTable();
        t.Columns.Add("Name", typeof(string)); t.Columns.Add("N", typeof(decimal)); t.Columns.Add("D", typeof(decimal));
        t.Rows.Add("b", 10m, 100m); t.Rows.Add("A", 5m, 10m); t.Rows.Add("c", DBNull.Value, 0m); t.Rows.Add("d", 2m, 0m);
        Assert.Equal(["A", "b", "c", "d"], GridSort.Rows(t, GridSort.Column(t, "Name"), false).Select(r => (string)r["Name"]).ToArray());
        Assert.Equal(["b", "A", "d", "c"], GridSort.Rows(t, GridSort.Column(t, "N"), true).Select(r => (string)r["Name"]).ToArray());
        // N/D: A = 0.5, b = 0.1, zero denominators sort as empty (low)
        Assert.Equal(["A", "b"], GridSort.Rows(t, GridSort.Column(t, "N", "D"), true).Take(2).Select(r => (string)r["Name"]).ToArray());
    }
}
