using System.Data;
using System.Text.RegularExpressions;
using DDReview.Core.Receipts.Legacy;
using DDReview.Core.Sales;
using DDReview.Core.Support;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Receipts;

[Flags]
public enum ReceiptsDimensions
{
    None = 0, StoreGroup = 1, Store = 2, ProfitCenter = 4, ReceiptClass = 8, ReceiptType = 16, CreditCardBatchNumber = 32,
    CashierStaffDepartment = 64, Cashier = 128, ServerStaffDepartment = 256, Server = 512, CheckNumber = 1024,
    DayName = 2048, WeekName = 4096, CashierSessionNumber = 8192, DayHourNumber = 16384,
}

public sealed record ReceiptsRequest(
    DateOnly Start, DateOnly End, ReceiptsDimensions Dimensions, IReadOnlyCollection<int> StoreIds,
    IReadOnlyDictionary<ReceiptsDimensions, string>? Filters = null);

/// <summary>Facts about the client database the generator needs (probed once per run).</summary>
public sealed record ReceiptsDbFacts(bool ProfitCenterColumnExists, int CashTipsEarnedId, bool ReceiptTypeOtherExists = true, bool DayHourNameFunctionExists = true);

public sealed class ReceiptsService
{
    static readonly Regex SafeList = new(@"^[0-9,:\- ]*$", RegexOptions.Compiled);
    static readonly (string, string)[] Calculated =
    [
        ("TotalTipsVsNetPercentageAmount", "case when isnull(TotalPayrollSalesAmount,0)<>0 then TotalTipsAmount/TotalPayrollSalesAmount else null end"),
        ("TotalDiscountPercentage", "case when isnull(TotalSalesAmount,0)<>0 then TotalDiscountAmount/TotalSalesAmount else null end"),
        ("AverageNetSalesPerTable", "case when isnull(TotalCheckCount,0)<>0 then TotalNetReceiptAmount/TotalCheckCount else null end"),
        ("AverageNetSalesPerGuest", "case when isnull(TotalDeclaredCoverCount,0)<>0 then TotalNetReceiptAmount/TotalDeclaredCoverCount else null end"),
        ("AverageTipPerTable", "case when isnull(TotalCheckCount,0)<>0 then TotalTipsAmount/TotalCheckCount else null end"),
        ("AverageTipGuest", "case when isnull(TotalDeclaredCoverCount,0)<>0 then TotalTipsAmount/TotalDeclaredCoverCount else null end"),
    ];

    // The old page asked for the (slower) deposit-detail query only when one of these was grouped by or filtered.
    const ReceiptsDimensions DetailDims = ReceiptsDimensions.CashierStaffDepartment | ReceiptsDimensions.Cashier
        | ReceiptsDimensions.ServerStaffDepartment | ReceiptsDimensions.Server | ReceiptsDimensions.CheckNumber
        | ReceiptsDimensions.CreditCardBatchNumber | ReceiptsDimensions.CashierSessionNumber | ReceiptsDimensions.DayHourNumber
        | ReceiptsDimensions.ProfitCenter;

