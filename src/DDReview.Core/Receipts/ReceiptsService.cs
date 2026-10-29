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
public sealed record ReceiptsDbFacts(bool ProfitCenterColumnExists, int CashTipsEarnedId);

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
        bool filteredDetail = false;
        if (req.Filters != null)
            foreach (var (dim, list) in req.Filters)
            {
                if (!SafeList.IsMatch(list)) throw new ArgumentException("Invalid filter list.");
                var f = typeof(DataAccessReceiptsReview).GetField(dim + "ListCSV") ?? throw new ArgumentException("Unknown filter " + dim);
                f.SetValue(r, list);
                if (list.Length > 0 && DetailDims.HasFlag(dim)) filteredDetail = true;
            }
        bool showDeposits = (d & DetailDims) != 0 || filteredDetail;
        return r.BuildDataScript(facts.ProfitCenterColumnExists, showDeposits, facts.CashTipsEarnedId);
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
        return new ReceiptsDbFacts(pc, tips);
    }

    public async Task<DataTable> RunAsync(string cs, ReceiptsRequest req, CancellationToken ct = default)
    {
        if (req.End < req.Start) throw new ArgumentException("The end date is before the start date.");
        if (req.StoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        var facts = await ProbeAsync(cs, ct);
        var (sql, pars) = SalesService.Parameterize(BuildScript(req, facts));
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        foreach (var p in pars) cmd.Parameters.Add(p);
        var table = new DataTable("Receipts");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        table.Load(rd);
        return table;
    }
}
