namespace DDReview.Core.Admin;

/// <summary>Where the Google verification sends the person back to. Only local /admin/ paths are accepted, so the endpoint cannot be used as an open redirect.</summary>
public static class SetupReturn
{
    public const string Landing = "/admin/setup";

    public static string SafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 200) return Landing;
        if (!path.StartsWith("/admin/", StringComparison.Ordinal) || path.Contains("//") || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)) return Landing;
        var q = path.IndexOf('?');
        return q >= 0 ? path[..q] : path;     // a flag is added on the way back; anything else in the query is dropped
    }

    public static string WithFlag(string path, string flag) => path + "?v=" + flag;
}
