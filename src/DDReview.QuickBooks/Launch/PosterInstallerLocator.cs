namespace DDReview.QuickBooks.Launch;

/// <summary>A poster installer found in the folder the website serves it from.</summary>
public sealed record PosterInstallerFile(string Path, string Name, long Bytes, DateTime Built);

/// <summary>
/// Finds the poster installer (the MSI made by deploy/Publish-Poster.ps1) in the folder named by the site setting <c>PosterInstaller:Folder</c>.
/// Only <c>DDQuickBooksPoster-*.msi</c> files directly in that folder count; the newest one wins. The file name comes from the folder, never from the request.
/// </summary>
public static class PosterInstallerLocator
{
    public const string Pattern = "DDQuickBooksPoster-*.msi";

    public static PosterInstallerFile? FindNewest(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            if (!Directory.Exists(folder)) return null;
            return new DirectoryInfo(folder).EnumerateFiles(Pattern, SearchOption.TopDirectoryOnly)
                .Where(f => f.Length > 0 && (f.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
                .Select(f => new PosterInstallerFile(f.FullName, f.Name, f.Length, f.LastWriteTime))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}
