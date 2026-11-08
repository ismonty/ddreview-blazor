using System.Data;
using System.Text.RegularExpressions;
using DDReview.Core.Sales;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Drill;

/// <summary>One box of the filter row: what a filter is limited to, in the old header-box style.</summary>
public sealed record FilterBox(string Key, string Title, IReadOnlyList<string> Names, bool Long);

/// <summary>
/// The old pages ran a small "header" query that answered, in one row, the names behind every active filter (even for a dimension that is
/// not being grouped by). This runs that query and turns its row into filter boxes.
/// </summary>
public static class FilterHeader
{
    public const int LongAfter = 8;   // the old page switched to "See Below" past eight names

    /// <summary>Runs the header script (built the same way as the report's own) and returns its row, or null if anything about it fails.</summary>
    public static async Task<DataRow?> RunAsync(string cs, Func<string> build, CancellationToken ct = default)
    {
        try
        {
            var (script, pairs) = await PairSupport.BuildAsync(cs, build, ct);
            var (sql, pars) = SalesService.Parameterize(script);
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
            foreach (var p in pars) cmd.Parameters.Add(p);
            pairs?.AttachTo(cmd);
            var t = new DataTable("Header");
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            t.Load(rd);
            return t.Rows.Count > 0 ? t.Rows[0] : null;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException or NullReferenceException) { return null; }
    }

    static readonly Regex Tags = new("<(?!br\\s*/?>)[^>]+>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Breaks = new("<br\\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The names in the header row for one dimension, or empty when the row has none (so the caller can fall back).</summary>
    public static (IReadOnlyList<string> Names, bool Long) Names(DataRow? header, DrillColumn col)
    {
        if (header is null) return ([], false);
        var t = header.Table;
        foreach (var c in col.Display.Reverse().Append(col.Key))
        {
            if (!t.Columns.Contains(c) || header[c] is DBNull) continue;
            var raw = Convert.ToString(header[c]) ?? "";
            var names = Breaks.Split(Tags.Replace(raw, "")).Select(x => x.Trim()).Where(x => x.Length > 0 && !x.Equals("All", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            if (names.Count == 0) continue;
            var flag = c.EndsWith("Name") ? c[..^4] : c;
            bool detail = new[] { c + "DetailFlag", flag + "DetailFlag", col.Key + "DetailFlag" }.Any(f => t.Columns.Contains(f) && header[f] is bool b && b);
            return (names, detail || names.Count > LongAfter);
        }
        return ([], false);
    }

    static readonly Regex Camel = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    /// <summary>The boxes for the state's active filters, in the order the filters were set. The header row is preferred; the report's own rows are the fallback.</summary>
    public static List<FilterBox> Boxes(DataRow? header, DataTable? result, IReadOnlyList<DrillColumn> cat, IEnumerable<string> filterKeys)
    {
        var boxes = new List<FilterBox>();
        foreach (var key in filterKeys)
        {
            var col = DrillState.Find(cat, key);
            IReadOnlyList<string> names = [];
            bool isLong = false;
            if (col is not null) (names, isLong) = Names(header, col);
            if (names.Count == 0)
            {
                names = FilterText.List(result, cat, key); isLong = names.Count > LongAfter;
            }
            var title = Camel.Replace(key, " ");
            if (names.Count > 1 && !title.EndsWith('s')) title += "s";
            boxes.Add(new FilterBox(key, title, names, isLong));
        }
        return boxes;
    }

    /// <summary>The one-line description of the active filters for an export, e.g. "Filters: Cashiers: A, B; Store: Main" (or "Filters: None").</summary>
    public static string Describe(IReadOnlyCollection<FilterBox> boxes) =>
        boxes.Count == 0 ? "Filters: None"
            : "Filters: " + string.Join("; ", boxes.Select(b => b.Names.Count == 0 ? b.Title : b.Title + ": " + string.Join(", ", b.Names)));
}
