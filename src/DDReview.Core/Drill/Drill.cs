using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Drill;

/// <summary>
/// One "control column" of the old review grids: a dimension the report can group by, the columns that show it, the column
/// that holds a row's id (used to filter when the row is clicked) and the dimension a click moves on to.
/// </summary>
/// <param name="Key">Name of the dimension (the same as the request's dimension enum member).</param>
/// <param name="Display">Result columns that show it; the first one carries the header buttons.</param>
/// <param name="IdColumn">Result column holding the id used for a filter (null = not clickable).</param>
/// <param name="NextKey">The dimension turned on by a row click and by "Drill Right".</param>
/// <param name="NameFunction">Client-database function that turns a clicked name into the id list (same-named items in several stores); null = use the id.</param>
/// <param name="SortColumn">Column the dimension sorts by when it is not the display text (a day number rather than a day name).</param>
/// <param name="FilterName">Name of the request's filter list when it differs from the key.</param>
public sealed record DrillColumn(string Key, string[] Display, string IdColumn, string NextKey, string? NameFunction = null,
    string? SortColumn = null, string? FilterName = null)
{
    public string Filter => FilterName ?? Key;
}

/// <summary>
/// The click state of a review grid: which dimensions are on, the filter list of each and the sort. The methods are the old
/// header buttons and row clicks (ReviewPage.ClickState: ClearFlagsAndSet, ClearFlagAndSet, ClearFilter).
/// </summary>
public sealed class DrillState
{
    public HashSet<string> Flags { get; } = [];
    public Dictionary<string, string> Filters { get; } = [];
    /// <summary>Header text of the sorted column, or null for the report's own order.</summary>
    public string? SortHeader { get; set; }
    public bool SortDescending { get; set; }

    public DrillState Clone()
    {
        var c = new DrillState { SortHeader = SortHeader, SortDescending = SortDescending };
        foreach (var f in Flags) c.Flags.Add(f);
        foreach (var (k, v) in Filters) c.Filters[k] = v;
        return c;
    }

    /// <summary>Header title click: this dimension alone, filters kept.</summary>
    public void ShowOnly(string key) { Flags.Clear(); Flags.Add(key); }
    /// <summary>"Drill Up": add this dimension to the grouping.</summary>
    public void DrillUp(string key) => Flags.Add(key);
    /// <summary>"Drill Right": add the dimension that follows this one.</summary>
    public void DrillRight(IReadOnlyList<DrillColumn> cols, string key)
    {
        var c = Find(cols, key); if (c is not null) Flags.Add(c.NextKey);
    }
    /// <summary>"Show All": forget this dimension's filter.</summary>
    public void ShowAll(string key) => Filters.Remove(key);
    /// <summary>A row's cell was clicked: filter to it, stop grouping by it, group by the next dimension.</summary>
    public void RowClick(IReadOnlyList<DrillColumn> cols, string key, string filterValue)
    {
        var c = Find(cols, key); if (c is null || filterValue.Length == 0) return;
        Filters[key] = filterValue;
        Flags.Remove(key);
        Flags.Add(c.NextKey);
    }
    /// <summary>Sort by a header; a second click on the same header reverses it. Numbers start high to low, text low to high.</summary>
    public void Sort(string header, bool numeric)
    {
        if (SortHeader == header) SortDescending = !SortDescending;
        else { SortHeader = header; SortDescending = numeric; }
    }

    public static DrillColumn? Find(IReadOnlyList<DrillColumn> cols, string key) => cols.FirstOrDefault(c => c.Key == key);
    public static DrillColumn? ForDisplay(IReadOnlyList<DrillColumn> cols, string column) => cols.FirstOrDefault(c => c.Display.Contains(column));

    /// <summary>The request's dimension flags for this state (keys that are not members of the enum are ignored).</summary>
    public T Dimensions<T>() where T : struct, Enum
    {
        long v = 0;
        foreach (var f in Flags) if (Enum.TryParse<T>(f, out var e)) v |= Convert.ToInt64(e);
        return (T)Enum.ToObject(typeof(T), v);
    }

    public void SetDimensions<T>(T dims) where T : struct, Enum
    {
        Flags.Clear();
        foreach (var e in Enum.GetValues<T>()) if (Convert.ToInt64(e) != 0 && dims.HasFlag(e)) Flags.Add(e.ToString());
    }
}

