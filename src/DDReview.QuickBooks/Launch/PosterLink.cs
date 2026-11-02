using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DDReview.QuickBooks.Sql;

namespace DDReview.QuickBooks.Launch;

/// <summary>
/// The "Post to QuickBooks" link: <c>ddposter://run?v=1&amp;start=2026-10-01&amp;end=2026-10-31&amp;scope=3&amp;stores=4,7,12&amp;exp=1793640000&amp;kid=prod1&amp;uid=12&amp;sig=...</c>.
/// Parameters only: never a connection string, password or user name. <c>sig</c> is an ECDSA P-256 / SHA-256 signature (IEEE P1363, 64 bytes, base64url)
/// over <c>"ddposter-v1\n"</c> plus everything before <c>&amp;sig=</c>. The parameters come in one fixed order and one fixed spelling, so a link has exactly one valid form.
/// The link is untrusted input until <see cref="TryParse"/> says otherwise.
/// </summary>
public static class PosterLink
{
    static readonly string[] Keys = ["v", "start", "end", "scope", "stores", "exp", "kid", "uid"];
    const string Domain = "ddposter-v1\n";
    const string SigMarker = "&sig=";
    const int SignatureChars = 86;   // 64 bytes in base64url without padding
    const long MaxUnixSeconds = 253402300799;   // 9999-12-31

    /// <summary>The exact text that is signed, given the query text before <c>&amp;sig=</c>.</summary>
    public static string SigningText(string canonicalQuery) => Domain + canonicalQuery;

    /// <summary>The query text before <c>&amp;sig=</c>, in canonical form. The request must already be normalised (distinct ascending store ids, dates without a time).</summary>
    public static string CanonicalQuery(PostingRequest r, long exp, string kid, int uid) =>
        string.Create(CultureInfo.InvariantCulture,
            $"v={PosterLinkRules.Version}&start={r.Start:yyyy-MM-dd}&end={r.End:yyyy-MM-dd}&scope={r.Scope}&stores={string.Join(',', r.StoreIds)}&exp={exp}&kid={kid}&uid={uid}");

    static bool AllowedChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is ':' or '/' or '?' or '&' or '=' or ',' or '.' or '_' or '~' or '-';

    static bool Fail(out VerifiedLaunch? launch, out PosterLinkError? error, PosterLinkProblem p, string message)
    {
        launch = null; error = new PosterLinkError(p, message); return false;
    }

    /// <summary>
    /// Accepts a link only when every part is well formed, inside the limits, signed by a trusted key and not expired.
    /// Cheap structural checks come first; the signature is checked before the expiry so "expired" is only ever said about a genuine link.
    /// </summary>
    public static bool TryParse(string? raw, IReadOnlyList<TrustedKey> trusted, DateTimeOffset now,
        [NotNullWhen(true)] out VerifiedLaunch? launch, [NotNullWhen(false)] out PosterLinkError? error)
    {
        launch = null; error = null;
        raw = raw?.Trim();
        // Some ways of starting a program from a link add one "/" on the end; it is outside the signed text and harmless.
        if (raw is { Length: > 0 } && raw[^1] == '/' && raw.Contains("&sig=", StringComparison.Ordinal)) raw = raw[..^1];
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith(PosterLinkRules.Scheme + ":", StringComparison.OrdinalIgnoreCase))
            return Fail(out launch, out error, PosterLinkProblem.NotALink, "That is not a DD Review link.");
        if (raw.Length > PosterLinkRules.MaxLength)
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link is too long to be one of ours.");
        foreach (char c in raw)
            if (!AllowedChar(c)) return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link contains characters it should not, so it was ignored.");

        string query;
        if (raw.StartsWith(PosterLinkRules.Prefix + "?", StringComparison.OrdinalIgnoreCase)) query = raw[(PosterLinkRules.Prefix.Length + 1)..];
        else if (raw.StartsWith(PosterLinkRules.Prefix + "/?", StringComparison.OrdinalIgnoreCase)) query = raw[(PosterLinkRules.Prefix.Length + 2)..];
        else return Fail(out launch, out error, PosterLinkProblem.NotALink, "That link is not one this program opens.");

        int at = query.LastIndexOf(SigMarker, StringComparison.Ordinal);
        if (at < 0) return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link has no signature, so it was ignored.");
        string body = query[..at], sigText = query[(at + SigMarker.Length)..];

