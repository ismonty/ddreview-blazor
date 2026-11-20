using System.Text;

namespace DDReview.QuickBooks.Launch;

/// <summary>
/// The <c>.ddposter</c> file the website can hand out instead of a <c>ddposter:</c> link (for browsers or office policies that block custom links).
/// It is a small text file holding the very same signed link, so opening it is checked exactly like clicking a link: trusted key, signature, expiry and every limit.
/// Lines starting with <c>#</c> are comments. Anything else besides the one link line makes the file refused.
/// </summary>
public static class PosterLaunchFile
{
    public const string Extension = ".ddposter";
    public const int MaxBytes = 4096;

    /// <summary>The text of a file for <paramref name="link"/>: a plain comment, then the link.</summary>
    public static string Build(string link) =>
        "# DD Review: opens the QuickBooks poster. It is only good for a few minutes; download a new one from the website if it has expired.\r\n" + link + "\r\n";

    public static string FileNameFor(DateTime start, DateTime end) => $"PostToQuickBooks_{start:yyyyMMdd}_{end:yyyyMMdd}{Extension}";

    /// <summary>Finds the link in the text of a file. Returns false with a reason a person can act on.</summary>
    public static bool TryExtract(string? text, out string link, out string problem)
    {
        link = ""; problem = "";
        if (string.IsNullOrWhiteSpace(text)) { problem = "The file is empty."; return false; }
        string? found = null;
        foreach (var raw in text.TrimStart('﻿').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (found is not null) { problem = "The file holds more than one line of content. Download a new one from the website."; return false; }
            found = line;
        }
        if (found is null) { problem = "The file has no link in it."; return false; }
        if (!found.StartsWith(PosterLinkRules.Scheme + ":", StringComparison.OrdinalIgnoreCase)) { problem = "This is not a DD Review launch file."; return false; }
        link = found;
        return true;
    }

    /// <summary>Reads a launch file from disk, refusing anything bigger than <see cref="MaxBytes"/>.</summary>
    public static bool TryRead(string path, out string link, out string problem)
    {
        link = "";
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length > MaxBytes) { problem = "This is not a DD Review launch file (too large)."; return false; }
            using var r = new StreamReader(fs, new UTF8Encoding(false), true);
            return TryExtract(r.ReadToEnd(), out link, out problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { problem = "The file could not be read: " + ex.Message; return false; }
    }
}
