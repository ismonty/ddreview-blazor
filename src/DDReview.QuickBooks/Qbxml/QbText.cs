using System.Globalization;

namespace DDReview.QuickBooks.Qbxml;

/// <summary>Small text helpers carried over from the Access code (TrimSpacesAroundColons, fmt, name chains).</summary>
public static class QbText
{
    /// <summary>"Cash : Bank" becomes "Cash:Bank" (QuickBooks full names have no spaces around the colons).</summary>
    public static string TrimSpacesAroundColons(string s)
    {
        s = s.Trim();
        while (s.Contains(" :")) s = s.Replace(" :", ":");
        while (s.Contains(": ")) s = s.Replace(": ", ":");
        return s;
    }

    /// <summary>Money as QuickBooks wants it: two decimals, no thousands separator (VB Format "#0.00").</summary>
    public static string Fmt(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>
    /// Splits "A:B:C" into the chain (parent full name or "", this part, full name) for A, A:B, A:B:C
    /// so a parent can be created before its child.
    /// </summary>
    public static List<(string Parent, string Name, string FullName)> Chain(string fullName)
    {
        var parts = fullName.Split(':');
        var list = new List<(string, string, string)>();
        string parent = "";
        foreach (var p in parts)
        {
            var full = parent.Length > 0 ? parent + ":" + p : p;
            list.Add((parent, p, full));
            parent = full;
        }
        return list;
    }

    /// <summary>Account types in the package are spelled with spaces ("Other Current Asset"); QBXML has none.</summary>
    public static string AccountType(string t) => t.Replace(" ", "");
}