        var parts = body.Split('&');
        if (parts.Length > 0 && parts[0] != "v=1")
            return parts[0].StartsWith("v=", StringComparison.Ordinal) && parts[0].Length is >= 3 and <= 5 && parts[0][2..].All(char.IsAsciiDigit)
                ? Fail(out launch, out error, PosterLinkProblem.UnsupportedVersion, "This link comes from a newer version of DD Review. Update the poster.")
                : Fail(out launch, out error, PosterLinkProblem.Malformed, "The link is damaged or was cut off. Click the button on the website again.");
        if (parts.Length != Keys.Length)
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link is damaged or was cut off. Click the button on the website again.");
        var v = new string[Keys.Length];
        for (int i = 0; i < Keys.Length; i++)
        {
            string prefix = Keys[i] + "=";
            if (!parts[i].StartsWith(prefix, StringComparison.Ordinal))
                return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link is damaged or was cut off. Click the button on the website again.");
            v[i] = parts[i][prefix.Length..];
        }

        const NumberStyles Digits = NumberStyles.None;
        var inv = CultureInfo.InvariantCulture;
        if (!TryDate(v[1], out var start) || !TryDate(v[2], out var end))
            return Fail(out launch, out error, PosterLinkProblem.BadDates, "The link has a date this program cannot read.");
        if (!int.TryParse(v[3], Digits, inv, out int scope))
            return Fail(out launch, out error, PosterLinkProblem.BadScope, "The link's scope is not a number.");
        var idTexts = v[4].Split(',');
        if (idTexts.Length > PosterLinkRules.MaxStores)
            return Fail(out launch, out error, PosterLinkProblem.BadStores, $"A link can carry at most {PosterLinkRules.MaxStores} stores.");
        var ids = new List<int>(idTexts.Length);
        foreach (var t in idTexts)
        {
            if (t.Length is 0 or > 7 || !int.TryParse(t, Digits, inv, out int id))
                return Fail(out launch, out error, PosterLinkProblem.BadStores, "The link's store numbers are not valid.");
            ids.Add(id);
        }
        if (v[5].Length is 0 or > 12 || !long.TryParse(v[5], Digits, inv, out long exp) || exp > MaxUnixSeconds)
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link's expiry is not valid.");
        string kid = v[6];
        if (!PosterLinkRules.IsValidKeyId(kid))
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link's key id is not valid.");
        if (v[7].Length is 0 or > 10 || !int.TryParse(v[7], Digits, inv, out int uid) || uid <= 0)
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link's user number is not valid.");

        var request = new PostingRequest(ids, start, end, scope);
        var problem = PosterLinkRules.Check(request, now);
        if (problem != null) { launch = null; error = problem; return false; }
        if (CanonicalQuery(request, exp, kid, uid) != body)
            return Fail(out launch, out error, PosterLinkProblem.Malformed, "The link is not in the form this program expects.");

        var key = trusted.FirstOrDefault(k => k.KeyId == kid);
        if (key is null)
            return Fail(out launch, out error, PosterLinkProblem.UnknownKey, trusted.Count == 0
                ? $"This program has no DD Review keys installed ({TrustedKeyFile.FileName}), so it cannot check the link."
                : "This program does not trust the key that signed the link.");

        byte[] sig;
        try { sig = sigText.Length == SignatureChars ? Base64Url.DecodeFromChars(sigText) : []; }
        catch (FormatException) { sig = []; }
        bool good = sig.Length == 64 && key.PublicKey.VerifyData(Encoding.UTF8.GetBytes(SigningText(body)), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (!good) return Fail(out launch, out error, PosterLinkProblem.BadSignature, "The link's signature is not valid, so it was ignored.");

        var expires = DateTimeOffset.FromUnixTimeSeconds(exp);
        if (now > expires + PosterLinkRules.ClockSkew)
            return Fail(out launch, out error, PosterLinkProblem.Expired, "The link has expired (links last a few minutes). Click the button on the website again.");
        if (expires > now + PosterLinkRules.MaxLifetime + PosterLinkRules.ClockSkew)
            return Fail(out launch, out error, PosterLinkProblem.ClockOff, "This computer's clock and the website's clock differ too much. Check the date and time on this PC.");

        launch = new VerifiedLaunch(request, uid, kid, expires);
        return true;
    }

    static bool TryDate(string s, out DateTime d) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && s.Length == 10;
}

