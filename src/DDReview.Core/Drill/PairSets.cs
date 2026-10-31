using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Drill;

/// <summary>
/// The old filter lists name items per store as text: "1,2,3:8901,8902 1,2:8903 3:8904" means stores 1,2,3 with items 8901 and 8902,
/// stores 1,2 with item 8903, store 3 with item 8904 (the same name has a different id in different stores). The generators used to paste
/// that into the SQL as a chain of "StoreID in (...) and ItemID in (...) or ...". While a <see cref="PairScope"/> is active they instead
/// emit a lookup into one table-valued parameter of (Dim, StoreID, ItemID) pairs, so no list text reaches the SQL, there is no length
/// limit, and 150 stores do not make a 150-term OR chain. Without a scope the output is exactly what it always was.
/// </summary>
public sealed class PairScope : IDisposable
{
    [ThreadStatic] static PairScope? current;
    public static PairScope? Current => current;

    /// <summary>Name of the parameter and the user-defined table type (script sql/002_client_drill_pair_type.sql).</summary>
    public const string ParameterName = "@DrillPairs";
    public const string TypeName = "dbo.DrillPair";

    readonly PairScope? _outer;
    readonly List<(string Key, short Dim, HashSet<(int Store, long Item)> Pairs)> _sets = new();

    PairScope() { _outer = current; current = this; }
    public static PairScope Begin() => new();
    public void Dispose() => current = _outer;

    public bool HasPairs => _sets.Count > 0;

    /// <summary>The predicate for a list, or null when the list has no store:item groups (the caller then keeps its old text).</summary>
    public string? Output(string input, string storeExpr, string itemExpr)
    {
        var pairs = new HashSet<(int, long)>();
        var plain = new List<string>();
        foreach (var group in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = group.IndexOf(':');
            if (colon < 0) { plain.Add(Checked(group)); continue; }
            var stores = Numbers(group[..colon]);
            var items = Numbers(group[(colon + 1)..]);
            foreach (var s in stores) foreach (var i in items) pairs.Add((checked((int)s), i));
        }
        if (pairs.Count == 0 && plain.Count == 0) return "";
        var parts = new List<string>();
        if (pairs.Count > 0)
        {
            var dim = DimFor(storeExpr + "|" + itemExpr, pairs);
            parts.Add($"exists (select 1 from {ParameterName} dp{dim} where dp{dim}.Dim = {dim} and dp{dim}.StoreID = {storeExpr} and dp{dim}.ItemID = {itemExpr})");
        }
        foreach (var p in plain) parts.Add($"{itemExpr} in ({p})");
        return string.Join(" or ", parts);
    }

    short DimFor(string key, HashSet<(int, long)> pairs)
    {
        foreach (var s in _sets) if (s.Key == key && s.Pairs.SetEquals(pairs)) return s.Dim;
        short dim = (short)(_sets.Count + 1);
        _sets.Add((key, dim, pairs));
        return dim;
    }

    static string Checked(string plain)
    {
        Numbers(plain);
        return plain;
    }

    static List<long> Numbers(string csv)
    {
        var list = new List<long>();
        foreach (var t in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                throw new ArgumentException("Invalid filter list.");
            list.Add(n);
        }
        return list;
    }

    public DataTable ToTable()
    {
        var t = new DataTable();
        t.Columns.Add("Dim", typeof(short));
        t.Columns.Add("StoreID", typeof(int));
        t.Columns.Add("ItemID", typeof(long));
        foreach (var s in _sets)
            foreach (var (store, item) in s.Pairs.OrderBy(p => p.Store).ThenBy(p => p.Item))
                t.Rows.Add(s.Dim, store, item);
        return t;
    }

    /// <summary>Adds the pairs to a command as the table-valued parameter (no-op without pairs).</summary>
    public void AttachTo(SqlCommand cmd)
    {
        if (!HasPairs) return;
        cmd.Parameters.Add(new SqlParameter(ParameterName, SqlDbType.Structured) { TypeName = TypeName, Value = ToTable() });
    }
}

/// <summary>Finds out whether a client database has the pair table type, so the pair lookup is used only where it can work.</summary>
public static class PairSupport
{
    static readonly ConcurrentDictionary<string, (bool Has, DateTime At)> Cache = new();
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(5);

    public static async Task<bool> AvailableAsync(string connectionString, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(connectionString, out var c) && DateTime.UtcNow - c.At < Keep) return c.Has;
        bool has;
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"select case when type_id('{PairScope.TypeName}') is null then 0 else 1 end", cn);
            has = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
        }
        catch (SqlException) { has = false; }
        Cache[connectionString] = (has, DateTime.UtcNow);
        return has;
    }

    /// <summary>Builds a script, with the pair lookup when the database can do it. The scope (or null) goes to <see cref="PairScope.AttachTo"/>.</summary>
    public static async Task<(string Script, PairScope? Pairs)> BuildAsync(string connectionString, Func<string> build, CancellationToken ct = default)
    {
        if (!await AvailableAsync(connectionString, ct)) return (build(), null);
        using var scope = PairScope.Begin();
        var script = build();
        // the scope's data outlives the using; Dispose only restores the thread's previous scope
        return (script, scope.HasPairs ? scope : null);
    }
}
