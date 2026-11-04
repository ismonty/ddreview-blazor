using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DDReview.QuickBooks.Settings;

namespace DDReview.QuickBooks.Win;

/// <summary>What the person typed last time. Stored per Windows user; the connection string may hold a password, so it is saved encrypted for this Windows user (DPAPI) and stays on this computer only.</summary>
public sealed class UserSettings
{
    /// <summary>The connection string as typed (in memory only). On disk it is the sealed <see cref="ConnectionStringOnDisk"/>.</summary>
    [JsonIgnore] public string ConnectionString { get; set; } = "";
    [JsonIgnore] bool _wasPlainText;

    /// <summary>What is written to the file under "ConnectionString": the encrypted text. Public only so the JSON serializer can see it.</summary>
    [JsonPropertyName("ConnectionString")]
    public string ConnectionStringOnDisk
    {
        get => ProtectedSetting.Seal(ConnectionString, Protect);
        set { _wasPlainText = value.Length > 0 && !ProtectedSetting.IsSealed(value); ConnectionString = ProtectedSetting.Open(value, Unprotect); }
    }

    static byte[] Protect(byte[] b) => ProtectedData.Protect(b, null, DataProtectionScope.CurrentUser);
    static byte[] Unprotect(byte[] b) => ProtectedData.Unprotect(b, null, DataProtectionScope.CurrentUser);
    public string DebugDirectory { get; set; } = "";
    public bool WriteDebugFiles { get; set; }
    public string QuickBooksCompanyFile { get; set; } = "";
    public string AppName { get; set; } = "DD Admin CS";
    /// <summary>The "let DD Review website links open this program?" question has been asked once; it is not asked again.</summary>
    public bool LinksPromptShown { get; set; }

    static string PathToFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DDReview", "QuickBooksPoster.json");

    public static UserSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(PathToFile)) ?? new();
            if (s._wasPlainText) s.Save();   // an older file with the password in plain text is rewritten encrypted right away
            return s;
        }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathToFile)!);
            File.WriteAllText(PathToFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not being able to remember is not worth stopping for */ }
    }
}
