using System.Buffers.Text;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Sql;

namespace DDReview.QuickBooks.Launch;

/// <summary>What a package file holds once it has been opened and every check passed.</summary>
public sealed record PackageContents(VerifiedLaunch Launch, IReadOnlyList<string> StoreNames, DateTimeOffset Created, PostingOptions Options, IReadOnlyList<PackageRow> Rows);

/// <summary>
/// The <c>.ddpackage</c> file: everything the poster needs to post a report, so the poster needs no database connection at all.
/// The website runs the same export-package query the poster used to run, adds the posting settings, and signs the lot with the key that signs links.
/// The poster checks the signature against its trusted keys before it reads anything else, then applies the same limits as for a link (dates, scope, stores) and an expiry.
///
/// Layout (gzip of UTF-8 text): one header line <c>{"format":"ddpackage-v1","kid":"prod1","sig":"&lt;base64url, 64 bytes&gt;"}</c>, a line break, then the payload JSON.
/// The signature is over <c>"ddpackage-v1\n"</c> followed by the payload bytes exactly as they are in the file.
/// </summary>
public static class PosterPackage
{
    public const string Extension = ".ddpackage";
    public const string Format = "ddpackage-v1";
    public const int Version = 1;
    public const int MaxRows = 500_000;
    public const int MaxFileBytes = 40 * 1024 * 1024;           // compressed
    public const int MaxPayloadBytes = 160 * 1024 * 1024;       // after unzipping; stops a small file that unpacks into something huge
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(48);

    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    sealed class Payload
    {
        public int V { get; set; }
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public int Scope { get; set; }
        public List<int> Stores { get; set; } = [];
        public List<string>? StoreNames { get; set; }
        public int Uid { get; set; }
        public long Created { get; set; }
        public long Exp { get; set; }
        public string Kid { get; set; } = "";
        public PostingOptions Options { get; set; } = new();
        public List<PackageRow> Rows { get; set; } = [];
    }

    sealed class Header
    {
        [JsonPropertyName("format")] public string? Format { get; set; }
        [JsonPropertyName("kid")] public string? Kid { get; set; }
        [JsonPropertyName("sig")] public string? Sig { get; set; }
    }

    public static string FileNameFor(DateTime start, DateTime end) => $"PostToQuickBooks_{start:yyyyMMdd}_{end:yyyyMMdd}{Extension}";

    static byte[] SigningBytes(byte[] payload)
    {
        var prefix = Encoding.UTF8.GetBytes(Format + "\n");
        var all = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(all, 0); payload.CopyTo(all, prefix.Length);
        return all;
    }

