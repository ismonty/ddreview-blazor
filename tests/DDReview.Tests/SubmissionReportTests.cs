using DDReview.Core.Gl;
using DDReview.Core.Tenancy;

namespace DDReview.Tests;

public class SubmissionReportTests
{
    static ReportCommand C(int code, int x = 0, int y = 0, int x2 = 0, int y2 = 0, string text = "", bool bold = false) =>
        new(1, code, bold, x, y, x2, y2, text);

    [Fact]
    public void Text_lines_and_alignment_are_drawn_from_twips()
    {
        var svg = SubmissionSvg.Render([
            C(1, 1500, 300, text: "Left & <co>"), C(4, 7500, 300, text: "Right"), C(5, 6000, 600, text: "Mid"),
            C(2, bold: true), C(1, 1500, 900, text: "Bold"), C(2, bold: false), C(3, 1500, 1500, 9000, 1500)]);
        Assert.Contains("viewBox=\"0 0 816 1056\"", svg);
        Assert.Contains("x=\"100\"", svg);                                   // 1500 twips = 100 px
        Assert.Contains(">Left &amp; &lt;co&gt;</text>", svg);                 // text is escaped
        Assert.Contains("text-anchor=\"end\"", svg);
        Assert.Contains("text-anchor=\"middle\"", svg);
        Assert.Contains("font-weight=\"700\">Bold</text>", svg);
        Assert.DoesNotContain("font-weight=\"700\">Left", svg);               // bold applies only after the bold command
        Assert.Contains("<line x1=\"100\" y1=\"100\" x2=\"600\" y2=\"100\"", svg);
    }

    [Fact]
    public void An_empty_report_is_a_blank_white_page_not_an_error()
    {
        var svg = SubmissionSvg.Render([]);
        Assert.StartsWith("<svg", svg);
        Assert.EndsWith("</svg>", svg);
        Assert.DoesNotContain("<text", svg);
        Assert.DoesNotContain("<line", svg);
    }

    [Fact]
    public void Blank_text_and_control_characters_are_dropped()
    {
        var svg = SubmissionSvg.Render([C(1, 15, 15, text: ""), C(1, 15, 30, text: "A\u0001B")]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "<text "));   // the empty text draws nothing
        Assert.Contains(">AB</text>", svg);
    }

    [Fact]
    public void Only_users_with_store_restrictions_get_a_store_list()
    {
        var restricted = new UserAccess();
        restricted.Stores[ReportKind.DebitCredit].UnionWith([5, 2]);
        Assert.Equal("2,5", SubmissionReportService.AllowedStores(restricted, ReportKind.DebitCredit));
        var open = new UserAccess();
        open.AllStores.Add(ReportKind.DebitCredit);
        Assert.Null(SubmissionReportService.AllowedStores(open, ReportKind.DebitCredit));
        Assert.Null(SubmissionReportService.AllowedStores(new UserAccess { Administrator = true }, ReportKind.DebitCredit));
    }

    [Fact]
    public void The_list_query_leaves_out_quiet_days_unless_told_not_to()
    {
        var withTest = SubmissionReportService.ListSql(true);
        Assert.Contains("tblCashAndSalesSales", withTest);
        Assert.Contains("tblCashAndSalesReceipts", withTest);
        Assert.Contains("csr.AmountTotal <> 0", withTest);
        Assert.Contains("css.Amount <> 0", withTest);
        Assert.DoesNotContain("Quantity", withTest);                 // a quantity alone does not make a day
        Assert.DoesNotContain("AmountTax", withTest);                // nor do the receipt parts
        Assert.DoesNotContain("AmountTip", withTest);
        Assert.Contains("tblCashAndSalesReport", withTest);          // still requires a stored report
        var without = SubmissionReportService.ListSql(false);
        Assert.DoesNotContain("tblCashAndSalesSales", without);
        Assert.DoesNotContain("AmountTotal", without);
        Assert.Contains("fn_CreateKeyTableValueFromCSVString(@allowed)", withTest);   // store restriction is in both
        Assert.Contains("fn_CreateKeyTableValueFromCSVString(@allowed)", without);
    }
}
