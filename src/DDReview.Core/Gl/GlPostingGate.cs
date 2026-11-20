using System.Globalization;

namespace DDReview.Core.Gl;

/// <summary>
/// Decides when the "Post to QuickBooks" button on the General ledger page may be used, and what to ask the person first.
/// The button needs a displayed report. If that report does not balance, the person is asked and may still go ahead:
/// QuickBooks refuses unbalanced entries and the poster says why.
/// </summary>
public static class GlPostingGate
{
    /// <summary>True when a report has run and its result is on the page.</summary>
    public static bool HasData(GlBalanceReport? report, bool running) => report is not null && !running;

    /// <summary>Null when the report balances; otherwise the question to put to the person.</summary>
    public static string? BalanceQuestion(GlBalanceReport? report)
    {
        if (report is null || report.Balanced) return null;
        var bad = report.OutOfBalance.ToList();
        var ci = CultureInfo.CurrentCulture;
        string what = bad.Count > 0
            ? (bad.Count == 1 ? "1 batch does not balance" : bad.Count.ToString(ci) + " batches do not balance")
            : "the debits and credits do not match";
        return $"Some of what is shown does not balance ({what}; total difference {report.Difference.ToString("N2", ci)}).\n\n" +
               "QuickBooks will refuse the entries that do not balance, and the poster will tell you which ones and why.\n\n" +
               "Open the poster anyway?";
    }
}
