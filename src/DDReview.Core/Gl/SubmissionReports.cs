using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

/// <summary>One posted batch that has a stored submission report (one per store per day).</summary>
public sealed record SubmissionBatch(int BatchId, int StoreId, int? StoreNumber, string StoreName, DateTime PostingDate);

/// <summary>One drawing command of the stored report (tblCashAndSalesReport): 1 text, 2 bold on/off, 3 line, 4 right-aligned text, 5 centered text.
/// Coordinates are twips (1/1440 inch), as the old report page drew them.</summary>
public sealed record ReportCommand(int Sequence, int Code, bool Bold, int X, int Y, int X2, int Y2, string Text);

public sealed record SubmissionReport(SubmissionBatch Batch, IReadOnlyList<ReportCommand> Commands);

/// <summary>
/// The submission report of the old site (DDAdminGLARSubmissionReportGraphic): a page the register software stored as drawing
/// commands for each posted batch. Listing and reading stay in SQL; the page is drawn as SVG by <see cref="SubmissionSvg"/>.
/// </summary>
public sealed class SubmissionReportService
{
    public const int MaxListed = 1000;

    /// <summary>Stores the user may see for this report, or null when unrestricted.</summary>
    public static string? AllowedStores(Tenancy.UserAccess access, Tenancy.ReportKind kind) =>
        access.Administrator || access.AllStores.Contains(kind) ? null : access.StoreListCsv(kind);

    /// <summary>
    /// A batch counts as a day with business when it has a sales line with an amount or quantity, or a receipt line with any amount
    /// (the same tables the Sales and Receipts reviews read). Days with nothing in either are quiet days and are left out of the list.
    /// </summary>
    const string HasActivity = @"
  and (exists (select 1 from dbo.tblCashAndSalesSales css with (nolock) where css.BatchID = csb.BatchID and (css.Amount <> 0 or css.Quantity <> 0))
    or exists (select 1 from dbo.tblCashAndSalesReceipts csr with (nolock) where csr.BatchID = csb.BatchID
               and (csr.AmountTotal <> 0 or csr.AmountFood <> 0 or csr.AmountBeverage <> 0 or csr.AmountOther <> 0 or csr.AmountCharge <> 0
                    or csr.AmountDiscount <> 0 or csr.AmountTax <> 0 or csr.AmountTip <> 0)))";

    public static string ListSql(bool skipQuietDays) => $@"
select top ({MaxListed}) csb.BatchID, csb.StoreID, sto.StoreNumber, isnull(sto.StoreName, sto.StoreGCode) as StoreName, csb.PostingDate
from dbo.tblCashAndSalesBatches csb
inner join dbo.tblStores sto on sto.StoreID = csb.StoreID
where csb.StoreID in (select KeyValue from dbo.fn_CreateKeyTableValueFromCSVString(@stores))
  and (@allowed is null or csb.StoreID in (select KeyValue from dbo.fn_CreateKeyTableValueFromCSVString(@allowed)))
  and csb.PostingDate >= @start and csb.PostingDate < dateadd(day, 1, @end)
  and exists (select 1 from dbo.tblCashAndSalesReport r where r.BatchID = csb.BatchID){(skipQuietDays ? HasActivity : "")}
order by csb.PostingDate, sto.StoreNumber, csb.BatchID";

    /// <summary>
    /// Batches with a stored report for the stores and dates (inclusive), oldest first, leaving out days with no sales and no receipts.
    /// The list is cut at <see cref="MaxListed"/>. If a database lacks one of the columns the quiet-day test reads, the list is
    /// returned without that test rather than failing.
    /// </summary>
    public async Task<IReadOnlyList<SubmissionBatch>> ListAsync(string connectionString, string storeListCsv, string? allowedStoresCsv,
        DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        try { return await ListCoreAsync(cn, true, storeListCsv, allowedStoresCsv, start, end, ct); }
        catch (SqlException ex) when (ex.Number is 207 or 208)   // invalid column / object name: an older schema
        {
            return await ListCoreAsync(cn, false, storeListCsv, allowedStoresCsv, start, end, ct);
        }
    }

    static async Task<IReadOnlyList<SubmissionBatch>> ListCoreAsync(SqlConnection cn, bool skipQuietDays, string storeListCsv, string? allowedStoresCsv,
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(ListSql(skipQuietDays), cn) { CommandTimeout = 120 };
        cmd.Parameters.Add("@stores", SqlDbType.VarChar, 8000).Value = storeListCsv ?? "";
        cmd.Parameters.Add("@allowed", SqlDbType.VarChar, 8000).Value = (object?)allowedStoresCsv ?? DBNull.Value;
        cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
        var list = new List<SubmissionBatch>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(ReadBatch(r));
        return list;
    }

