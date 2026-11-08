using System.Data;
using DDReview.Core.Receipts;
using Xunit;

namespace DDReview.Tests;

public class ReceiptsWeekdaysTests
{
    static DataRow Row()
    {
        var t = new DataTable();
        foreach (var c in new[] { "R1", "R2", "NetReceiptAmount1", "DiscountAmount1", "CheckCount1", "TipsReceivedAmount1", "TipsDeclaredAmount1", "PaidOutAmount1N", "TotalReceipts", "TotalNetReceiptAmount", "TotalCheckCount", "TotalSalesAmount", "TotalDiscountAmount", "TotalCount" })
            t.Columns.Add(c, typeof(decimal));
        var r = t.NewRow();
        r["R1"] = 10m; r["R2"] = 20m; r["NetReceiptAmount1"] = 90m; r["DiscountAmount1"] = 10m; r["CheckCount1"] = 4m;
        r["TipsReceivedAmount1"] = 6m; r["TipsDeclaredAmount1"] = 2m; r["PaidOutAmount1N"] = 3m;
        r["TotalReceipts"] = 30m; r["TotalNetReceiptAmount"] = 90m; r["TotalCheckCount"] = 4m; r["TotalSalesAmount"] = 100m; r["TotalDiscountAmount"] = 10m;
        t.Rows.Add(r);
        return r;
    }
    static WeekLine L(string label) => ReceiptsWeekdays.Lines.First(l => l.Label == label);

    [Fact] public void Lines_Are_In_The_Old_Order_With_Fifty_Rows() { Assert.Equal(50, ReceiptsWeekdays.Lines.Count); Assert.Equal("Expected", ReceiptsWeekdays.Lines[0].Label); Assert.Equal("Tips/Payment", ReceiptsWeekdays.Lines[^1].Label); }
    [Fact] public void Day_Cells_Read_The_Numbered_Field() { var r = Row(); Assert.Equal("10.00", ReceiptsWeekdays.Cell(r, L("Expected"), 0)); Assert.Equal("20.00", ReceiptsWeekdays.Cell(r, L("Expected"), 1)); Assert.Equal("30.00", ReceiptsWeekdays.Cell(r, L("Expected"), 7)); }
    [Fact] public void Paid_Out_N_Field_Name_Puts_The_Digit_Before_The_N() { Assert.Equal("PaidOutAmount3N", ReceiptsWeekdays.DayName("PaidOutAmount#N", 2)); Assert.Equal("3.00", ReceiptsWeekdays.Cell(Row(), ReceiptsWeekdays.Lines.Last(l => l.Label == "Paid Out"), 0)); }
    [Fact] public void Total_Sales_And_Tips_Are_Derived_When_Not_In_The_Result() { var r = Row(); Assert.Equal("100.00", ReceiptsWeekdays.Cell(r, L("Total Sales"), 0)); Assert.Equal("8.00", ReceiptsWeekdays.Cell(r, L("Tips Rec'd"), 0)); }
    [Fact] public void Ratios_Are_Computed_And_Blank_Or_Zero_When_Dividing_By_Nothing()
    {
        var r = Row();
        Assert.Equal("10.00%", ReceiptsWeekdays.Cell(r, L("Discounts %"), 0)); Assert.Equal("", ReceiptsWeekdays.Cell(r, L("Discounts %"), 1));
        Assert.Equal("22.50", ReceiptsWeekdays.Cell(r, L("Net/Table"), 0)); Assert.Equal("0.00", ReceiptsWeekdays.Cell(r, L("Net/Table"), 1)); Assert.Equal("22.50", ReceiptsWeekdays.Cell(r, L("Net/Table"), 7));
    }
    [Fact] public void Total_Row_Sums_Then_Divides() { var t = Row().Table; var r2 = t.NewRow(); r2["CheckCount1"] = 4m; r2["NetReceiptAmount1"] = 30m; t.Rows.Add(r2); var s = ReceiptsWeekdays.Sums(t); Assert.Equal("15.00", ReceiptsWeekdays.Cell(s, L("Net/Table"), 0)); }
}
