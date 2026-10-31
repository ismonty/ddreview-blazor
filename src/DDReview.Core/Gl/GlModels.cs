namespace DDReview.Core.Gl;

/// <summary>One row of dbo.fn_CashAndSalesGLandARMultiStoreSelect (detail view). Debit/Credit are already netted per line by the function.</summary>
public sealed record GlLine(
    int BatchId, DateTime PostingDate, string? Account, string? ARAccount, int? ARAccountId, string? ARAccountType,
    decimal Debit, decimal Credit, string? DistReference, int? Sorter, bool Suspense,
    int? CheckNumber, int? TransactionNumber, short? DebitCreditFlag, int? StoreId, bool ExportFlag,
    string? AccountClass, bool AROffsetLine, int? StoreNumber, string? StoreName, string? DefaultQuickBooksType,
    string? AccountNumber, decimal? FunctionTotalDebit, decimal? FunctionTotalCredit);

/// <summary>One row of dbo.fn_DDAdminCSWebGLAR (the summarized view the old web page used).</summary>
public sealed record GlSummaryLine(
    int? StoreNumber, int TotalLine, string? StoreName, int? BatchId, string? PostingDateRange, string? Account, string? ARAccount,
    decimal Debit, decimal Credit, string? DistReference, string? AccountClass, int? StoreId,
    int? CheckNumber, int? TransactionNumber, int? Sorter, string? DetailInvoiceMessage);

public sealed record BatchBalance(int BatchId, DateTime PostingDate, decimal Debit, decimal Credit, int Lines, bool HasSuspense)
{
    public decimal Difference => Debit - Credit;
    public bool Balanced => Difference == 0m;
}

public sealed record GlBalanceReport(decimal TotalDebit, decimal TotalCredit, IReadOnlyList<BatchBalance> Batches)
{
    public decimal Difference => TotalDebit - TotalCredit;
    public bool Balanced => Difference == 0m && Batches.All(b => b.Balanced);
    public IEnumerable<BatchBalance> OutOfBalance => Batches.Where(b => !b.Balanced);
}

public static class GlBalance
{
    /// <summary>Totals computed from the lines themselves, not the function's own TotalDebit/TotalCredit columns, so the check is independent.</summary>
    public static GlBalanceReport Compute(IEnumerable<GlLine> lines)
    {
        var batches = lines
            .GroupBy(l => (l.BatchId, l.PostingDate))
            .Select(g => new BatchBalance(g.Key.BatchId, g.Key.PostingDate, g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Count(), g.Any(l => l.Suspense)))
            .OrderBy(b => b.PostingDate).ThenBy(b => b.BatchId)
            .ToList();
        return new GlBalanceReport(batches.Sum(b => b.Debit), batches.Sum(b => b.Credit), batches);
    }
}

/// <summary>One line of the "Totals" mode: everything with the same account, A/R account and reference, over the whole date range.</summary>
public sealed record GlTotalLine(string Account, string ARAccount, string Reference, decimal Debit, decimal Credit, bool Suspense);

public static class GlTotals
{
    /// <summary>
    /// Adds the lines up ignoring date and batch. Lines that differ in account, A/R account or reference stay on separate lines
    /// (names compared ignoring case and surrounding spaces); everything else is tallied together. Lines with no amounts are dropped.
    /// </summary>
    public static IReadOnlyList<GlTotalLine> Group(IEnumerable<GlLine> lines) =>
        lines.Where(l => l.Debit != 0 || l.Credit != 0)
            .GroupBy(l => (Norm(l.Account), Norm(l.ARAccount), Norm(l.DistReference)))
            .Select(g => (Min: g.Min(l => l.Sorter ?? int.MaxValue), Line: new GlTotalLine(
                g.First().Account?.Trim() ?? "", g.First().ARAccount?.Trim() ?? "", g.First().DistReference?.Trim() ?? "",
                g.Sum(l => l.Debit), g.Sum(l => l.Credit), g.Any(l => l.Suspense))))
            .OrderBy(x => x.Min).ThenBy(x => x.Line.Account, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Line.ARAccount, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Line.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Line).ToList();

    private static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();
}