    public string BuildScript(ReceiptsRequest req, ReceiptsDbFacts facts)
    {
        var r = Configure(req, out var filteredDetail);
        var d = req.Dimensions;
        bool showDeposits = (d & DetailDims) != 0 || filteredDetail;
        var script = r.BuildDataScript(facts.ProfitCenterColumnExists, showDeposits, facts.CashTipsEarnedId);
        // Older databases have no tblReceiptTypes.ReceiptTypeOther ("Other card"): treat every receipt type as not flagged, as before the column existed.
        if (!facts.ReceiptTypeOtherExists)
            script = System.Text.RegularExpressions.Regex.Replace(script, @"\brt\.ReceiptTypeOther\b", "0", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Older databases have no dbo.fn_DayHourName: the same names are worked out in line (Midnight, Noon, then 1:00 AM style, as the Sales review builds them).
        if (!facts.DayHourNameFunctionExists)
            script = script.Replace("dbo.fn_DayHourName(DayHourNumber)", DayHourNameInline);
        return script;
    }

    /// <summary>The names behind every active filter (one row), from the old page's own header query; null if it cannot be had.</summary>
    public Task<DataRow?> HeaderAsync(string cs, ReceiptsRequest req, CancellationToken ct = default) =>
        DDReview.Core.Drill.FilterHeader.RunAsync(cs, () => BuildHeaderScript(req), ct);

    public string BuildHeaderScript(ReceiptsRequest req) => Configure(req, out _).BuildHeaderScript();

    static DataAccessReceiptsReview Configure(ReceiptsRequest req, out bool filteredDetail)
    {
        var r = new DataAccessReceiptsReview("")
        {
            StartDayNumber = DayNumber.FromDate(req.Start),
            EndDayNumber = DayNumber.FromDate(req.End),
            StoreListCSV = string.Join(",", req.StoreIds.Distinct().OrderBy(x => x)),
        };
        foreach (var (n, e) in Calculated) r.AddCalculatedField(n, e);
        var d = req.Dimensions;
        int F(ReceiptsDimensions x) => d.HasFlag(x) ? 1 : 0;
        r.StoreGroupFlag = F(ReceiptsDimensions.StoreGroup); r.StoreFlag = F(ReceiptsDimensions.Store);
        r.ProfitCenterFlag = F(ReceiptsDimensions.ProfitCenter); r.ReceiptClassFlag = F(ReceiptsDimensions.ReceiptClass);
        r.ReceiptTypeFlag = F(ReceiptsDimensions.ReceiptType); r.CreditCardBatchNumberFlag = F(ReceiptsDimensions.CreditCardBatchNumber);
        r.CashierStaffDepartmentFlag = F(ReceiptsDimensions.CashierStaffDepartment); r.CashierFlag = F(ReceiptsDimensions.Cashier);
        r.ServerStaffDepartmentFlag = F(ReceiptsDimensions.ServerStaffDepartment); r.ServerFlag = F(ReceiptsDimensions.Server);
        r.CheckNumberFlag = F(ReceiptsDimensions.CheckNumber); r.DayNameFlag = F(ReceiptsDimensions.DayName);
        r.WeekNameFlag = F(ReceiptsDimensions.WeekName); r.CashierSessionNumberFlag = F(ReceiptsDimensions.CashierSessionNumber);
        r.DayHourNumberFlag = F(ReceiptsDimensions.DayHourNumber);
        filteredDetail = false;
        if (req.Filters != null)
            foreach (var (dim, list) in req.Filters)
            {
                if (!SafeList.IsMatch(list)) throw new ArgumentException("Invalid filter list.");
                var f = typeof(DataAccessReceiptsReview).GetField(dim + "ListCSV") ?? throw new ArgumentException("Unknown filter " + dim);
                f.SetValue(r, list);
                if (list.Length > 0 && DetailDims.HasFlag(dim)) filteredDetail = true;
            }
        return r;
    }

    public async Task<ReceiptsDbFacts> ProbeAsync(string cs, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        bool pc;
        await using (var c = new SqlCommand("select coalesce(col_length('dbo.tblCashAndSalesReceiptsDetail','ProfitCenterID'),0)", cn))
            pc = Convert.ToInt32(await c.ExecuteScalarAsync(ct)) != 0;
        int tips = 0;
        await using (var c = new SqlCommand("select isnull((select 1 from INFORMATION_SCHEMA.TABLES where TABLE_TYPE='BASE TABLE' and TABLE_NAME='tblCashAndSalesReceiptsDetail_Text'),0)", cn))
        {
            if (Convert.ToInt32(await c.ExecuteScalarAsync(ct)) != 0)
            {
                await using var c2 = new SqlCommand("select top 1 ReceiptDetailsID from tblCashAndSalesReceiptsDetail_Text where ReceiptDetailsText='Cash Tips Earned'", cn);
                var o = await c2.ExecuteScalarAsync(ct);
                if (o != null && o != DBNull.Value) tips = Convert.ToInt32(o);
            }
        }
        bool other;
        await using (var c = new SqlCommand("select coalesce(col_length('dbo.tblReceiptTypes','ReceiptTypeOther'),0)", cn))
            other = Convert.ToInt32(await c.ExecuteScalarAsync(ct)) != 0;
        bool hourFn;
        await using (var c = new SqlCommand("select case when object_id('dbo.fn_DayHourName') is null then 0 else 1 end", cn))
            hourFn = Convert.ToInt32(await c.ExecuteScalarAsync(ct)) != 0;
        return new ReceiptsDbFacts(pc, tips, other, hourFn);
    }

    public const string DayHourNameInline =
        "case when DayHourNumber in (0,24) then 'Midnight' when DayHourNumber in (12,36) then 'Noon' " +
        "else right('  ' + cast(DayHourNumber as varchar(3)),2) + case when floor(DayHourNumber/12)%2=1 then ':00 PM' else ':00 AM' end end";

    static readonly Regex DbObjects = new(@"\b(?:dbo\.)?(?<n>(?:fn|vw|cs_sp)_\w+|tbl[A-Za-z]\w*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The tables, views and functions a script names that this database does not have (to say so when a run fails on one).</summary>
    static async Task<string> MissingObjectsAsync(string cs, string sql, CancellationToken ct)
    {
        try
        {
            var names = DbObjects.Matches(sql).Select(m => m.Groups["n"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) return "";
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("select name from sys.objects where name in (" + string.Join(",", names.Select((_, i) => "@n" + i)) + ")", cn);
            for (int i = 0; i < names.Count; i++) cmd.Parameters.AddWithValue("@n" + i, names[i]);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) found.Add(r.GetString(0));
            var missing = names.Where(n => !found.Contains(n)).ToList();
            return missing.Count == 0 ? "" : " Not found in this database: " + string.Join(", ", missing) + ".";
        }
        catch (Exception) { return ""; }
    }

    public async Task<DataTable> RunAsync(string cs, ReceiptsRequest req, CancellationToken ct = default)
    {
        if (req.End < req.Start) throw new ArgumentException("The end date is before the start date.");
        if (req.StoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        var facts = await ProbeAsync(cs, ct);
        var (script, pairs) = await DDReview.Core.Drill.PairSupport.BuildAsync(cs, () => BuildScript(req, facts), ct);
        var (sql, pars) = SalesService.Parameterize(script);
        sql = DDReview.Core.Sales.ReportSqlTuning.Apply(sql);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        foreach (var p in pars) cmd.Parameters.Add(p);
        pairs?.AttachTo(cmd);
        var table = new DataTable("Receipts");
        try
        {
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            table.Load(rd);
        }
        catch (SqlException ex) when (ex.Number is 207 or 208 or 4121 or 195)
        {
            throw new InvalidOperationException(ex.Message + await MissingObjectsAsync(cs, sql, ct), ex);
        }
        return table;
    }
}