    /// <summary>The website's side. Refuses what the poster would refuse, so a person is told on the website and not at the poster.</summary>
    public static bool TryCreate(PosterLinkSigner signer, PostingRequest request, IReadOnlyList<string> storeNames, int webUserId, PostingOptions options,
        IReadOnlyList<PackageRow> rows, DateTimeOffset now, out byte[] file, out string error)
    {
        file = []; error = "";
        if (webUserId <= 0) { error = "There is no signed-in user."; return false; }
        var normal = new PostingRequest(request.StoreIds.Distinct().Order().ToList(), request.Start.Date, request.End.Date, request.Scope);
        var problem = PosterLinkRules.Check(normal, now);
        if (problem != null) { error = problem.Message; return false; }
        if (rows.Count == 0) { error = "There is nothing to post for those stores and dates."; return false; }
        if (rows.Count > MaxRows) { error = $"That is too much to put in one file ({rows.Count:N0} rows). Pick fewer stores or a shorter range."; return false; }

        var payload = new Payload
        {
            V = Version, Start = normal.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), End = normal.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Scope = normal.Scope, Stores = [.. normal.StoreIds], StoreNames = storeNames.Count == normal.StoreIds.Count ? [.. storeNames] : null,
            Uid = webUserId, Created = now.ToUnixTimeSeconds(), Exp = now.Add(Lifetime).ToUnixTimeSeconds(), Kid = signer.KeyId, Options = options, Rows = [.. rows],
        };
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        string sig = Base64Url.EncodeToString(signer.SignData(SigningBytes(body)));
        byte[] head = JsonSerializer.SerializeToUtf8Bytes(new Header { Format = Format, Kid = signer.KeyId, Sig = sig });

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(head); gz.WriteByte((byte)'\n'); gz.Write(body);
        }
        if (ms.Length > MaxFileBytes) { error = "That is too much to put in one file. Pick fewer stores or a shorter range."; return false; }
        file = ms.ToArray();
        return true;
    }

    static bool Fail(out PackageContents? c, out PosterLinkError e, PosterLinkProblem p, string m) { c = null; e = new(p, m); return false; }

    /// <summary>The poster's side: nothing in the file is used until the signature has checked out.</summary>
    public static bool TryOpen(byte[] file, IReadOnlyList<TrustedKey> trusted, DateTimeOffset now, out PackageContents? contents, out PosterLinkError error)
    {
        const string NotOurs = "This is not a DD Review package file.";
        if (file.Length is 0 || file.Length > MaxFileBytes) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);

        byte[] raw;
        try
        {
            using var gz = new GZipStream(new MemoryStream(file), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = gz.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, n);
                if (ms.Length > MaxPayloadBytes) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);
            }
            raw = ms.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs); }

        int nl = Array.IndexOf(raw, (byte)'\n');
        if (nl <= 0 || nl > 1024) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);
        Header? head;
        try { head = JsonSerializer.Deserialize<Header>(raw.AsSpan(0, nl)); }
        catch (JsonException) { return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs); }
        if (head is null || head.Format != Format) return Fail(out contents, out error, PosterLinkProblem.UnsupportedVersion, head?.Format is null ? NotOurs : "This package file is from a different version of DD Review. Download a new one from the website.");
        if (!PosterLinkRules.IsValidKeyId(head.Kid)) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);

        var key = trusted.FirstOrDefault(k => k.KeyId == head.Kid);
        if (key is null)
            return Fail(out contents, out error, PosterLinkProblem.UnknownKey, trusted.Count == 0
                ? $"This program has no DD Review keys installed ({TrustedKeyFile.FileName}), so it cannot check the package."
                : "This program does not trust the key that signed the package.");

        byte[] payload = raw.AsSpan(nl + 1).ToArray();
        byte[] sig;
        try { sig = head.Sig is { Length: 86 } s ? Base64Url.DecodeFromChars(s) : []; }
        catch (FormatException) { sig = []; }
        if (sig.Length != 64 || !key.PublicKey.VerifyData(SigningBytes(payload), sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            return Fail(out contents, out error, PosterLinkProblem.BadSignature, "The package's signature is not valid, so it was not used.");

        Payload? p;
        try { p = JsonSerializer.Deserialize<Payload>(payload, Json); }
        catch (JsonException) { return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs); }
        if (p is null || p.V != Version || p.Kid != head.Kid) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);
        if (p.Rows.Count > MaxRows) return Fail(out contents, out error, PosterLinkProblem.Malformed, NotOurs);
        if (!DateTime.TryParseExact(p.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(p.End, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return Fail(out contents, out error, PosterLinkProblem.BadDates, "The package's dates are not valid.");

        var request = new PostingRequest(p.Stores, start, end, p.Scope);
        var problem = PosterLinkRules.Check(request, now);
        if (problem != null) { contents = null; error = problem; return false; }
        if (p.Uid <= 0 || p.Exp <= 0 || p.Exp > 253_402_300_799L) return Fail(out contents, out error, PosterLinkProblem.Malformed, "The package's details are not valid.");

        var created = DateTimeOffset.FromUnixTimeSeconds(p.Created);
        var expires = DateTimeOffset.FromUnixTimeSeconds(p.Exp);
        if (now > expires + PosterLinkRules.ClockSkew)
            return Fail(out contents, out error, PosterLinkProblem.Expired, "This package has expired (packages last one day). Download a new one from the website.");
        if (expires > now + MaxLifetime + PosterLinkRules.ClockSkew)
            return Fail(out contents, out error, PosterLinkProblem.ClockOff, "This computer's clock and the website's clock differ too much. Check the date and time on this PC.");

        var names = p.StoreNames is { } n2 && n2.Count == p.Stores.Count ? n2 : [];
        contents = new PackageContents(new VerifiedLaunch(request, p.Uid, head.Kid!, expires), names, created, p.Options ?? new PostingOptions(), p.Rows);
        error = new(PosterLinkProblem.NotALink, "");
        return true;
    }

    /// <summary>Reads a package file from disk and opens it.</summary>
    public static bool TryOpenFile(string path, IReadOnlyList<TrustedKey> trusted, DateTimeOffset now, out PackageContents? contents, out PosterLinkError error)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return Fail(out contents, out error, PosterLinkProblem.Malformed, "This is not a DD Review package file.");
            return TryOpen(File.ReadAllBytes(path), trusted, now, out contents, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Fail(out contents, out error, PosterLinkProblem.Malformed, "The file could not be read: " + ex.Message); }
    }
}
