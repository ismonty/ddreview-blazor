using System.Globalization;

namespace DDReview.QuickBooks.Package;

/// <summary>
/// One row of the multistore export package
/// (dbo.fn_CashAndSalesGLandARMultiStoreSelectExportPackage2). PackageNumber says what the row is:
/// 1 vendors, 2 accounts, 3 classes, 4 service items, 5/7 customers, 6 customer types, 8 sales tax item,
/// 21/22 reference keys (not used here), 99 transaction lines.
/// </summary>
public sealed class PackageRow
{
    public int PackageNumber { get; init; }
    public int? BatchID { get; init; }
    public DateTime? PostingDate { get; init; }
    public string? Account { get; init; }
    public string? ARAccount { get; init; }
    public string? ARAccountType { get; init; }
    public int? AccountClassID { get; init; }
    public decimal? Debit { get; init; }
    public decimal? Credit { get; init; }
    public string? DistReference { get; init; }
    public int? Sorter { get; init; }
    public string? ItemName { get; init; }
    public long? CheckNumber { get; init; }
    public long? TransactionNumber { get; init; }
    public decimal? TaxRate { get; init; }
    public int? DebitCreditFlag { get; init; }
    public string? AccountClass { get; init; }
    public string? DefaultQuickBooksType { get; init; }
    public string? DetailInvoiceMessage { get; init; }
    public string? DuringPeriodInvoiceMessage { get; init; }
    public string? QuickBooksRefNumber { get; init; }
    public decimal? ClubMinimumSpendingExpected { get; init; }
    public decimal? ClubMinimumSpendingActual { get; init; }
    public decimal? ClubMinimumSpendingRemaining { get; init; }
    public string? ParentARAccount { get; init; }

    /// <summary>Builds a row from a column lookup (CSV header name or SQL column name). Empty/NULL means null.</summary>
    public static PackageRow FromLookup(Func<string, string?> get)
    {
        string? S(string n) { var v = get(n); return string.IsNullOrEmpty(v) || v == "NULL" ? null : v; }
        decimal? D(string n) => S(n) is { } v ? decimal.Parse(v, CultureInfo.InvariantCulture) : null;
        int? I(string n) => S(n) is { } v ? (int)decimal.Parse(v, CultureInfo.InvariantCulture) : null;
        long? L(string n) => S(n) is { } v ? (long)decimal.Parse(v, CultureInfo.InvariantCulture) : null;
        DateTime? T(string n) => S(n) is { } v ? DateTime.Parse(v, CultureInfo.InvariantCulture) : null;
        return new PackageRow
        {
            PackageNumber = I("PackageNumber") ?? 0,
            BatchID = I("BatchID"),
            PostingDate = T("PostingDate"),
            Account = S("Account"),
            ARAccount = S("ARAccount"),
            ARAccountType = S("ARAccountType"),
            AccountClassID = I("AccountClassID"),
            Debit = D("Debit"),
            Credit = D("Credit"),
            DistReference = S("DistReference"),
            Sorter = I("Sorter"),
            ItemName = S("ItemName"),
            CheckNumber = L("CheckNumber"),
            TransactionNumber = L("TransactionNumber"),
            TaxRate = D("TaxRate"),
            DebitCreditFlag = I("DebitCreditFlag"),
            AccountClass = S("AccountClass"),
            DefaultQuickBooksType = S("DefaultQuickBooksType"),
            DetailInvoiceMessage = S("DetailInvoiceMessage"),
            DuringPeriodInvoiceMessage = S("DuringPeriodInvoiceMessage"),
            QuickBooksRefNumber = S("QuickBooksRefNumber"),
            ClubMinimumSpendingExpected = D("ClubMinimumSpendingExpected"),
            ClubMinimumSpendingActual = D("ClubMinimumSpendingActual"),
            ClubMinimumSpendingRemaining = D("ClubMinimumSpendingRemaining"),
            ParentARAccount = S("ParentARAccount"),
        };
    }
}
