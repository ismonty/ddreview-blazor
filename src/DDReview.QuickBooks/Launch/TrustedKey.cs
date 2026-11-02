using System.Security.Cryptography;
using System.Text.Json;

namespace DDReview.QuickBooks.Launch;

/// <summary>A public key the poster trusts to sign "Post to QuickBooks" links, identified by a short key id (for example <c>prod1</c>).</summary>
public sealed class TrustedKey
{
    const string P256Oid = "1.2.840.10045.3.1.7";

    public string KeyId { get; }
    public ECDsa PublicKey { get; }

    public TrustedKey(string keyId, ECDsa publicKey)
    {
        if (!PosterLinkRules.IsValidKeyId(keyId)) throw new ArgumentException("The key id must be 1 to 24 characters: a-z, 0-9 and '-'.", nameof(keyId));
        if (!IsP256(publicKey)) throw new ArgumentException("Only NIST P-256 keys are accepted.", nameof(publicKey));
        KeyId = keyId; PublicKey = publicKey;
    }

    public static bool IsP256(ECDsa key)
    {
        if (key.KeySize != 256) return false;
        var oid = key.ExportParameters(false).Curve.Oid;
        return oid.Value == P256Oid || oid.FriendlyName is "nistP256" or "ECDSA_P256";
    }

    /// <summary>The public half of a key in the form kept in the trusted-keys file: base64 of the SubjectPublicKeyInfo.</summary>
    public static string ExportPublicBase64(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    public static TrustedKey FromPublicBase64(string keyId, string base64)
    {
        var key = ECDsa.Create();
        try { key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64), out _); }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { key.Dispose(); throw new ArgumentException("The public key is not valid.", nameof(base64)); }
        try { return new TrustedKey(keyId, key); }
        catch { key.Dispose(); throw; }
    }
}

/// <summary>
/// The trusted-keys file shipped next to the poster (<c>DDQuickBooksPoster.keys.json</c>): <c>{ "keys": [ { "kid": "prod1", "publicKey": "MFkw..." } ] }</c>.
/// Public keys only; nothing in it is secret. A missing or bad entry never stops the poster, it just means links signed by that key are not trusted.
/// </summary>
public static class TrustedKeyFile
{
    public const string FileName = "DDQuickBooksPoster.keys.json";

    public static IReadOnlyList<TrustedKey> Parse(string json, out List<string> problems)
    {
        problems = [];
        var keys = new List<TrustedKey>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("keys", out var arr) || arr.ValueKind != JsonValueKind.Array)
            { problems.Add("The trusted-keys file has no \"keys\" list."); return keys; }
            int n = 0;
            foreach (var e in arr.EnumerateArray())
            {
                n++;
                string? kid = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("kid", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
                string? pub = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("publicKey", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                if (kid is null || pub is null) { problems.Add($"Key {n}: needs \"kid\" and \"publicKey\"."); continue; }
                if (keys.Any(x => x.KeyId == kid)) { problems.Add($"Key {n}: the key id \"{kid}\" appears twice; the first one is used."); continue; }
                try { keys.Add(TrustedKey.FromPublicBase64(kid, pub)); }
                catch (ArgumentException ex) { problems.Add($"Key {n} (\"{kid}\"): {ex.Message}"); }
            }
        }
        catch (JsonException) { problems.Add("The trusted-keys file is not valid JSON."); }
        return keys;
    }

    /// <summary>Reads the file; a missing file gives an empty list (links then cannot be verified, so none is accepted).</summary>
    public static IReadOnlyList<TrustedKey> Load(string path, out List<string> problems)
    {
        if (!File.Exists(path)) { problems = [$"{Path.GetFileName(path)} was not found next to the program."]; return []; }
        try { return Parse(File.ReadAllText(path), out problems); }
        catch (IOException ex) { problems = ["The trusted-keys file could not be read: " + ex.Message]; return []; }
        catch (UnauthorizedAccessException) { problems = ["The trusted-keys file could not be read (access denied)."]; return []; }
    }
}