public static class DrillFilters
{
    /// <summary>
    /// Splits the state's filters into the stores to run and the other filter lists. A Store filter can only narrow the stores
    /// the user already has; it can never add one.
    /// </summary>
    public static (List<int> StoreIds, Dictionary<string, string> Lists) Split(DrillState state, IReadOnlyList<DrillColumn> cols, IEnumerable<int> allowedSelected)
    {
        var ids = allowedSelected.Distinct().ToList();
        var lists = new Dictionary<string, string>();
        foreach (var (key, value) in state.Filters)
        {
            if (value.Length == 0) continue;
            if (key == "Store")
            {
                var want = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var i) ? i : (int?)null)
                    .Where(i => i.HasValue).Select(i => i!.Value).ToHashSet();
                ids = ids.Where(want.Contains).ToList();
            }
            else
            {
                var c = DrillState.Find(cols, key);
                lists[c?.Filter ?? key] = value;
            }
        }
        return (ids, lists);
    }
}

/// <summary>Sorts result rows the way a clicked header asked.</summary>
public static class GridSort
{
    public static IEnumerable<DataRow> Rows(DataTable t, Func<DataRow, IComparable?> key, bool descending)
    {
        var rows = t.Rows.Cast<DataRow>();
        // Nulls sort first ascending, last descending; OrderBy is stable so ties keep the report's order.
        return descending ? rows.OrderByDescending(key, Comparer) : rows.OrderBy(key, Comparer);
    }

    static readonly IComparer<IComparable?> Comparer = Comparer<IComparable?>.Create((a, b) =>
    {
        if (a is null) return b is null ? 0 : -1;
        if (b is null) return 1;
        if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
        return a.CompareTo(b);
    });

    /// <summary>Key for a plain column: numbers as decimals, dates as dates, everything else as text.</summary>
    public static Func<DataRow, IComparable?> Column(DataTable t, string column, string? denominator = null)
    {
        if (!t.Columns.Contains(column)) return _ => null;
        return row =>
        {
            var v = row[column];
            if (v is DBNull) return null;
            if (denominator is not null)
            {
                if (!t.Columns.Contains(denominator) || row[denominator] is DBNull) return null;
                var d = Convert.ToDecimal(row[denominator]);
                return d == 0 ? null : Convert.ToDecimal(v) / d;
            }
            return v switch
            {
                string s => s.Trim(),
                DateTime dt => dt,
                bool b => b ? 1 : 0,
                IConvertible c when v is not char => Convert.ToDecimal(c),
                _ => v.ToString(),
            };
        };
    }
}

/// <summary>Turns a clicked name into the id list the generator filters on, using the client database's own functions.</summary>
public sealed class DrillKeyResolver
{
    static readonly HashSet<string> Functions =
    [
        "fn_ParsePairSetStringAddProfitCenter", "fn_ParsePairSetStringAddMainGroup", "fn_ParsePairSetStringAddSaleType",
        "fn_ParsePairSetStringAddStoreStaff", "fn_ParsePairSetStringAddMenuItem", "fn_ParsePairSetStringAddMenuItemPLU",
        "fn_ParsePairSetStringAddDiscount", "fn_ParsePairSetStringAddCheckNumber", "fn_ParsePairSetStringAddStaffDepartmentName",
        "fn_ParsePairSetStringAddStaffDepartmentGroup", "fn_ParsePairSetStringAddReceiptType",
    ];
    static readonly Regex Safe = new(@"^[0-9,:\- ]*$", RegexOptions.Compiled);

    /// <summary>The value to store as the filter for a clicked row. Falls back to the plain id when there is no function or the answer is not a clean list.</summary>
    public async Task<string> ResolveAsync(string connectionString, DrillColumn col, DataRow row, CancellationToken ct = default)
    {
        string Id() => row.Table.Columns.Contains(col.IdColumn) && row[col.IdColumn] is not DBNull ? Convert.ToString(row[col.IdColumn], System.Globalization.CultureInfo.InvariantCulture)!.Trim() : "";
        if (col.NameFunction is null || !Functions.Contains(col.NameFunction)) return Safe.IsMatch(Id()) ? Id() : "";
        var nameCol = col.Display[^1];
        if (!row.Table.Columns.Contains(nameCol) || row[nameCol] is DBNull) return Safe.IsMatch(Id()) ? Id() : "";
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"select dbo.{col.NameFunction}(@a, @b)", cn);
            cmd.Parameters.Add(new SqlParameter("@a", SqlDbType.VarChar, 8000) { Value = "" });
            cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.VarChar, 500) { Value = Convert.ToString(row[nameCol])!.Trim() });
            var o = await cmd.ExecuteScalarAsync(ct);
            var s = o is null or DBNull ? "" : Convert.ToString(o)!.Trim();
            if (s.Length > 0 && Safe.IsMatch(s)) return s;
        }
        catch (SqlException) { /* fall back to the id below */ }
        return Safe.IsMatch(Id()) ? Id() : "";
    }
}
