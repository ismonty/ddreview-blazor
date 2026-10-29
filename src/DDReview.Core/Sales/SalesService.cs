using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using DDReview.Core.Sales.Legacy;
using DDReview.Core.Support;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Sales;

/// <summary>The report dimensions the Sales Review can group by (the ones seen in real use plus the rest of the legacy set).</summary>
[Flags]
public enum SalesDimensions
{
    None = 0, DayPart = 1, DayHour = 2, StoreGroup = 4, Store = 8, ProfitCenter = 16, MainType = 32, MainGroup = 64,
    SaleType = 128, MenuItem = 256, MenuItemPLU = 512, Discount = 1024, Special = 2048, Week = 4096, WeekDay = 8192,
    Day = 16384, StoreStaff = 32768, CheckNumber = 65536,
}

/// <param name="CompareOffsetDays">null = no comparison period; otherwise the comparison period is shifted by this many days (e.g. -364).</param>
/// <param name="StoreIds">Stores to include; the caller must already have limited these to what the user may see.</param>
/// <param name="Filters">Optional per-dimension filter lists (digits, commas, colons only), keyed by dimension.</param>
public sealed record SalesRequest(
    DateOnly Start, DateOnly End, SalesDimensions Dimensions, IReadOnlyCollection<int> StoreIds,
    int? CompareOffsetDays = null, IReadOnlyDictionary<SalesDimensions, string>? Filters = null);

public sealed class SalesService
{
    static readonly Regex SafeList = new(@"^[0-9,:\-]*$", RegexOptions.Compiled);
    static readonly Regex HeaderSet = new(@"^(?<pre>[ \t]*)set @(?<n>\w+)\s*=\s*(?<v>[^\r\n]*)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Builds the script exactly as the legacy generator would (used by tests and for diagnostics).</summary>
    public string BuildScript(string connectionString, SalesRequest req, ISalesLookups? lookups = null)
    {
        var r = Configure(connectionString, req, lookups);
        return r.BuildDataScript(true);
    }

    public async Task<DataTable> RunAsync(string connectionString, SalesRequest req, CancellationToken ct = default)
    {
        if (req.End < req.Start) throw new ArgumentException("The end date is before the start date.");
        if (req.StoreIds.Count == 0) throw new ArgumentException("No stores selected.");
        var script = BuildScript(connectionString, req);
        var (sql, pars) = Parameterize(script);

        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        foreach (var p in pars) cmd.Parameters.Add(p);
        var table = new DataTable("Sales");
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        table.Load(rd);
        return table;
    }

    DataAccessSalesReview Configure(string cs, SalesRequest req, ISalesLookups? lookups)
    {
        var r = new DataAccessSalesReview(cs) { Lookups = lookups ?? new SqlSalesLookups(cs) };
        r.StartDayNumber = DayNumber.FromDate(req.Start);
        r.EndDayNumber = DayNumber.FromDate(req.End);
        r.OffsetDays = req.CompareOffsetDays ?? int.MinValue;
        r.StoreListCSV = string.Join(",", req.StoreIds.Distinct().OrderBy(x => x));
        var d = req.Dimensions;
        r.DayPartFlag = Has(d, SalesDimensions.DayPart); r.DayHourFlag = Has(d, SalesDimensions.DayHour);
        r.StoreGroupFlag = Has(d, SalesDimensions.StoreGroup); r.StoreFlag = Has(d, SalesDimensions.Store);
        r.ProfitCenterFlag = Has(d, SalesDimensions.ProfitCenter); r.MainTypeFlag = Has(d, SalesDimensions.MainType);
        r.MainGroupFlag = Has(d, SalesDimensions.MainGroup); r.SaleTypeFlag = Has(d, SalesDimensions.SaleType);
        r.MenuItemFlag = Has(d, SalesDimensions.MenuItem); r.MenuItemPLUFlag = Has(d, SalesDimensions.MenuItemPLU);
        r.DiscountFlag = Has(d, SalesDimensions.Discount); r.SpecialFlag = Has(d, SalesDimensions.Special);
        r.WeekFlag = Has(d, SalesDimensions.Week); r.WeekDayFlag = Has(d, SalesDimensions.WeekDay);
        r.DayFlag = Has(d, SalesDimensions.Day); r.StoreStaffFlag = Has(d, SalesDimensions.StoreStaff);
        r.CheckNumberFlag = Has(d, SalesDimensions.CheckNumber);
        if (req.Filters != null)
            foreach (var (dim, list) in req.Filters)
            {
                if (!SafeList.IsMatch(list)) throw new ArgumentException("Invalid filter list.");
                var f = typeof(DataAccessSalesReview).GetField(dim + "ListCSV") ?? throw new ArgumentException("Unknown filter " + dim);
                f.SetValue(r, list);
            }
        return r;
    }

    static int Has(SalesDimensions d, SalesDimensions f) => d.HasFlag(f) ? 1 : 0;

    /// <summary>
    /// Replaces the literal values in the script's header (<c>set @X=literal</c>) with SQL parameters.
    /// The generator still inlines some store lists inside the body; those are built from validated integers only.
    /// </summary>
    public static (string Sql, List<SqlParameter> Parameters) Parameterize(string script)
    {
        var pars = new List<SqlParameter>();
        var seen = new HashSet<string>();
        var sql = HeaderSet.Replace(script, m =>
        {
            var n = m.Groups["n"].Value; var v = m.Groups["v"].Value.Trim();
            if (!seen.Add(n)) return m.Value;                           // only the header assignment, not later reuse
            SqlParameter? p;
            if (n.EndsWith("ListCSV", StringComparison.Ordinal) && v.StartsWith('\'') && v.EndsWith('\''))
            {
                var s = v[1..^1].Replace("''", "'");
                if (!SafeList.IsMatch(s)) throw new InvalidOperationException("Unsafe list value in " + n);
                p = new SqlParameter("@p_" + n, SqlDbType.VarChar, 8000) { Value = s };
            }
            else if (n.EndsWith("Flag", StringComparison.Ordinal) || n.StartsWith("Include", StringComparison.Ordinal)
                  || n.EndsWith("DayNumber", StringComparison.Ordinal))
            {
                if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var i)) return m.Value;
                p = new SqlParameter("@p_" + n, SqlDbType.Int) { Value = i };
            }
            else return m.Value;
            pars.Add(p);
            return m.Groups["pre"].Value + "set @" + n + " = " + p.ParameterName;
        });
        return (sql, pars);
    }
}
