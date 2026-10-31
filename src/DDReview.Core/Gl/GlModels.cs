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