    /// <summary>The stored report of one batch, or null when there is none or the user may not see that store.</summary>
    public async Task<SubmissionReport?> GetAsync(string connectionString, int batchId, string? allowedStoresCsv, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        SubmissionBatch? batch;
        await using (var cmd = new SqlCommand(@"
select csb.BatchID, csb.StoreID, sto.StoreNumber, isnull(sto.StoreName, sto.StoreGCode) as StoreName, csb.PostingDate
from dbo.tblCashAndSalesBatches csb
inner join dbo.tblStores sto on sto.StoreID = csb.StoreID
where csb.BatchID = @id
  and (@allowed is null or csb.StoreID in (select KeyValue from dbo.fn_CreateKeyTableValueFromCSVString(@allowed)))", cn))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = batchId;
            cmd.Parameters.Add("@allowed", SqlDbType.VarChar, 8000).Value = (object?)allowedStoresCsv ?? DBNull.Value;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            batch = await r.ReadAsync(ct) ? ReadBatch(r) : null;
        }
        if (batch is null) return null;

        var commands = new List<ReportCommand>();
        await using (var cmd = new SqlCommand(@"
select SequenceNumber, CommandCode, SetBold, XCoordinate, YCoordinate, XCoordinate2, YCoordinate2, OutputString
from dbo.tblCashAndSalesReport where BatchID = @id order by SequenceNumber", cn))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = batchId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                commands.Add(new ReportCommand(Int(r, 0), Int(r, 1), !r.IsDBNull(2) && Convert.ToBoolean(r.GetValue(2)),
                    Int(r, 3), Int(r, 4), Int(r, 5), Int(r, 6), r.IsDBNull(7) ? "" : Convert.ToString(r.GetValue(7)) ?? ""));
        }
        return new SubmissionReport(batch, commands);
    }

    static int Int(IDataRecord r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

    static SubmissionBatch ReadBatch(IDataRecord r) =>
        new(Convert.ToInt32(r["BatchID"]), Convert.ToInt32(r["StoreID"]), r["StoreNumber"] is DBNull ? null : Convert.ToInt32(r["StoreNumber"]),
            r["StoreName"] is DBNull ? "" : Convert.ToString(r["StoreName"])!.Trim(), Convert.ToDateTime(r["PostingDate"]));
}

/// <summary>Draws a stored report as SVG, the way the old page drew it into a 8.5 x 11 inch image (twips / 15 = pixels at 96 dpi, Verdana 7 pt).</summary>
public static class SubmissionSvg
{
    public const int Width = 816;   // 8.5 in at 96 dpi
    public const int Height = 1056; // 11 in
    const double FontPx = 7 * 96.0 / 72.0;

    public static string Render(IEnumerable<ReportCommand> commands)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" width=\"{Width}\" height=\"{Height}\" ");
        sb.Append("font-family=\"Verdana, 'DejaVu Sans', sans-serif\" ");
        sb.Append(CultureInfo.InvariantCulture, $"font-size=\"{FontPx:0.##}\" xml:space=\"preserve\" style=\"white-space:pre\">");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{Width}\" height=\"{Height}\" fill=\"#fff\"/>");
        bool bold = false;
        double x = 0, y = 0;
        foreach (var c in commands)
        {
            if (c.Code == 2) { bold = c.Bold; continue; }
            x = c.X / 15.0; y = c.Y / 15.0;
            switch (c.Code)
            {
                case 3:
                    sb.Append(CultureInfo.InvariantCulture, $"<line x1=\"{x:0.##}\" y1=\"{y:0.##}\" x2=\"{c.X2 / 15.0:0.##}\" y2=\"{c.Y2 / 15.0:0.##}\" stroke=\"#000\" stroke-width=\"1\" shape-rendering=\"crispEdges\"/>");
                    break;
                case 1 or 4 or 5:
                    var text = Clean(c.Text);
                    if (text.Length == 0) break;
                    var anchor = c.Code == 4 ? "end" : c.Code == 5 ? "middle" : "start";
                    // the old page drew text from its top edge; SVG draws from the baseline, about one font ascent lower
                    sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{x:0.##}\" y=\"{y + FontPx * 1.0:0.##}\" text-anchor=\"{anchor}\"{(bold ? " font-weight=\"700\"" : "")}>{Escape(text)}</text>");
                    break;
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    static string Clean(string s) => new(s.Where(ch => ch >= ' ' || ch == '\t').ToArray());

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
