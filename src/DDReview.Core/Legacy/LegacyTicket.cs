using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DDReview.Core.Legacy;

/// <summary>
/// The short-lived "handoff ticket" that lets a person already signed in to the new site enter the old (classic) site without a second login.
/// Plain text on purpose, so the old VS 2008 side can parse and check it in a few lines of C# 2.0:
///   <c>v1.{webUserId}.{expiryUnixSeconds}.{nonceHex}.{signatureHex}</c>
/// where signature = lowercase hex of HMAC-SHA256(secret as UTF-8 bytes, everything before the last dot).
/// The nonce lets the old side refuse a ticket it has already accepted (single use).
/// </summary>
public static class LegacyTicket
{
    public const string Version = "v1";
    public const int MinSecretLength = 32;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(60);
    /// <summary>Clock difference tolerated between the two sites (both run on the same server, so this is small).</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    public static string Mint(string secret, int webUserId, DateTimeOffset now, TimeSpan? lifetime = null, string? nonceHex = null)
    {
        RequireSecret(secret);
        if (webUserId <= 0) throw new ArgumentOutOfRangeException(nameof(webUserId));
        nonceHex ??= Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        if (!IsLowerHex(nonceHex, 32)) throw new ArgumentException("nonce must be 32 lowercase hex characters", nameof(nonceHex));
        long expiry = now.Add(lifetime ?? DefaultLifetime).ToUnixTimeSeconds();
        string body = string.Create(CultureInfo.InvariantCulture, $"{Version}.{webUserId}.{expiry}.{nonceHex}");
        return body + "." + Sign(secret, body);
    }

    /// <summary>Checks signature and expiry. Single use (nonce) is the receiver's job: the caller gets the nonce back to remember.</summary>
    public static bool TryVerify(string secret, string? ticket, DateTimeOffset now, out int webUserId, out string nonceHex)
    {
        webUserId = 0; nonceHex = "";
        if (string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength || string.IsNullOrEmpty(ticket) || ticket.Length > 200) return false;
        var parts = ticket.Split('.');
        if (parts.Length != 5 || parts[0] != Version) return false;
        if (!IsLowerHex(parts[3], 32) || !IsLowerHex(parts[4], 64)) return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int uid) || uid <= 0) return false;
        if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long expiry)) return false;

        string body = string.Join('.', parts, 0, 4);
        byte[] expected = Encoding.ASCII.GetBytes(Sign(secret, body));
        byte[] given = Encoding.ASCII.GetBytes(parts[4]);
        if (!CryptographicOperations.FixedTimeEquals(expected, given)) return false;
        if (now.ToUnixTimeSeconds() > expiry + (long)ClockSkew.TotalSeconds) return false;

        webUserId = uid; nonceHex = parts[3];
        return true;
    }

    public static string Sign(string secret, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(h.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    static void RequireSecret(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < MinSecretLength)
            throw new ArgumentException($"The handoff secret must be at least {MinSecretLength} characters.", nameof(secret));
    }

    static bool IsLowerHex(string s, int length)
    {
        if (s.Length != length) return false;
        foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }
}
