using System.Data;
using DDReview.Payroll.Adp;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll;

/// <param name="ByStore">Group by store.</param><param name="ByEmployee">Group by employee.</param>
/// <param name="ByWeek">Split by payroll week.</param><param name="ByDepartment">Split by staff department.</param>
public sealed record PayrollReviewRequest(DateOnly Start, DateOnly End, IReadOnlyCollection<int> StoreIds,
    bool ByStore = true, bool ByEmployee = true, bool ByWeek = true, bool ByDepartment = true);

/// <summary>The Totals row of Payroll Review (the old RedrawTotals).</summary>
public sealed record PayrollTotals(decimal RegularHours, decimal OvertimeHours, decimal DoubletimeHours, decimal TotalHours, decimal TotalWages,
    decimal TotalSales, decimal PayrollSales, decimal CustomerTipsReceived, decimal PayrollTips, decimal NetChargedTips, decimal ExtraPay)
{
    /// <summary>
    /// Sums the rows given (pass the filtered view to total only what is shown). A missing value counts as zero.
    /// Same arithmetic as the old form: DoubletimeHours here is the DoubletimeHours column only, total hours = regular + overtime + doubletime,
    /// payroll sales = sales + transferred sales, payroll tips = net cash + net credit card POS tips, extra pay = additional pay + additional overtime pay.
    /// </summary>
    public static PayrollTotals Compute(IEnumerable<DataRow> rows)
    {
        decimal reg = 0, ot = 0, dt = 0, wages = 0, sales = 0, psales = 0, tips = 0, ptips = 0, cc = 0, extra = 0;
        foreach (var r in rows)
        {
            reg += D(r, "RegularHours"); ot += D(r, "OvertimeHours"); dt += D(r, "DoubletimeHours");
            wages += D(r, "TotalHourlyPay"); sales += D(r, "TotalSales"); psales += D(r, "TotalSales") + D(r, "TransferredSales");
            tips += D(r, "TotalTips"); ptips += D(r, "POSTipsNetCash") + D(r, "POSTipsNetCreditCard"); cc += D(r, "POSTipsNetCreditCard");
            extra += D(r, "AdditionalPayAmount") + D(r, "AdditionalOvertimePay");
        }
        return new PayrollTotals(reg, ot, dt, reg + ot + dt, wages, sales, psales, tips, ptips, cc, extra);
    }

    static decimal D(DataRow r, string c) => r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToDecimal(r[c]) : 0m;
}

