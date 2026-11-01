namespace DDReview.Core.Legacy;

public static class LegacyCookies
{
    /// <summary>
    /// Removes the named cookies from a Cookie request header so the new site's sign-in cookie (and the antiforgery cookie) are never
    /// handed to the old site. Returns null when nothing is left.
    /// </summary>
    public static string? Strip(string? cookieHeader, params string[] prefixesToRemove)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)) return null;
        var kept = new List<string>();
        foreach (var part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = part.Split('=', 2)[0].Trim();
            if (prefixesToRemove.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            kept.Add(part);
        }
        return kept.Count == 0 ? null : string.Join("; ", kept);
    }
}
