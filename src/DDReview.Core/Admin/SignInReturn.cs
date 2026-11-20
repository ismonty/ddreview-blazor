namespace DDReview.Core.Admin;

/// <summary>
/// Where sign-in sends the person afterwards, so a link such as /?tab=recap in an email can ask for credentials and then open that page.
/// Only paths on this site are accepted (so the sign-in cannot be used as an open redirect), and never the sign-in or account-action pages themselves.
/// </summary>
public static class SignInReturn
{
    public const string Landing = "/";

    public static string SafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 200) return Landing;
        if (path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)) return Landing;
        var p = path.ToLowerInvariant();
        if (p.StartsWith("/login", StringComparison.Ordinal) || p.StartsWith("/account/", StringComparison.Ordinal) || p.StartsWith("/signin-google", StringComparison.Ordinal)) return Landing;
        return path;
    }
}
