using System.Data;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Home;

/// <summary>How the dashboard totals are scoped (the old "Date Range / Week Day / Calendar Day" drop-down).</summary>
public enum DashboardScope { DateRange = 1, WeekDay = 2, CalendarDay = 3 }

/// <summary>
/// Data for the home tabs. All the numbers come from the client database's own SQL functions
/// (fn_DashBoardAlerts, fn_DashBoardSummary, fn_DailySummaryByStore and friends); this class only calls them,
/// with real parameters instead of the old string-built dates.
/// </summary>
public sealed class HomeService
{
    static string Csv(IEnumerable<int> ids) => string.Join(",", ids.Distinct().OrderBy(x => x));

    static async Task<DataTable> QueryAsync(string cs, string sql, string name, int timeout, CancellationToken ct, params SqlParameter[] pars)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = timeout };
        cmd.Parameters.AddRange(pars);
        var t = new DataTable(name);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        t.Load(rd);
        return t;
    }

    static SqlParameter Date(string n, DateOnly d) => new(n, SqlDbType.DateTime) { Value = d.ToDateTime(TimeOnly.MinValue) };
    static SqlParameter List(string n, IEnumerable<int> ids) => new(n, SqlDbType.VarChar, 8000) { Value = Csv(ids) };

    /// <summary>
    /// Whether this database shows the Daily Recap tab (Setup > Global Settings > "Daily Recap on", dbo.tblGlobal.DailyRecapEnabled, sql/019).
    /// On when the column is not there yet, the table has no row, or the value is null; a database that has never run 019 keeps its recap.
    /// </summary>
    public async Task<bool> DailyRecapEnabledAsync(string cs, CancellationToken ct = default)
    {
        const string sql = @"
            if col_length(N'dbo.tblGlobal', N'DailyRecapEnabled') is null select cast(1 as int) as Enabled
            else exec sp_executesql N'select top 1 cast(coalesce(DailyRecapEnabled, 1) as int) as Enabled from dbo.tblGlobal order by ItemID'";
        var t = await QueryAsync(cs, sql, "RecapSwitch", 30, ct);
        return t.Rows.Count == 0 || Convert.ToInt32(t.Rows[0]["Enabled"]) != 0;
    }

    /// <summary>Alerts: SectionNumber 1 = missing / immature batches, 2 and 4 = void summaries. monthsBack 0 = current month.</summary>
    public Task<DataTable> AlertsAsync(string cs, IEnumerable<int> authorizedStores, int monthsBack, CancellationToken ct = default) =>
        QueryAsync(cs, "select * from dbo.fn_DashBoardAlerts(@list, @m, 0) order by SectionNumber, StoreReportName, PostingDate",
            "Alerts", 600, ct, List("@list", authorizedStores), new SqlParameter("@m", SqlDbType.Int) { Value = monthsBack });

    /// <summary>The This Week / Last Week numbers. singleStore = null combines the authorized stores (the old "All Stores").</summary>
    public Task<DataTable> NumbersAsync(string cs, IEnumerable<int> authorizedStores, int? singleStore, DateOnly start, DateOnly end, DateOnly previousStart, CancellationToken ct = default)
    {
        var flag = singleStore is null ? 0 : 1;
        var list = singleStore is null ? Csv(authorizedStores) : singleStore.Value.ToString();
        return QueryAsync(cs, "select * from dbo.fn_DashBoardSummary(@flag, @list, 0, @s, @e, @p, '') order by StoreNumber, SectionNumber, DisplayOrderID",
            "Numbers", 600, ct,
            new SqlParameter("@flag", SqlDbType.Int) { Value = flag },
            new SqlParameter("@list", SqlDbType.VarChar, 8000) { Value = list },
            Date("@s", start), Date("@e", end), Date("@p", previousStart));
    }

    /// <summary>
    /// Daily Summary by store, plus register-processing status. The old page first ran cs_spDailySummaryByStoreCheckPreTotalSales
    /// to fill its pre-total cache; that is kept (best effort) because the summary function reads what it fills.
    /// </summary>
    public async Task<(DataTable Stores, DataTable Status)> DailySummaryAsync(string cs, IEnumerable<int> stores, DateOnly start, DateOnly end, DateOnly previousStart, CancellationToken ct = default)
    {
        await PreTotalsAsync(cs, start, end, previousStart, ct);
        var list = List("@list", stores);
        var rows = await QueryAsync(cs, "select * from dbo.fn_DailySummaryByStore(@s, @e, @p, @list) order by StoreNumber", "DailySummary", 600, ct,
            Date("@s", start), Date("@e", end), Date("@p", previousStart), list);
        return (rows, await StatusAsync(cs, stores, ct));
    }

    /// <summary>
    /// The Daily Recap (the numbers of the emailed recap) for one ending day, limited to <paramref name="stores"/>.
    /// Returns null when this database does not have dbo.fn_ConciseDailyRecapBlazor2 yet (sql/018, added by Database Updater).
    /// </summary>
    public async Task<ConciseRecap?> ConciseRecapAsync(string cs, IEnumerable<int> stores, DateOnly ending, CancellationToken ct = default)
    {
        var ids = stores.Distinct().ToList();
        if (ids.Count == 0) return ConciseRecap.Empty(ending);          // no stores: nothing to show (the function would return nothing too)
        try
        {
            // The three day numbers (the day, 364 and 728 days back) are worked out once here and passed in as plain numbers: inside the query they made SQL Server run a
            // nested scalar function over the sales table (see sql/018).
            var days = (await QueryAsync(cs,
                "select dbo.fn_CalcDayNumber(@d) as D0, dbo.fn_CalcDayNumber(dateadd(day, -364, @d)) as D1, dbo.fn_CalcDayNumber(dateadd(day, -728, @d)) as D2",
                "RecapDays", 30, ct, Date("@d", ending))).Rows[0];
            var t = await QueryAsync(cs, "select * from dbo.fn_ConciseDailyRecapBlazor2(@d, @n0, @n1, @n2, @list) order by RowSeq", "Recap", 300, ct,
                Date("@d", ending), new SqlParameter("@n0", (int)days["D0"]), new SqlParameter("@n1", (int)days["D1"]), new SqlParameter("@n2", (int)days["D2"]), List("@list", ids));
            return ConciseRecap.From(t, ending);
        }
        catch (SqlException ex) when (ex.Number is 208 or 4121)
        {
            return null;                                                  // an older database: the card says so and the rest of the page still loads
        }
    }

    /// <summary>Register-processing status per group of stores; StoreProcessingStatus above 0 means running behind.</summary>
    public async Task<DataTable> StatusAsync(string cs, IEnumerable<int> stores, CancellationToken ct = default)
    {
        try
        {
            return await QueryAsync(cs, "select StoreProcessingStatus, StoreProcessingStatusDescription, StoreNameList from dbo.fn_StoreRegisterProcessingStatus(@list) order by StoreProcessingStatus desc",
                "Status", 600, ct, List("@list", stores));
        }
        catch (SqlException ex) when (ex.Number is 208 or 4121)
        {
            // An older database without dbo.fn_StoreRegisterProcessingStatus: no "running behind" notice, but the page still loads.
            var none = new DataTable("Status");
            none.Columns.Add("StoreProcessingStatus", typeof(int));
            none.Columns.Add("StoreProcessingStatusDescription", typeof(string));
            none.Columns.Add("StoreNameList", typeof(string));
            return none;
        }
    }

    /// <summary>Dashboard source rows (the gauges and charts add these up). storeIds = the stores to include.</summary>
    public async Task<DataTable> DashboardAsync(string cs, IEnumerable<int> storeIds, DashboardScope scope, DateOnly start, DateOnly end, DateOnly previousStart, CancellationToken ct = default)
    {
        await PreTotalsAsync(cs, start, end, previousStart, ct);
        var fn = scope switch { DashboardScope.WeekDay => "fn_DailySummaryByWeekDay", DashboardScope.CalendarDay => "fn_DailySummaryByCalendarDay", _ => "fn_DailySummaryByStore" };
        return await QueryAsync(cs, $"select * from dbo.{fn}(@s, @e, @p, @list)", "Dashboard", 300, ct,
            Date("@s", start), Date("@e", end), Date("@p", previousStart), List("@list", storeIds));
    }

    static async Task PreTotalsAsync(string cs, DateOnly start, DateOnly end, DateOnly previousStart, CancellationToken ct)
    {
        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("exec dbo.cs_spDailySummaryByStoreCheckPreTotalSales @s, @e, @p, ''", cn) { CommandTimeout = 120 };
            cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.VarChar, 20) { Value = start.ToString("MM/dd/yyyy") });
            cmd.Parameters.Add(new SqlParameter("@e", SqlDbType.VarChar, 20) { Value = end.ToString("MM/dd/yyyy") });
            cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.VarChar, 20) { Value = previousStart.ToString("MM/dd/yyyy") });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException) { /* the old page ignored a slow or missing pre-total step too; the function still answers */ }
    }

    /// <summary>The Monday on or before <paramref name="d"/> (DayNumber 0 is a Monday).</summary>
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
