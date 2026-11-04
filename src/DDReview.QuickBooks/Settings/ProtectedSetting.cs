using System.Text;

namespace DDReview.QuickBooks.Settings;

/// <summary>
/// Keeps a secret (the database connection string) from sitting in a settings file as readable text. The poster passes in Windows' per-user
/// encryption (DPAPI); a file copied to another computer or Windows user cannot be opened and the person types the string again.
/// A value without the prefix is an older, plain-text file: it is still read, and written back sealed.
/// </summary>
public static class ProtectedSetting
{
    public const string Prefix = "dpapi:";

    public static bool IsSealed(string? stored) => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Seal(string? plain, Func<byte[], byte[]> protect)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        return Prefix + Convert.ToBase64String(protect(Encoding.UTF8.GetBytes(plain)));
    }

    /// <summary>The secret, or "" when it cannot be opened (another user or computer, damaged text).</summary>
    public static string Open(string? stored, Func<byte[], byte[]> unprotect)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!IsSealed(stored)) return stored;
        try { return Encoding.UTF8.GetString(unprotect(Convert.FromBase64String(stored[Prefix.Length..]))); }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException) { return ""; }
    }
}