/// <summary>Payroll Review and the payroll file export, over the client database's existing functions (which the Access front end also uses).</summary>
public sealed class PayrollService
{
    /// <summary>Old rule: the payroll group is the lowest PayrollGroupNumber among the selected stores.</summary>
    public async Task<int> PayrollGroupAsync(SqlConnection cn, IReadOnlyCollection<int> storeIds, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(storeIds.Count == 0
            ? "select min(PayrollGroupNumber) from dbo.tblStores where Active <> 0"
            : $"select min(PayrollGroupNumber) from dbo.tblStores where StoreID in ({IdList(storeIds)})", cn) { CommandTimeout = 60 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
    }

    public async Task<DataTable> ReviewAsync(string connectionString, PayrollReviewRequest req, CancellationToken ct = default)
    {
        if (req.End < req.Start) throw new ArgumentException("The end date is before the start date.");
        if (req.StoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var group = await PayrollGroupAsync(cn, req.StoreIds, ct);
        // Store ids are integers, so the list is safe to inline; everything else is a parameter.
        await using var cmd = new SqlCommand($@"
select lps.*, isnull(lps.RegularHours,0) + isnull(lps.SplitShiftAdditionalTimeInHours,0) as RegularHoursPaid, lps.TotalHourlyPay as TotalWages
from dbo.fn_LaborPayrollReviewByDate3(@s, @e, @store, @emp, @week, @dept, @grp) lps
where lps.StoreID in ({IdList(req.StoreIds)})", cn) { CommandTimeout = 600 };
        cmd.Parameters.Add("@s", SqlDbType.DateTime).Value = req.Start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@e", SqlDbType.DateTime).Value = req.End.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@store", SqlDbType.Bit).Value = req.ByStore;
        cmd.Parameters.Add("@emp", SqlDbType.Bit).Value = req.ByEmployee;
        cmd.Parameters.Add("@week", SqlDbType.Bit).Value = req.ByWeek;
        cmd.Parameters.Add("@dept", SqlDbType.Bit).Value = req.ByDepartment;
        cmd.Parameters.Add("@grp", SqlDbType.BigInt).Value = (long)group;
        var table = new DataTable("PayrollReview");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        table.Load(rd);
        return table;
    }

    /// <summary>
    /// Builds the ADP files for the period. The export function works by payroll group, not by store (as in the old program),
    /// so it refuses to run when the group includes stores the user is not authorized for.
    /// </summary>
    public async Task<AdpExportResult> ExportAdpAsync(string connectionString, DateOnly start, DateOnly end, IReadOnlyCollection<int> selectedStoreIds,
        IReadOnlySet<int> authorizedStoreIds, CancellationToken ct = default)
    {
        if (end < start) throw new ArgumentException("The end date is before the start date.");
        if (selectedStoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);

        var type = await ScalarAsync(cn, "select top 1 PayrollExportType from dbo.tblGlobal", ct);
        if (type is null or DBNull || Convert.ToInt32(type) != 1)
            throw new NotSupportedException("This client's payroll export type is not ADP. Only the ADP export has been ported so far.");

        DataTable g = new();
        await using (var cmd = new SqlCommand("select top 1 * from dbo.tblGlobal", cn) { CommandTimeout = 60 })
        await using (var rd = await cmd.ExecuteReaderAsync(ct)) g.Load(rd);
        if (g.Rows.Count == 0) throw new InvalidOperationException("tblGlobal has no settings row.");
        var settings = AdpSettings.FromRow(g.Rows[0]);

        var group = await PayrollGroupAsync(cn, selectedStoreIds, ct);
        var rows = new List<AdpExportRow>();
        await using (var cmd = new SqlCommand("select * from dbo.fn_LaborPayrollExportByDate3(@s, @e, @wk, @grp) lpe", cn) { CommandTimeout = 600 })
        {
            cmd.Parameters.Add("@s", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@e", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@wk", SqlDbType.Bit).Value = settings.WeekMemoOn;
            cmd.Parameters.Add("@grp", SqlDbType.BigInt).Value = (long)group;
            var t = new DataTable();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            t.Load(rd);
            foreach (DataRow r in t.Rows) rows.Add(AdpExportRow.FromRow(r));
        }
        if (rows.Any(r => !authorizedStoreIds.Contains(r.StoreId)))
            throw new UnauthorizedAccessException("The payroll group for these stores includes stores you are not authorized to see, so the payroll file cannot be created for you.");

        var meals = new List<AdpMealRow>();
        await using (var cmd = new SqlCommand("select * from dbo.fn_LaborPayrollExportMealsByDate(@s, @e) lpe order by EmployeeName, PayrollID", cn) { CommandTimeout = 600 })
        {
            cmd.Parameters.Add("@s", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@e", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                meals.Add(new AdpMealRow(rd["PayrollID"] is DBNull ? null : Convert.ToInt64(rd["PayrollID"]), rd["EmployeeName"] as string, rd["MealCount"] is DBNull ? 0 : Convert.ToInt32(rd["MealCount"])));
        }
        return AdpPayrollExporter.Build(settings, rows, meals);
    }

    /// <summary>Stores the next ADP batch number, as the old export did after writing the files. Call it only after the user has the files.</summary>
    public async Task SaveNextBatchIdAsync(string connectionString, int nextBatchId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("update dbo.tblGlobal set PayrollExportADPBatchID = @b", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = nextBatchId;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static async Task<object?> ScalarAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        return await cmd.ExecuteScalarAsync(ct);
    }

    static string IdList(IReadOnlyCollection<int> ids) => string.Join(",", ids.Distinct().OrderBy(x => x));
}
