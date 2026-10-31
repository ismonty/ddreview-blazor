using System.Text.Json;

namespace DDReview.QuickBooks.Win;

/// <summary>What the person typed last time. Stored per Windows user; the connection string may hold a password, so it stays on this computer only.</summary>
public sealed class UserSettings
{
    public string ConnectionString { get; set; } = "";
    public string DebugDirectory { get; set; } = "";
    public bool WriteDebugFiles { get; set; }
    public string QuickBooksCompanyFile { get; set; } = "";
    public string AppName { get; set; } = "DD Admin CS";

    static string PathToFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DDReview", "QuickBooksPoster.json");

    public static UserSettings Load()
    {
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(PathToFile)) ?? new(); }
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
