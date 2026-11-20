using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

/// <summary>
/// One menu item sold with a POS tax rate that differs from the rate mapped for its sales account
/// (dbo.fn_CashAndSalesGLandARTaxReview, the data behind DD Admin CS's tax inequities window).
/// </summary>
public sealed record TaxVarianceLine(
    DateTime PostingDate, string? StoreName, string? ProfitCenter, string? MainType, string? MainGroup, string? SaleType,
    string? MenuItem, int? Plu, bool TaxExempt, decimal PosRate, decimal MappedRate, string? CreditAccount, string? DistReference,
    decimal Quantity, decimal Amount, decimal TaxVariance);

public sealed record TaxVarianceSummary(int Lines, decimal Sales, decimal Variance, int Stores);

public static class TaxVariance
{
    public static TaxVarianceSummary Summarize(IReadOnlyList<TaxVarianceLine> lines) =>
        new(lines.Count, lines.Sum(l => l.Amount), lines.Sum(l => l.TaxVariance), lines.Select(l => l.StoreName).Distinct().Count());
}

/// <summary>Read-only. A zero-row answer is the normal, good case.</summary>
public sealed class GlTaxVarianceService
{
    /// <summary>The access layer allowed this query 150 seconds by default; the function walks each store in turn.</summary>
    public const int TimeoutSeconds = 150;

    public async Task<IReadOnlyList<TaxVarianceLine>> GetAsync(string connectionString, GlRequest req, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
select tr.PostingDate, isnull(ts.StoreReportName, ts.StoreName) as StoreName, tr.ProfitCenter, tr.MainType, tr.MainGroup, tr.SaleType, tr.MenuItem, tr.PLU,
       tr.TaxExempt, tr.POSAssignedTaxRate, tr.MappedTaxRate, tr.CreditAccount, tr.DistReference, tr.Quantity, tr.Amount,
       tr.Amount * (tr.POSAssignedTaxRate - tr.MappedTaxRate) / 100 as TaxVariance
from dbo.fn_CashAndSalesGLandARTaxReview(@stores, @start, @end, 0) tr
inner join dbo.tblStores ts on ts.StoreID = tr.StoreID
order by tr.PostingDate, ts.StoreNumber, tr.MainType, tr.MenuItem", cn) { CommandTimeout = TimeoutSeconds };
        cmd.Parameters.Add("@stores", System.Data.SqlDbType.VarChar, 8000).Value = req.StoreListCsv ?? "";
        cmd.Parameters.Add("@start", System.Data.SqlDbType.DateTime).Value = req.Start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@end", System.Data.SqlDbType.DateTime).Value = req.End.ToDateTime(TimeOnly.MinValue);

        var list = new List<TaxVarianceLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        string? S(string c) => r[c] is DBNull ? null : r[c].ToString();
        decimal D(string c) => r[c] is DBNull ? 0m : Convert.ToDecimal(r[c]);
        while (await r.ReadAsync(ct))
            list.Add(new TaxVarianceLine(
                r["PostingDate"] is DBNull ? default : (DateTime)r["PostingDate"], S("StoreName"), S("ProfitCenter"), S("MainType"), S("MainGroup"), S("SaleType"),
                S("MenuItem"), r["PLU"] is DBNull ? null : Convert.ToInt32(r["PLU"]), r["TaxExempt"] is not DBNull && Convert.ToBoolean(r["TaxExempt"]),
                D("POSAssignedTaxRate"), D("MappedTaxRate"), S("CreditAccount"), S("DistReference"), D("Quantity"), D("Amount"), D("TaxVariance")));
        return list;
    }
}
