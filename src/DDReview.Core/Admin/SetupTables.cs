namespace DDReview.Core.Admin;

public enum SetupKind { Text, Int, Decimal, Bool, Date }

/// <summary>
/// One editable column of a Setup table. Only what is declared here can ever be read or written: names go into SQL from these
/// declarations (never from the browser), values always travel as parameters.
/// </summary>
/// <param name="Lookup">A name from <see cref="SetupLookups"/>: the value is the id of a row there and is shown as its text.</param>
/// <param name="AccountName">An account name typed for QuickBooks: an accented e is stored as a plain e (the old form did this so the names match).</param>
public sealed record SetupColumn(string Name, string Label, SetupKind Kind = SetupKind.Text, string? Lookup = null,
    bool AccountName = false, string[]? Suggestions = null, string? Help = null);

public sealed record SetupOrder(string Column, bool Descending = false);

/// <param name="Key">The identity column; the row id the page works with.</param>
/// <param name="StoreColumn">Set for per-store tables: rows are listed, added and changed for one chosen store.</param>
/// <param name="FilterColumn">A bit column fixed to <paramref name="FilterValue"/> for this page (tax rates and royalty/fee rates share one table).</param>
public sealed record SetupTable(string Id, SetupArea Area, string Title, string Table, string Key, IReadOnlyList<SetupColumn> Columns,
    string? StoreColumn = null, string? FilterColumn = null, bool FilterValue = false, IReadOnlyList<SetupOrder>? Order = null,
    string? Help = null, bool AllowDelete = true)
{
    public string Route => "/admin/setup/table/" + Id;
}

/// <summary>The drop-down lists the old forms offered. Each query returns an int id and its text.</summary>
public static class SetupLookups
{
    public const string AccountClasses = "AccountClasses";
    public const string TaxTypes = "TaxTypes";
    public const string RoyaltyFeeTypes = "RoyaltyFeeTypes";

    public static string? Sql(string name) => name switch
    {
        AccountClasses => "select AccountClassID, AccountClass from dbo.tblAccountsClasses order by AccountClass",
        TaxTypes => "select TaxTypeID, TaxType from dbo.tblTaxTypes where RoyaltyFee = 0 order by TaxType",
        RoyaltyFeeTypes => "select TaxTypeID, TaxType from dbo.tblTaxTypes where RoyaltyFee = 1 order by TaxType",
        _ => null,
    };
}

/// <summary>The Setup tables that are built, one declaration per old DDAdminCS form (see docs/ADMIN-SETUP-PLAN.md).</summary>
public static class SetupTables
{
    /// <summary>Account types QuickBooks accepts, as the old forms and the QuickBooks code name them. Only suggestions; the field stays free text.</summary>
    private static readonly string[] QuickBooksTypes =
    [
        "Accounts Payable", "Accounts Receivable", "Bank", "Checking or Savings", "Cost Of Goods Sold", "Credit Card", "CreditCardAccount",
        "Equity", "Expense", "Fixed Asset", "Income", "Long Term Liability", "Other Asset", "Other Current Asset", "Other Current Liability",
        "Other Expense", "Other Income",
    ];

    private const string FlagHelp = "1 posts as a debit, -1 as a credit.";

    public static SetupTable AccountClasses { get; } = new("account-classes", SetupArea.GlSetup, "Account Classes", "tblAccountsClasses", "AccountClassID",
    [
        new("AccountClass", "Class"),
        new("ExportFlag", "Export", SetupKind.Bool),
        new("InterClassSameClassAccount", "Same-class account", AccountName: true),
        new("InterClassSameClassDistReference", "Same-class reference"),
        new("InterClassSameClassDebitCreditFlag", "Same-class debit/credit", SetupKind.Int, Help: FlagHelp),
        new("InterClassSameClassDefaultQuickBooksType", "Same-class QuickBooks type", Suggestions: QuickBooksTypes),
        new("OtherAccountClassID", "Other class", SetupKind.Int, SetupLookups.AccountClasses),
        new("InterClassOtherClassAccount", "Other-class account", AccountName: true),
        new("InterClassOtherClassDistReference", "Other-class reference"),
        new("InterClassOtherClassDebitCreditFlag", "Other-class debit/credit", SetupKind.Int, Help: FlagHelp),
        new("InterClassOtherClassDefaultQuickBooksType", "Other-class QuickBooks type", Suggestions: QuickBooksTypes),
    ], Order: [new("AccountClass")],
    Help: "The classes that journal lines are split by, and the accounts that balance one class against another.");

