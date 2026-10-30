using System.Data;
using System.Text.RegularExpressions;
using DDReview.Core.Labor.Legacy;
using DDReview.Core.Sales;
using DDReview.Core.Support;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Labor;

/// <summary>What the labor review can group by (the old "control columns"). PunchDetail also turns on Employee, as the old button did.</summary>
[Flags]
public enum LaborDimensions
{
    None = 0, StoreGroup = 1, Store = 2, LaborType = 4, LaborGroup = 8, LaborDepartment = 16, Employee = 32,
    OriginalPayrollID = 64, DayHourSlot = 128, DayTimeSlot = 256, PunchDetail = 512,
}

/// <param name="Filters">Optional per-dimension lists (digits, commas, colons, minus only), keyed by LaborType, LaborGroup, LaborDepartment, Employee, PayrollID, DayHourSlot, DayTimeSlot.</param>
public sealed record LaborRequest(
    DateOnly Start, DateOnly End, LaborDimensions Dimensions, IReadOnlyCollection<int> StoreIds,
    IReadOnlyDictionary<string, string>? Filters = null);

/// <summary>Runs the ported labor generator. Header-set values become SQL parameters (same approach as SalesService).</summary>
public sealed class LaborService
{
    static readonly Regex SafeList = new(@"^[0-9,:\- ]*$", RegexOptions.Compiled);
    static readonly HashSet<string> FilterNames = ["StoreGroup", "LaborType", "LaborGroup", "LaborDepartment", "Employee", "PayrollID", "DayHourSlot", "DayTimeSlot"];

    /// <summary>The script exactly as the legacy generator builds it (tests and diagnostics).</summary>
    public string BuildScript(LaborRequest req) => Configure(req).BuildDataScript();

    public async Task<DataTable> RunAsync(string connectionString, LaborRequest req, CancellationToken ct = default)
    {
        if (req.End < req.Start) throw new ArgumentException("The end date is before the start date.");
        if (req.StoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        var (script, pairs) = await DDReview.Core.Drill.PairSupport.BuildAsync(connectionString, () => BuildScript(req), ct);
        var (sql, pars) = SalesService.Parameterize(script);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        foreach (var p in pars) cmd.Parameters.Add(p);
        pairs?.AttachTo(cmd);
        var table = new DataTable("Labor");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        table.Load(rd);
        return table;
    }

    static DataAccessLaborReview Configure(LaborRequest req)
    {
        var r = new DataAccessLaborReview("");
        var d = req.Dimensions;
        r.StartDayNumber = DayNumber.FromDate(req.Start);
        r.EndDayNumber = DayNumber.FromDate(req.End);
        r.StoreListCSV = string.Join(",", req.StoreIds.Distinct().OrderBy(x => x));
        r.StoreGroupFlag = F(d, LaborDimensions.StoreGroup);
        r.StoreFlag = F(d, LaborDimensions.Store);
        r.LaborTypeFlag = F(d, LaborDimensions.LaborType);
        r.LaborGroupFlag = F(d, LaborDimensions.LaborGroup);
        r.LaborDepartmentFlag = F(d, LaborDimensions.LaborDepartment);
        r.EmployeeFlag = F(d, LaborDimensions.Employee | LaborDimensions.PunchDetail);   // punch detail implies employee
        r.PayrollIDFlag = F(d, LaborDimensions.OriginalPayrollID);
        r.DayHourSlotFlag = F(d, LaborDimensions.DayHourSlot);
        r.DayTimeSlotFlag = F(d, LaborDimensions.DayTimeSlot);
        r.EmployeeDetailFlag = F(d, LaborDimensions.PunchDetail);
        if (req.Filters != null)
            foreach (var (name, list) in req.Filters)
            {
                if (!FilterNames.Contains(name)) throw new ArgumentException("Unknown filter " + name);
                if (!SafeList.IsMatch(list)) throw new ArgumentException("Invalid filter list.");
                typeof(DataAccessLaborReview).GetField(name + "ListCSV")!.SetValue(r, list);
            }
        return r;
    }

    static int F(LaborDimensions d, LaborDimensions f) => (d & f) != 0 ? 1 : 0;
}
