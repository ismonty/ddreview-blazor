using DDReview.Core.Reports;
using Xunit;

namespace DDReview.Tests;

public class CustomReportTests
{
    [Theory]
    [InlineData("dbo.fn_CustomReport_TimeAndAttendance", "fn_CustomReport_TimeAndAttendance")]
    [InlineData("[dbo].[fn_X]", "fn_X")]
    [InlineData("fn_X", "fn_X")]
    [InlineData("other.fn_X", null)]
    [InlineData("fn_X(1)", null)]
    [InlineData("fn_X; drop table t", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Function_names_must_be_plain(string? raw, string? expected) =>
        Assert.Equal(expected, CustomReportService.PlainFunctionName(raw));

    [Fact]
    public void Download_name_uses_the_index_name_with_the_new_extension()
    {
        Assert.Equal("TipsExport.xlsx", CustomReportService.DownloadName("TipsExport.xls", false));
        Assert.Equal("TipsExport.csv", CustomReportService.DownloadName("TipsExport.xls", true));
        Assert.Equal("Export.xlsx", CustomReportService.DownloadName("", false));
        Assert.DoesNotContain("/", CustomReportService.DownloadName("a/b.xls", false));
    }

    [Fact]
    public void Row_counter_column_is_dropped_only_when_first()
    {
        var withId = new List<(int, string)> { (0, "ItemID"), (1, "Employee Number") };
        Assert.Equal([(1, "Employee Number")], CustomReportService.DropRowCounter(withId));
        var without = new List<(int, string)> { (0, "Employee Number"), (1, "ItemID") };
        Assert.Equal(2, CustomReportService.DropRowCounter(without).Count);
    }

    [Fact]
    public void Csv_quotes_only_when_needed_and_keeps_empty_cells()
    {
        var r = new CustomReportResult(["A", "B,C"], [[1.50m, null], ["x\"y", new DateTime(2026, 1, 2)]]);
        Assert.Equal("A,\"B,C\"\r\n1.5,\r\n\"x\"\"y\",01/02/2026\r\n", CustomReportService.ToCsv(r));
    }

    [Fact]
    public void Xlsx_is_a_zip()
    {
        var bytes = CustomReportService.ToXlsx("Tips", new CustomReportResult(["Employee Number"], [[12345]]));
        Assert.Equal((byte)'P', bytes[0]); Assert.Equal((byte)'K', bytes[1]);
    }
}
