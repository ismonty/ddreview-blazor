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

    /// <summary>Register-processing status per group of stores; StoreProcessingStatus above 0 means running behind.</summary>
    public Task<DataTable> StatusAsync(string cs, IEnumerable<int> stores, CancellationToken ct = default) =>
        QueryAsync(cs, "select StoreProcessingStatus, StoreProcessingStatusDescription, StoreNameList from dbo.fn_StoreRegisterProcessingStatus(@list) order by StoreProcessingStatus desc",
            "Status", 600, ct, List("@list", stores));

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
