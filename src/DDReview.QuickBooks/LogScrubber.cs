using System.Text.RegularExpressions;

namespace DDReview.QuickBooks;

/// <summary>Keeps passwords out of anything the poster shows or writes to a log. Applied to every log line before it is stored.</summary>
public static class LogScrubber
{
    const string Mask = "********";
    static readonly Regex KeyValue = new(@"(?<k>\b(?:password|pwd)\s*=\s*)(?<v>""[^""]*""|'[^']*'|[^;\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Masks <c>Password=...</c> / <c>Pwd=...</c> values, and the literal password found in <paramref name="connectionString"/> wherever it turns up.</summary>
    public static string Scrub(string? text, string? connectionString = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string result = KeyValue.Replace(text, m => m.Groups["k"].Value + Mask);
        if (!string.IsNullOrEmpty(connectionString))
        {
            foreach (Match m in KeyValue.Matches(connectionString))
            {
                string v = m.Groups["v"].Value.Trim().Trim('"', '\'');
                if (v.Length >= 4) result = result.Replace(v, Mask, StringComparison.Ordinal);
            }
        }
        return result;
    }
}