    public static SetupTable TaxTypes { get; } = new("tax-types", SetupArea.GlSetup, "Tax/Royalty/Fee Types", "tblTaxTypes", "TaxTypeID",
    [
        new("TaxType", "Type"),
        new("RoyaltyFee", "Royalty/fee", SetupKind.Bool, Help: "Ticked types are used on the Royalty/Fee Rates page, the others on Tax Rates."),
    ], Order: [new("RoyaltyFee"), new("TaxType")],
    Help: "The names that tax rates and royalty/fee rates are filed under. A type that a rate still uses cannot be removed.");

    private static SetupColumn[] RateColumns(string typeLookup, bool royalty)
    {
        var cols = new List<SetupColumn>
        {
            new("TaxTypeID", royalty ? "Royalty/fee type" : "Tax type", SetupKind.Int, typeLookup),
            new("Enabled", "Enabled", SetupKind.Bool),
            new("TaxPayee", royalty ? "Payee" : "Tax payee"),
            new("TaxRate", royalty ? "Rate" : "Tax rate", SetupKind.Decimal),
            new("TaxAcct", royalty ? "Credit account" : "Tax account", AccountName: true),
            new("DistReference", "Reference"),
            new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
            new("Vendor", "Vendor"),
            new("DebitAccount", "Debit account", AccountName: true),
            new("DebitDistReference", "Debit reference"),
            new("DebitAccountClassID", "Debit class", SetupKind.Int, SetupLookups.AccountClasses),
        };
        if (royalty)
        {
            cols.Add(new("DebitQuickBooksType", "Debit QuickBooks type", Suggestions: QuickBooksTypes));
            cols.Add(new("CreditQuickBooksType", "Credit QuickBooks type", Suggestions: QuickBooksTypes));
            cols.Add(new("RoyaltyPeriodStartDate", "Period start", SetupKind.Date));
            cols.Add(new("RoyaltyPeriodEndDate", "Period end", SetupKind.Date));
            cols.Add(new("RoyaltyPeriodSalesExcluded", "Sales excluded", SetupKind.Decimal));
            cols.Add(new("RoyaltyPeriodSalesExpected", "Sales expected", SetupKind.Decimal));
            cols.Add(new("ProfitCenterAccountClassID", "Profit center class", SetupKind.Int, SetupLookups.AccountClasses));
        }
        else
        {
            // present only in databases that have them (the old form tried the column list with and without these)
            cols.Add(new("UseTaxApplies", "Use tax applies", SetupKind.Bool));
            cols.Add(new("UseTaxPercentage", "Use tax %", SetupKind.Decimal));
        }
        return [.. cols];
    }

    public static SetupTable TaxRates { get; } = new("tax-rates", SetupArea.GlSetup, "Tax Rates/Accts", "tblTaxRates", "ItemID",
        RateColumns(SetupLookups.TaxTypes, royalty: false), StoreColumn: "StoreID", FilterColumn: "RoyaltyFee", FilterValue: false,
        Order: [new("TaxTypeID"), new("TaxRate", Descending: true)],
        Help: "Sales tax rates for the chosen store and the accounts they post to.");

    public static SetupTable RoyaltyFeeRates { get; } = new("royalty-fee-rates", SetupArea.GlSetup, "Royalty/Fee Rates/Accts", "tblTaxRates", "ItemID",
        RateColumns(SetupLookups.RoyaltyFeeTypes, royalty: true), StoreColumn: "StoreID", FilterColumn: "RoyaltyFee", FilterValue: true,
        Order: [new("TaxTypeID"), new("Enabled")],
        Help: "Royalties and fees charged on sales for the chosen store, and the accounts they post to.");

    public static IReadOnlyList<SetupTable> All { get; } = [AccountClasses, TaxTypes, TaxRates, RoyaltyFeeRates];

    public static SetupTable? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
