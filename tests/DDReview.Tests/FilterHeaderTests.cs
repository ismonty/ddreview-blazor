using System.Data;
using DDReview.Core.Drill;
using DDReview.Core.Labor;
using DDReview.Core.Receipts;
using DDReview.Core.Sales;
using DDReview.Core.Sales.Legacy;
using Xunit;

namespace DDReview.Tests;

public class FilterHeaderTests
{
    static DataRow Header(params (string Col, object? Val)[] cells)
    {
        var t = new DataTable();
        foreach (var (c, v) in cells) t.Columns.Add(c, v is bool ? typeof(bool) : typeof(string));
        var r = t.NewRow();
        foreach (var (c, v) in cells) r[c] = v ?? DBNull.Value;
        t.Rows.Add(r);
        return r;
    }

    [Fact]
    public void Names_Split_On_Line_Breaks_And_Drop_Markup_And_All()
    {
        var h = Header(("CashierName", "<B>Smith, Pat</B><br/>Lee, Mo<br/>All"), ("CashierDetailFlag", false));
        var (names, isLong) = FilterHeader.Names(h, DrillCatalogs.Receipts.First(c => c.Key == "Cashier"));
        Assert.Equal(["Smith, Pat", "Lee, Mo"], names);
        Assert.False(isLong);
    }

    [Fact]
    public void Long_Lists_Are_Marked_Long()
    {
        var h = Header(("CashierName", string.Join("<br/>", Enumerable.Range(1, 9).Select(i => "N" + i))));
        var (names, isLong) = FilterHeader.Names(h, DrillCatalogs.Receipts.First(c => c.Key == "Cashier"));
        Assert.Equal(9, names.Count); Assert.True(isLong);
    }

    [Fact]
    public void Boxes_Use_The_Header_Row_Even_When_The_Result_Has_No_Names_And_Pluralize()
    {
        var h = Header(("CashierName", "A<br/>B"));
        var boxes = FilterHeader.Boxes(h, new DataTable(), DrillCatalogs.Receipts, ["Cashier"]);
        var b = Assert.Single(boxes);
        Assert.Equal("Cashiers", b.Title); Assert.Equal(["A", "B"], b.Names);
    }

    [Fact]
    public void Boxes_Fall_Back_To_The_Result_Rows_Without_A_Header()
    {
        var t = new DataTable(); t.Columns.Add("CashierName"); t.Rows.Add("Solo");
        var b = Assert.Single(FilterHeader.Boxes(null, t, DrillCatalogs.Receipts, ["Cashier"]));
        Assert.Equal("Cashier", b.Title); Assert.Equal(["Solo"], b.Names);
    }

    [Fact]
    public void Header_Scripts_Build_And_Parameterize_For_The_Three_Reports()
    {
        var a = DateOnly.Parse("2026-01-01"); var b = DateOnly.Parse("2026-01-07");
        var rec = new ReceiptsService().BuildHeaderScript(new ReceiptsRequest(a, b, ReceiptsDimensions.Store, [1, 2], new Dictionary<ReceiptsDimensions, string> { [ReceiptsDimensions.Cashier] = "5,6" }));
        Assert.Contains("@p_CashierListCSV", SalesService.Parameterize(rec).Sql);
        var lab = new LaborService().BuildHeaderScript(new LaborRequest(a, b, LaborDimensions.Store, [1, 2], new Dictionary<string, string> { ["Employee"] = "5,6" }));
        Assert.Contains("@p_EmployeeListCSV", SalesService.Parameterize(lab).Sql);
        var sal = new SalesService().BuildHeaderScript("", new SalesRequest(a, b, SalesDimensions.Store, [1, 2], null, new Dictionary<SalesDimensions, string> { [SalesDimensions.MainType] = "5,6" }), new NoLookups());
        Assert.Contains("@p_MainTypeListCSV", SalesService.Parameterize(sal).Sql);
    }

    sealed class NoLookups : ISalesLookups
    {
        public DataTable GetTable(string name, string sql) => new DataTable(name);
    }
}

public class ExportFilterLineTests
{
    [Fact]
    public void Describe_Lists_Names_Or_None()
    {
        Assert.Equal("Filters: None", FilterHeader.Describe([]));
        Assert.Equal("Filters: Cashiers: A, B; Store", FilterHeader.Describe([new("Cashier", "Cashiers", ["A", "B"], false), new("Store", "Store", [], false)]));
    }

    [Fact]
    public void Workbook_Puts_The_Filter_Line_Under_The_Title_And_Above_The_Headers()
    {
        var bytes = DDReview.Core.Support.XlsxWriter.Write("S", "Title", ["H1"], [["v"]], null, "Filters: None");
        using var z = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var xml = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.True(xml.IndexOf("Title") < xml.IndexOf("Filters: None") && xml.IndexOf("Filters: None") < xml.IndexOf("H1"));
    }
}

public class CashOutTypeTests
{
    [Theory]
    [InlineData(null, "Bank")]
    [InlineData("", "Bank")]
    [InlineData("0", "Bank")]
    [InlineData(" Other Current Asset ", "Other Current Asset")]
    public void Cash_out_type_defaults_to_bank(string? configured, string expected) => Assert.Equal(expected, DDReview.Core.Gl.GlAdjustmentService.CashOutType(configured));
}

public class ReceiptTypeOtherTests
{
    [Fact]
    public void Script_Without_The_Other_Card_Column_Does_Not_Mention_It()
    {
        var req = new ReceiptsRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), ReceiptsDimensions.Store, [1, 2]);
        var svc = new ReceiptsService();
        Assert.Contains("rt.ReceiptTypeOther", svc.BuildScript(req, new ReceiptsDbFacts(true, 0)));
        var old = svc.BuildScript(req, new ReceiptsDbFacts(true, 0, false));
        Assert.DoesNotContain("ReceiptTypeOther", old);
        Assert.Contains("(0=0 and rt.CreditCardReceipt<>0", old);
    }
}

public class DayHourNameTests
{
    [Fact]
    public void Script_Without_The_Function_Works_The_Names_Out_In_Line()
    {
        var req = new ReceiptsRequest(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), ReceiptsDimensions.Store | ReceiptsDimensions.DayHourNumber, [1, 2]);
        var svc = new ReceiptsService();
        Assert.Contains("dbo.fn_DayHourName(DayHourNumber)", svc.BuildScript(req, new ReceiptsDbFacts(true, 0)));
        var old = svc.BuildScript(req, new ReceiptsDbFacts(true, 0, true, false));
        Assert.DoesNotContain("fn_DayHourName", old);
        Assert.Contains("'Midnight'", old);
    }
}
