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

    static string Cell(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] is not DBNull
            ? Convert.ToString(row[column], System.Globalization.CultureInfo.InvariantCulture)!.Trim() : "";

    /// <summary>A cell is clickable when it has what the filter is built from: the name for name-looked-up dimensions (as in the old grid), otherwise the id.</summary>
    public static bool CanClick(DataRow row, DrillColumn col) =>
        Cell(row, col.NameFunction is not null && Functions.Contains(col.NameFunction) ? col.Display[^1] : col.IdColumn).Length > 0;

    /// <summary>
    /// The filter for a click: the clicked row plus every checked row (the old grid's row check boxes), each added once.
    /// Name-looked-up dimensions call the client database function once per row, feeding it the list so far, as the old grid did;
    /// the others join the row ids. Returns "" when nothing clean can be built.
    /// </summary>
    public async Task<string> ResolveAsync(string connectionString, DrillColumn col, DataRow clicked, IEnumerable<DataRow>? checkedRows = null, CancellationToken ct = default)
    {
        var rows = new List<DataRow> { clicked };
        if (checkedRows is not null) foreach (var r in checkedRows) if (!rows.Contains(r)) rows.Add(r);
        bool byName = col.NameFunction is not null && Functions.Contains(col.NameFunction);
        if (!byName)
        {
            var ids = rows.Select(r => Cell(r, col.IdColumn)).Where(x => x.Length > 0).Distinct().ToList();
            var joined = string.Join(",", ids);
            return Safe.IsMatch(joined) ? joined : "";
        }
        var acc = "";
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            foreach (var r in rows)
            {
                var name = Cell(r, col.Display[^1]);
                if (name.Length == 0) continue;
                await using var cmd = new SqlCommand($"select dbo.{col.NameFunction}(@a, @b)", cn);
                cmd.Parameters.Add(new SqlParameter("@a", SqlDbType.VarChar, 8000) { Value = acc });
                cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.VarChar, 500) { Value = name });
                var o = await cmd.ExecuteScalarAsync(ct);
                var next = o is null or DBNull ? "" : Convert.ToString(o)!.Trim();
                if (next.Length > 0 && Safe.IsMatch(next)) acc = next;
            }
        }
        catch (SqlException) { acc = ""; }
        // If the function fails or answers with something unclean, do nothing rather than fall back to one store's id:
        // the same name has a different id in each store, so a plain id would silently drop the other stores.
        return acc;
    }
}

/// <summary>Names for the "Filtered on" line: what each active filter is limited to, read from the result's own name columns
/// (the report fills them with the filtered names, or "All", even for a dimension that isn't grouped by).</summary>
public static class FilterText
{
    public static string Names(System.Data.DataTable? result, IReadOnlyList<DrillColumn> cat, string key, int max = 4)
    {
        var col = DrillState.Find(cat, key);
        if (result is null || col is null) return "";
        var names = new List<string>();
        foreach (var c in col.Display)
        {
            if (!result.Columns.Contains(c)) continue;
            var part = result.Rows.Cast<System.Data.DataRow>()
                .Select(r => r[c] is DBNull ? "" : Convert.ToString(r[c])!.Trim())
                .Where(v => v.Length > 0 && !v.Equals("All", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            if (part.Count > 0) { names = part; if (c == col.Display[^1]) break; }
        }
        if (names.Count == 0) return "";
        var more = names.Count > max ? $" +{names.Count - max} more" : "";
        return string.Join(", ", names.Take(max)) + more;
    }
}
