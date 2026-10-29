using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

/// <summary>Flags passed to the SQL function. Meaning of each bit is taken from fn_CashAndSalesGLandAR; confirm names with the Access layer owner.</summary>
[Flags]
public enum ArFlag { None = 0, Bit1 = 1, Bit2 = 2, Bit4 = 4, Bit8 = 8, Bit16 = 16 }

public sealed record GlRequest(string StoreListCsv, DateOnly Start, DateOnly End, int ArFlag = 2, bool SalesTaxFlag = false);

public sealed class GlService
{
    public async Task<IReadOnlyList<GlLine>> GetDetailAsync(string connectionString, GlRequest req, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "select * from dbo.fn_CashAndSalesGLandARMultiStoreSelect(@stores, @start, @end, @ar, @tax)", cn) { CommandTimeout = 600 };
        AddCommon(cmd, req);
        cmd.Parameters.Add("@tax", System.Data.SqlDbType.Bit).Value = req.SalesTaxFlag;

        var list = new List<GlLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var ix = Enumerable.Range(0, r.FieldCount).ToDictionary(i => r.GetName(i), i => i, StringComparer.OrdinalIgnoreCase);
        T? V<T>(string c) where T : struct => !ix.TryGetValue(c, out var i) || r.IsDBNull(i) ? null : (T)Convert.ChangeType(r.GetValue(i), typeof(T));
        string? S(string c) => !ix.TryGetValue(c, out var i) || r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        while (await r.ReadAsync(ct))
            list.Add(new GlLine(
                V<int>("BatchID") ?? 0, V<DateTime>("PostingDate") ?? default, S("Account"), S("ARAccount"), V<int>("ARAccountID"), S("ARAccountType"),
                V<decimal>("Debit") ?? 0m, V<decimal>("Credit") ?? 0m, S("DistReference"), V<int>("Sorter"), V<bool>("Suspense") ?? false,
                V<int>("CheckNumber"), V<int>("TransactionNumber"), V<short>("DebitCreditFlag"), V<int>("StoreID"), V<bool>("ExportFlag") ?? false,
                S("AccountClass"), V<bool>("AROffsetLine") ?? false, V<int>("StoreNumber"), S("StoreName"), S("DefaultQuickBooksType"),
                S("AccountNumber"), V<decimal>("TotalDebit"), V<decimal>("TotalCredit")));
        return list;
    }

    public async Task<IReadOnlyList<GlSummaryLine>> GetSummaryAsync(string connectionString, GlRequest req, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
select * from dbo.fn_DDAdminCSWebGLAR(@stores, @start, @end, @ar) gl
order by gl.StoreNumber, (case when gl.ARAccount is null then 0 else 1 end*2+gl.TotalLine), gl.AccountClass, gl.ARAccount,
         gl.CheckNumber, gl.TransactionNumber, gl.Sorter, gl.DebitCreditFlag desc, gl.Account", cn) { CommandTimeout = 600 };
        AddCommon(cmd, req);

        var list = new List<GlSummaryLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        T? V<T>(string c) where T : struct => r[c] is DBNull ? null : (T)Convert.ChangeType(r[c], typeof(T));
        string? S(string c) => r[c] is DBNull ? null : r[c].ToString();
        while (await r.ReadAsync(ct))
            list.Add(new GlSummaryLine(V<int>("StoreNumber"), V<int>("TotalLine") ?? 0, S("StoreName"), V<int>("BatchID"), S("PostingDateRange"),
                S("Account"), S("ARAccount"), V<decimal>("Debit") ?? 0m, V<decimal>("Credit") ?? 0m, S("DistReference"), S("AccountClass"),
                V<int>("StoreID"), V<int>("CheckNumber"), V<int>("TransactionNumber"), V<int>("Sorter"), S("DetailInvoiceMessage")));
        return list;
    }

    private static void AddCommon(SqlCommand cmd, GlRequest req)
    {
        cmd.Parameters.Add("@stores", System.Data.SqlDbType.VarChar, 8000).Value = req.StoreListCsv ?? "";
        cmd.Parameters.Add("@start", System.Data.SqlDbType.DateTime).Value = req.Start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@end", System.Data.SqlDbType.DateTime).Value = req.End.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@ar", System.Data.SqlDbType.Int).Value = req.ArFlag;
    }
}
