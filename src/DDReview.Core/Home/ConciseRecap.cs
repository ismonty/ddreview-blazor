using System.Data;
using System.Globalization;

namespace DDReview.Core.Home;

/// <summary>The six figures the emailed Daily Sales Recap showed for one date. A comparison date with no data is null.</summary>
public sealed record RecapFigures(decimal? Gross, decimal? Discounts, decimal? Net, decimal? WeekToDate, decimal? Covers, decimal? WeekToDateCovers)
{
    public bool IsAllZero => (Gross ?? 0) == 0 && (Discounts ?? 0) == 0 && (Net ?? 0) == 0 && (WeekToDate ?? 0) == 0 && (Covers ?? 0) == 0 && (WeekToDateCovers ?? 0) == 0;
}

/// <summary>One store, or the All Stores total, with the day and the same weekday one and two years back.</summary>
public sealed record RecapLine(int DisplayOrder, string GroupName, int StoreNumber, string StoreName, bool IsTotal,
    RecapFigures Now, RecapFigures LastYear, RecapFigures TwoYearsAgo)
{
    /// <summary>A closed or not-yet-reporting store: nothing on the day or on either comparison day.</summary>
    public bool IsAllZero => Now.IsAllZero && LastYear.IsAllZero && TwoYearsAgo.IsAllZero;
}

/// <summary>A store group with its stores in store-number order.</summary>
public sealed record RecapGroup(string Name, IReadOnlyList<RecapLine> Stores);

/// <summary>
/// The home screen's Daily Recap: the numbers of the old emailed recap (dbo.fn_ConciseDailyRecapHTML2) without the HTML.
/// The rows come from dbo.fn_ConciseDailyRecapBlazor (sql/016), already limited to the user's stores.
/// </summary>
public sealed class ConciseRecap
{
    public ConciseRecap(DateOnly ending, IReadOnlyList<RecapGroup> groups, RecapLine? total)
    {
        Ending = ending; Groups = groups; Total = total;
    }

    public DateOnly Ending { get; }
    /// <summary>The same weekday a year back (364 days), the date the email compared with.</summary>
    public DateOnly LastYear => Ending.AddDays(-364);
    public DateOnly TwoYearsAgo => Ending.AddDays(-728);
    public IReadOnlyList<RecapGroup> Groups { get; }
    public RecapLine? Total { get; }
    public bool HasActivity => Groups.Count > 0;

    public static ConciseRecap Empty(DateOnly ending) => new(ending, [], null);

    /// <summary>Builds the recap from the function's rows. Stores with nothing at all are left out, and so are groups left empty; a zero-row table is fine.</summary>
    public static ConciseRecap From(DataTable t, DateOnly ending)
    {
        var lines = t.Rows.Cast<DataRow>().Select(Line).ToList();
        var groups = lines.Where(l => !l.IsTotal && !l.IsAllZero)
            .GroupBy(l => (l.DisplayOrder, l.GroupName))
            .OrderBy(g => g.Key.DisplayOrder).ThenBy(g => g.Key.GroupName, StringComparer.Ordinal)
            .Select(g => new RecapGroup(g.Key.GroupName, g.OrderBy(l => l.StoreNumber).ToList()))
            .ToList();
        var total = groups.Count == 0 ? null : lines.FirstOrDefault(l => l.IsTotal);
        return new ConciseRecap(ending, groups, total);
    }

    static RecapLine Line(DataRow r) => new(
        (int)(Dec(r, "DisplayOrder") ?? 0),
        Text(r, "StoreGroupName"),
        (int)(Dec(r, "StoreNumber") ?? 0),
        Text(r, "StoreName"),
        r.Table.Columns.Contains("IsTotal") && r["IsTotal"] is not DBNull && Convert.ToBoolean(r["IsTotal"], CultureInfo.InvariantCulture),
        Figures(r, ""), Figures(r, "LastYear"), Figures(r, "TwoYearsAgo"));

    static RecapFigures Figures(DataRow r, string suffix) => new(
        Dec(r, "GrossSales" + suffix), Dec(r, "Discounts" + suffix), Dec(r, "Sales" + suffix),
        Dec(r, "WeekToDate" + suffix), Dec(r, "Covers" + suffix), Dec(r, "WeekToDateCovers" + suffix));

    static decimal? Dec(DataRow r, string c) =>
        r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToDecimal(r[c], CultureInfo.InvariantCulture) : null;

    static string Text(DataRow r, string c) =>
        r.Table.Columns.Contains(c) && r[c] is not DBNull ? r[c].ToString()!.Trim() : "";

    /// <summary>Money as on the other home tabs (1,234.50); blank when there is no figure.</summary>
    public static string Money(decimal? v) => v is null ? "" : DailySummaryLine.Money(v.Value);

    /// <summary>A whole-number count such as covers; blank when there is no figure.</summary>
    public static string Count(decimal? v) => v is null ? "" : v.Value.ToString("N0", CultureInfo.InvariantCulture);
}
