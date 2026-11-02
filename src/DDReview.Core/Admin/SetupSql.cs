using System.Globalization;

namespace DDReview.Core.Admin;

/// <summary>A column as the client database has it (sys.columns). Older databases lack some columns; the grid only offers what exists.</summary>
public sealed record SetupDbColumn(string Name, string Type, bool Nullable, bool Identity, bool HasDefault, int MaxLength, int Precision, int Scale)
{
    /// <summary>A value must be given when adding a row: not null, and no default to fall back on.</summary>
    public bool Required => !Nullable && !HasDefault && !Identity;
    public bool Supported => Type is "int" or "smallint" or "tinyint" or "bigint" or "bit" or "money" or "smallmoney" or "decimal" or "numeric"
        or "datetime" or "smalldatetime" or "datetime2" or "date" or "varchar" or "char" or "nvarchar" or "nchar";
}

/// <summary>Builds the statements. Identifiers come only from <see cref="SetupTable"/> declarations (the caller passes column names it has already
/// matched against them); every value is a parameter: @store, @filter, @key, @v_&lt;column&gt; (new value), @o_&lt;column&gt; (value it had when loaded).</summary>
public static class SetupSql
{
    public static string Q(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static string Where(SetupTable t, bool byKey)
    {
        var w = new List<string>();
        if (byKey) w.Add(Q(t.Key) + " = @key");
        if (t.StoreColumn is not null) w.Add(Q(t.StoreColumn) + " = @store");
        if (t.FilterColumn is not null) w.Add(Q(t.FilterColumn) + " = @filter");
        return w.Count == 0 ? "" : " where " + string.Join(" and ", w);
    }

    public static string Select(SetupTable t, IReadOnlyList<string> columns, bool lockRow = false, bool byKey = false)
    {
        var order = (t.Order ?? []).Where(o => columns.Contains(o.Column)).Select(o => Q(o.Column) + (o.Descending ? " desc" : "")).Append(Q(t.Key));
        return "select " + string.Join(", ", columns.Prepend(t.Key).Distinct().Select(Q)) + " from dbo." + Q(t.Table) + (lockRow ? " with (updlock, rowlock)" : "")
            + Where(t, byKey) + (byKey ? "" : " order by " + string.Join(", ", order));
    }

    public static string Insert(SetupTable t, IReadOnlyList<string> columns) =>
        columns.Count == 0
            ? $"insert dbo.{Q(t.Table)} output inserted.{Q(t.Key)} default values"
            : $"insert dbo.{Q(t.Table)} ({string.Join(", ", columns.Select(Q))}) output inserted.{Q(t.Key)} values ({string.Join(", ", columns.Select(c => "@v_" + c))})";

    /// <summary>Changes the given columns of one row, but only if each still holds what it held when the page loaded it (a null matches a null).</summary>
    public static string Update(SetupTable t, IReadOnlyList<string> changed) =>
        $"update dbo.{Q(t.Table)} set {string.Join(", ", changed.Select(c => $"{Q(c)} = @v_{c}"))}{Where(t, true)}"
        + string.Concat(changed.Select(c => $" and ({Q(c)} = @o_{c} or ({Q(c)} is null and @o_{c} is null))"));

    public static string Delete(SetupTable t) => $"delete dbo.{Q(t.Table)}{Where(t, true)}";
}

/// <summary>Turns what was typed into a value for the column, and values back into the text a person sees.</summary>
public static class SetupValue
{
    private const decimal MoneyMax = 922337203685477.5807m;

    /// <summary>Parses non-blank text. Blank is the caller's business (null, the database default, or "required").</summary>
    public static bool TryParse(SetupColumn col, SetupDbColumn db, string text, out object? value, out string? error)
    {
        value = null; error = null;
        text = text.Trim();
        switch (col.Kind)
        {
            case SetupKind.Text:
                if (db.MaxLength > 0 && text.Length > db.MaxLength) { error = $"{col.Label} can be at most {db.MaxLength} characters."; return false; }
                value = text; return true;
            case SetupKind.Int:
                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) { error = $"{col.Label} must be a whole number."; return false; }
                var (lo, hi) = db.Type switch { "tinyint" => (0L, 255L), "smallint" => (-32768L, 32767L), "bigint" => (long.MinValue, long.MaxValue), _ => (int.MinValue, (long)int.MaxValue) };
                if (n < lo || n > hi) { error = $"{col.Label} is out of range."; return false; }
                value = n; return true;
            case SetupKind.Decimal:
                // a dollar sign is allowed in front (the invariant culture's own currency sign is another character)
                if (!decimal.TryParse(text.TrimStart('$'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture, out var d)) { error = $"{col.Label} must be a number."; return false; }
                if (db.Type is "money" or "smallmoney") { if (Math.Abs(d) > (db.Type == "money" ? MoneyMax : 214748.3647m)) { error = $"{col.Label} is too large."; return false; } }
                else if (db.Precision > 0 && Math.Abs(d) >= Pow10(db.Precision - db.Scale)) { error = $"{col.Label} is too large."; return false; }
                value = d; return true;
            case SetupKind.Bool:
                switch (text.ToLowerInvariant())
                {
                    case "true" or "1" or "yes" or "on": value = true; return true;
                    case "false" or "0" or "no" or "off": value = false; return true;
                    default: error = $"{col.Label} must be yes or no."; return false;
                }
            case SetupKind.Date:
                if (!DateTime.TryParseExact(text, ["yyyy-MM-dd", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { error = $"{col.Label} must be a date (yyyy-mm-dd)."; return false; }
                if (dt.Year < 1753) { error = $"{col.Label} is out of range."; return false; }
                value = dt; return true;
            default: error = "Unsupported column."; return false;
        }
    }

    private static decimal Pow10(int digits) { var r = 1m; for (var i = 0; i < digits; i++) r *= 10m; return r; }

    /// <summary>The canonical text of a stored value ("" for null). Comparing two of these tells whether a field really changed.</summary>
    public static string Format(SetupColumn col, object? v) => v switch
    {
        null or DBNull => "",
        bool b => b ? "true" : "false",
        decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s => s.TrimEnd(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => v.ToString() ?? "",
    };
}
