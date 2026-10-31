using System.Security.Cryptography;

namespace DDReview.Core.Security;

/// <summary>PBKDF2-SHA256 password hashing. Format: pbkdf2-sha256$iterations$saltB64$hashB64.</summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2-sha256";
    private const int SaltBytes = 16, HashBytes = 32, DefaultIterations = 600_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>
    /// Legacy rule from the old app: passwords were compared case-insensitively (upper-cased on both sides).
    /// Kept only so existing accounts can sign in once and be upgraded to a hash.
    /// </summary>
    public static bool VerifyLegacy(string password, string? storedPlain)
    {
        if (storedPlain is null) return false;
        var a = System.Text.Encoding.UTF8.GetBytes(password.ToUpperInvariant());
        var b = System.Text.Encoding.UTF8.GetBytes(storedPlain.ToUpperInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