/// <summary>The website's side: holds the private key and makes links. The private key never leaves the site's secret settings.</summary>
public sealed class PosterLinkSigner
{
    readonly ECDsa _key;
    public string KeyId { get; }

    public PosterLinkSigner(ECDsa privateKey, string keyId)
    {
        if (!PosterLinkRules.IsValidKeyId(keyId)) throw new ArgumentException("The key id must be 1 to 24 characters: a-z, 0-9 and '-'.", nameof(keyId));
        if (!TrustedKey.IsP256(privateKey)) throw new ArgumentException("Only NIST P-256 keys are accepted.", nameof(privateKey));
        _key = privateKey; KeyId = keyId;
    }

    /// <summary>From the base64 PKCS#8 private key that the key tool writes.</summary>
    public static PosterLinkSigner FromPrivateBase64(string base64, string keyId)
    {
        var key = ECDsa.Create();
        try { key.ImportPkcs8PrivateKey(Convert.FromBase64String(base64), out _); }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { key.Dispose(); throw new ArgumentException("The signing key is not a valid base64 PKCS#8 P-256 key."); }
        try { return new PosterLinkSigner(key, keyId); }
        catch { key.Dispose(); throw; }
    }

    public string PublicKeyBase64 => TrustedKey.ExportPublicBase64(_key);

    /// <summary>Makes a link for a request, or says why it cannot (dates before day 0, too many stores, a scope the poster does not take).</summary>
    public bool TryCreate(PostingRequest request, int webUserId, DateTimeOffset now, TimeSpan lifetime, out string link, out string error)
    {
        link = ""; error = "";
        if (lifetime <= TimeSpan.Zero || lifetime > PosterLinkRules.MaxLifetime) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (webUserId <= 0) { error = "There is no signed-in user."; return false; }
        var normal = new PostingRequest(request.StoreIds.Distinct().Order().ToList(), request.Start.Date, request.End.Date, request.Scope);
        var problem = PosterLinkRules.Check(normal, now);
        if (problem != null) { error = problem.Message; return false; }

        string body = PosterLink.CanonicalQuery(normal, now.Add(lifetime).ToUnixTimeSeconds(), KeyId, webUserId);
        byte[] sig = _key.SignData(Encoding.UTF8.GetBytes(PosterLink.SigningText(body)), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        string text = PosterLinkRules.Prefix + "?" + body + "&sig=" + Base64Url.EncodeToString(sig);
        if (text.Length > PosterLinkRules.MaxLength) { error = $"A link can carry at most {PosterLinkRules.MaxStores} stores; pick fewer."; return false; }
        link = text;
        return true;
    }
}

/// <summary>What the site registers: makes links when the signing key is set, and reports "not set up" when it is not.</summary>
public sealed class PosterLinkService
{
    readonly PosterLinkSigner? _signer;
    readonly TimeSpan _lifetime;

    /// <summary>Both blank = the feature is off. Only one of them set, or a key that is not a P-256 PKCS#8 key, is a settings mistake and throws (the site then refuses to start, like the legacy proxy).</summary>
    public PosterLinkService(string? signingKeyPkcs8, string? keyId, int lifetimeMinutes = 10)
    {
        _lifetime = TimeSpan.FromMinutes(Math.Clamp(lifetimeMinutes, 1, (int)PosterLinkRules.MaxLifetime.TotalMinutes));
        bool hasKey = !string.IsNullOrWhiteSpace(signingKeyPkcs8), hasId = !string.IsNullOrWhiteSpace(keyId);
        if (!hasKey && !hasId) return;
        if (!hasKey || !hasId) throw new InvalidOperationException("PosterLink needs both SigningKeyPkcs8 and KeyId (or neither, to leave the Post to QuickBooks button off).");
        try { _signer = PosterLinkSigner.FromPrivateBase64(signingKeyPkcs8!.Trim(), keyId!.Trim()); }
        catch (ArgumentException ex) { throw new InvalidOperationException("PosterLink: " + ex.Message); }
    }

    public bool IsConfigured => _signer != null;
    public string? KeyId => _signer?.KeyId;

    public bool TryCreate(PostingRequest request, int webUserId, DateTimeOffset now, out string link, out string error)
    {
        if (_signer is null) { link = ""; error = "Posting to QuickBooks from the website is not set up."; return false; }
        return _signer.TryCreate(request, webUserId, now, _lifetime, out link, out error);
    }
}
