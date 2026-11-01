namespace DDReview.Core.Legacy;

/// <summary>
/// Decides which paths under /classic may be passed to the old site. Default is "no": only known page, image, script and chart/gauge handler
/// types get through, and anything that smells like path trickery is refused before it reaches the old (unpatched) server.
/// </summary>
public static class LegacyPathPolicy
{
    static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".aspx", ".asmx", ".png", ".gif", ".jpg", ".jpeg", ".css", ".js", ".ico", ".axd", ".svg" };

    // .axd is a handler family; only these are allowed (trace.axd and elmah.axd are not).
    static readonly HashSet<string> AllowedHandlers = new(StringComparer.OrdinalIgnoreCase)
    { "ChartAxd.axd", "ScriptResource.axd", "WebResource.axd" };

    static readonly string[] BlockedSegments =
    { "bin", "app_code", "app_data", "app_themes", "tempfiles", "temp_files", "aspnet_client", "web.config", "trace.axd", "elmah.axd", "download" };

    public static bool IsAllowed(string? path, LegacyProxyOptions o)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 400 || !path.StartsWith(o.PathPrefix + "/", StringComparison.OrdinalIgnoreCase)) return false;
        // Anything that could hide a different path from this check: encoded or backslash characters, dot segments, cookieless-session
        // tokens "(S(...))", NUL, alternate data streams, ";" parameters, doubled slashes.
        foreach (char c in path)
            if (c is '\\' or '%' or '(' or ')' or ';' or ':' or '\0' or '*' or '?' or '<' or '>' or '|' or '"' || c < ' ') return false;
        if (path.Contains("//", StringComparison.Ordinal)) return false;

        var segments = path[(o.PathPrefix.Length + 1)..].Split('/');
        foreach (var s in segments)
        {
            if (s.Length == 0 && s != segments[^1]) return false;
            if (s == "." || s == ".." || s.EndsWith('.') || s.EndsWith(' ')) return false;
        }
        for (int i = 0; i < segments.Length - 1; i++)
            if (IsBlocked(segments[i], o)) return false;
        string last = segments[^1];
        if (last.Length == 0) return true;                    // "/classic/" : the old site's default document
        if (IsBlocked(last, o)) return false;

        if (o.HandoffOn && string.Equals(path, o.LegacyLoginPath, StringComparison.OrdinalIgnoreCase))
            return false;                                     // handoff on: nobody uses the old login page any more

        string ext = Path.GetExtension(last);
        if (!AllowedExtensions.Contains(ext)) return false;
        if (ext.Equals(".axd", StringComparison.OrdinalIgnoreCase) && !AllowedHandlers.Contains(last)) return false;
        return true;
    }

    static bool IsBlocked(string segment, LegacyProxyOptions o)
    {
        foreach (var b in BlockedSegments)
        {
            if (!segment.Equals(b, StringComparison.OrdinalIgnoreCase)) continue;
            return !(o.AllowDownloads && b == "download");
        }
        return false;
    }
}
