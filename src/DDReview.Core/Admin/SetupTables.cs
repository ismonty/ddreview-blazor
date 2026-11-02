namespace DDReview.Core.Admin;

public enum SetupKind { Text, Int, Decimal, Bool, Date }

/// <summary>
/// One editable column of a Setup table. Only what is declared here can ever be read or written: names go into SQL from these
/// declarations (never from the browser), values always travel as parameters.
/// </summary>
/// <param name="Lookup">A name from <see cref="SetupLookups"/>: the value is the id of a row there and is shown as its text.</param>
/// <param name="BlankText">What an empty value means, shown instead of blank (the mapping columns: "any").</param>
/// <param name="LookupParent">Another column of the same row that narrows this list (sale types follow the main type): only items whose parent matches are offered.</param>
public sealed record SetupColumn(string Name, string Label, SetupKind Kind = SetupKind.Text, string? Lookup = null,
    string[]? Suggestions = null, string? Help = null, string? LookupParent = null, string? BlankText = null);

public sealed record SetupOrder(string Column, bool Descending = false);

/// <summary>Of these yes/no columns the leader excludes the others: turning the leader on turns them off, turning one of them on turns the leader off (Include all vs. the specific kinds).</summary>
public sealed record SetupExclusive(string Leader, string[] Others);

/// <summary>What the old forms did on a double-click: fill an empty field from other fields of the row. {Name} is replaced by that column's value.</summary>
public sealed record SetupFill(string Column, string Template);

/// <param name="Key">The identity column; the row id the page works with.</param>
/// <param name="StoreColumn">Set for per-store tables: rows are listed, added and changed for one chosen store.</param>
/// <param name="FilterColumn">A bit column fixed to <paramref name="FilterValue"/> for this page (tax rates and royalty/fee rates share one table).</param>
public sealed record SetupTable(string Id, SetupArea Area, string Title, string Table, string Key, IReadOnlyList<SetupColumn> Columns,
    string? StoreColumn = null, string? FilterColumn = null, bool FilterValue = false, IReadOnlyList<SetupOrder>? Order = null,
    string? Help = null, bool AllowDelete = true, SetupExclusive? Exclusive = null, IReadOnlyList<SetupFill>? Fills = null)
{
    public string Route => "/admin/setup/table/" + Id;
}

/// <summary>The drop-down lists the old forms offered. Each query returns an int id and its text.</summary>
public static class SetupLookups
{
    public const string AccountClasses = "AccountClasses";
    public const string TaxTypes = "TaxTypes";
    public const string RoyaltyFeeTypes = "RoyaltyFeeTypes";
    // per-store lists (they use @store, the store chosen on the page)
    public const string MainTypes = "MainTypes";
    public const string ProfitCenters = "ProfitCenters";
    public const string SaleTypes = "SaleTypes";
    public const string MenuItems = "MenuItems";
    public const string Discounts = "Discounts";
    public const string PosWindows = "PosWindows";
    public const string CreditAccounts = "CreditAccounts";
    public const string ReportTitles = "ReportTitles";
    public const string ReceiptTypes = "ReceiptTypes";

    /// <summary>The query for a list: id, text and (optionally) a third int column, the parent a narrowing column filters by.</summary>
    public static string? Sql(string name) => name switch
    {
        AccountClasses => "select AccountClassID, AccountClass from dbo.tblAccountsClasses order by AccountClass",
        TaxTypes => "select TaxTypeID, TaxType from dbo.tblTaxTypes where RoyaltyFee = 0 order by TaxType",
        RoyaltyFeeTypes => "select TaxTypeID, TaxType from dbo.tblTaxTypes where RoyaltyFee = 1 order by TaxType",
        MainTypes => "select MainTypeID, Description from dbo.tblMainTypes where MainTypeID is not null order by DisplayOrder, Description",
        ProfitCenters => "select ProfitCenterID, ProfitCenter from dbo.tblProfitCenters where StoreID = @store and ProfitCenterID is not null order by ProfitCenter",
        SaleTypes => "select st.SaleTypeID, st.SaleType, mg.MainTypeID from dbo.tblSaleTypes st "
            + "left join dbo.tblMainGroups mg on mg.StoreID = st.StoreID and mg.MainGroupID = st.MainGroupID "
            + "where st.StoreID = @store and st.SaleTypeID is not null order by st.SaleType",
        // the menu item list the old form used (it folds in the charge items), one line per PLU
        MenuItems => "select PLU, isnull(max(Description), '') + ' (' + convert(varchar(12), PLU) + ')' from dbo.fn_MenuItemList(@store, null, null) group by PLU order by 2",
        // negative ids are receipt types posted as discounts, -1 is every discount: the same encoding the old form wrote
        Discounts => "select DiscountID, Discount from ("
            + "select -1 as Sorter, -1 as DiscountID, '<All Discounts>' as Discount "
            + "union all select 0, DiscountID, Discount from dbo.tblDiscounts where StoreID = @store and DiscountID is not null "
            + "union all select 1, -ReceiptTypeID, 'Receipt: ' + ReceiptType from dbo.tblReceiptTypes where StoreID = @store and ReceiptPostedAsDiscount = 1"
            + ") d order by Sorter, Discount",
        PosWindows => "select POSWindowID, POSWindowName from dbo.tblPOSWindows where StoreID = @store order by POSWindowName",
        CreditAccounts => "select ca.CreditAccountID, rtrim(isnull(ca.DistReference, '')) + ' (' + rtrim(isnull(ca.CreditAccount, '')) + isnull(' - ' + rtrim(tt.TaxType), '') + isnull(' - ' + rtrim(ac.AccountClass), '') + ')' "
            + "from dbo.tblAccountsCredit ca left join dbo.tblTaxTypes tt on tt.TaxTypeID = ca.TaxTypeID left join dbo.tblAccountsClasses ac on ac.AccountClassID = ca.AccountClassID "
            + "where ca.StoreID = @store order by 2",
        ReceiptTypes => "select ReceiptTypeID, ReceiptType from dbo.tblReceiptTypes where StoreID = @store and ReceiptTypeID between 1 and 999 order by ReceiptType",
        ReportTitles => "select AccountReportTitleID, ReportTitle from dbo.tblAccountsReportTitles order by ReportTitle",
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
        new("InterClassSameClassAccount", "Same-class account"),
        new("InterClassSameClassDistReference", "Same-class reference"),
        new("InterClassSameClassDebitCreditFlag", "Same-class debit/credit", SetupKind.Int, Help: FlagHelp),
        new("InterClassSameClassDefaultQuickBooksType", "Same-class QuickBooks type", Suggestions: QuickBooksTypes),
        new("OtherAccountClassID", "Other class", SetupKind.Int, SetupLookups.AccountClasses),
        new("InterClassOtherClassAccount", "Other-class account"),
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
            new("TaxAcct", royalty ? "Credit account" : "Tax account"),
            new("DistReference", "Reference"),
            new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
            new("Vendor", "Vendor"),
            new("DebitAccount", "Debit account"),
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


    private const string TriHelp = "Leave blank for any (the row then matches whether or not it is set).";

    public static SetupTable SalesAccounts { get; } = new("sales-accounts", SetupArea.GlSetup, "Sales Accounts", "tblAccountsCredit", "CreditAccountID",
    [
        new("DistReference", "Reference"),
        new("CreditAccount", "Credit account"),
        new("TaxTypeID", "Tax type", SetupKind.Int, SetupLookups.TaxTypes),
        new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
        new("SalesItem", "Sales item", SetupKind.Bool),
        new("FranchiseSale", "Franchise sale", SetupKind.Bool),
        new("Suspense", "Suspense", SetupKind.Bool),
        new("TotalSalesAccount", "Total sales account", SetupKind.Bool),
        new("Exclude", "Exclude", SetupKind.Bool),
        new("UseTaxApplies", "Use tax applies", SetupKind.Bool),
        new("ExemptTaxTypeID", "Exempt tax type", SetupKind.Int, SetupLookups.TaxTypes),
        new("DefaultQuickBooksType", "QuickBooks type", Suggestions: QuickBooksTypes),
        new("AccountReportTitleID", "Report title", SetupKind.Int, SetupLookups.ReportTitles),
        new("SalesRedirectCreditAccountID", "Sales redirect to", SetupKind.Int, SetupLookups.CreditAccounts),
        new("SalesRedirectCreditAccount", "Sales redirect account"),
        new("SalesRedirectDistReference", "Sales redirect reference"),
        new("SalesRedirectAccountClassID", "Sales redirect class", SetupKind.Int, SetupLookups.AccountClasses),
        new("DiscountRedirectCreditAccount", "Discount redirect account"),
        new("DiscountRedirectDistReference", "Discount redirect reference"),
        new("DiscountRedirectAccountClassID", "Discount redirect class", SetupKind.Int, SetupLookups.AccountClasses),
        new("AdjustmentFactor", "Adjustment factor", SetupKind.Decimal),
        new("AdjustmentFactor2", "Adjustment factor 2", SetupKind.Decimal),
        new("AdjustmentDebitAccount", "Adjustment debit account"),
        new("AdjustmentDebitDistReference", "Adjustment debit reference"),
        new("AdjustmentDebitAccountClassID", "Adjustment debit class", SetupKind.Int, SetupLookups.AccountClasses),
        new("AdjustmentCreditAccount", "Adjustment credit account"),
        new("AdjustmentCreditDistReference", "Adjustment credit reference"),
        new("AdjustmentCreditAccountClassID", "Adjustment credit class", SetupKind.Int, SetupLookups.AccountClasses),
        new("POSWindowID", "POS window", SetupKind.Int, SetupLookups.PosWindows),
        new("MappingSetID", "Mapping set", SetupKind.Int),
        new("Delete", "Delete", SetupKind.Bool),
        new("NewAccount", "New account"),
    ], StoreColumn: "StoreID", Order: [new("DistReference"), new("CreditAccount")],
    Help: "The G/L credit accounts that sales post to for the chosen store, and where discounts, redirected sales and adjustments go.");

    public static SetupTable SalesMapping { get; } = new("sales-mapping", SetupArea.GlSetup, "Sales Account Mapping", "tblAccountsSalesMapping", "ItemID",
    [
        new("Priority", "Priority", SetupKind.Int, Help: "A sale is checked against the rows in this order and takes the first row that matches (lowest number first)."),
        new("MainTypeID", "Main type", SetupKind.Int, SetupLookups.MainTypes, BlankText: "any"),
        new("ProfitCenterID", "Profit center", SetupKind.Int, SetupLookups.ProfitCenters, BlankText: "any"),
        new("SaleTypeID", "Sale type", SetupKind.Int, SetupLookups.SaleTypes, BlankText: "any", LookupParent: "MainTypeID", Help: "Only sale types of the chosen main type are offered."),
        new("Special", "Special", SetupKind.Bool, Help: TriHelp, BlankText: "any"),
        new("PLU", "Menu item", SetupKind.Int, SetupLookups.MenuItems, BlankText: "any"),
        new("Discount", "Discount", SetupKind.Int, SetupLookups.Discounts, BlankText: "any"),
        new("POSWindowID", "POS window", SetupKind.Int, SetupLookups.PosWindows, BlankText: "any"),
        new("TaxExempt", "Tax exempt", SetupKind.Bool, Help: TriHelp, BlankText: "any"),
        new("CreditAccountID", "Credit account", SetupKind.Int, SetupLookups.CreditAccounts),
        new("CompositeSalesTaxRate", "Composite tax rate", SetupKind.Decimal),
    ], StoreColumn: "StoreID",
    Order: [new("Priority"), new("MainTypeID"), new("ProfitCenterID"), new("SaleTypeID"), new("Special", Descending: true)],
    Help: "Which G/L credit account a sale posts to, by what was sold. A blank column matches everything. Each sale takes the first row, in priority order, whose filled-in columns all match it, so a row above another with more blanks hides it, and a wrong row changes the next export.");

    private static SetupColumn[] AdjustmentColumns(string prefix) =>
    [
        new(prefix + "Account", "Adjustment " + (prefix == "AdjustmentDebit" ? "debit" : "credit") + " account"),
        new(prefix + "DistReference", "Adjustment " + (prefix == "AdjustmentDebit" ? "debit" : "credit") + " reference"),
        new(prefix + "AccountClassID", "Adjustment " + (prefix == "AdjustmentDebit" ? "debit" : "credit") + " class", SetupKind.Int, SetupLookups.AccountClasses),
    ];

    public static SetupTable ReceiptTypes { get; } = new("receipt-types", SetupArea.GlSetup, "Receipt Account Mapping", "tblReceiptTypes", "ItemID",
    [
        new("ReceiptTypeID", "Receipt type id", SetupKind.Int, Help: "The POS's own number for this receipt type."),
        new("ReceiptType", "Receipt type"),
        new("Enabled", "Enabled", SetupKind.Bool),
        new("Visible", "Visible", SetupKind.Bool),
        new("ReceiptDebitAcct", "Debit account"),
        new("DistReference", "Reference"),
        new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
        new("ReceiptContraAcct", "Contra account"),
        new("DiscountDebitAcct", "Discount debit account"),
        new("DiscountDebitAccountClassID", "Discount debit class", SetupKind.Int, SetupLookups.AccountClasses),
        new("DiscountPercent", "Discount %", SetupKind.Decimal),
        new("ARAccount", "A/R account"),
        new("ARAccountClassID", "A/R class", SetupKind.Int, SetupLookups.AccountClasses),
        new("ARCustomerName", "A/R customer name"),
        new("PostARDetailInGL", "Post A/R detail in G/L", SetupKind.Bool),
        new("DefaultQuickBooksType", "QuickBooks type", Suggestions: QuickBooksTypes),
        new("Label", "Label"),
        new("Sorter", "Sort", SetupKind.Int),
        new("ReceiptPostedAsDiscount", "Posted as discount", SetupKind.Bool),
        new("CreditCardReceipt", "Credit card", SetupKind.Bool),
        new("GiftCard", "Gift card", SetupKind.Bool),
        new("Suspense", "Suspense", SetupKind.Bool),
        new("CreditBalanceAccount", "Credit balance account", SetupKind.Bool),
        new("Blind", "Blind", SetupKind.Bool),
        new("EndOfShiftDayInitialCountedBlank", "Initial count blank", SetupKind.Bool),
        new("ReceiptQuery", "Receipt query", SetupKind.Int),
        new("EODLine", "End-of-day line", SetupKind.Int),
        new("EODLineLevel", "End-of-day level", SetupKind.Int),
        new("CurrencyCalculator", "Currency calculator", SetupKind.Int),
        new("ReceiptClass", "Receipt class", SetupKind.Int),
        new("ServerDropMode", "Server drop mode", SetupKind.Int),
        new("ReportTitle", "Report title"),
        new("ReportGroup", "Report group", SetupKind.Int),
        new("ReportGroupTitle", "Report group title"),
        new("TotalBits", "Total bits", SetupKind.Int),
        new("ReceiptCheckbookID", "Checkbook id"),
        new("ReceiptTypeVISA", "VISA", SetupKind.Int),
        new("ReceiptTypeMC", "MC", SetupKind.Int),
        new("ReceiptTypeDiscover", "Discover", SetupKind.Int),
        new("ReceiptTypeAmEx", "AmEx", SetupKind.Int),
        new("ReceiptTypeEMV", "EMV", SetupKind.Int),
        new("ReceiptTypeOther", "Other card", SetupKind.Int),
    ], StoreColumn: "StoreID", Order: [new("ReceiptTypeID")], AllowDelete: false,
    Fills: [new("ReceiptDebitAcct", "Receipts: {ReceiptType}"), new("DistReference", "{ReceiptType} Receipts")],
    Help: "The accounts each POS receipt type posts to. \"Fill empty accounts\" proposes a debit account and reference from the receipt type name. Receipt types come from the POS, so they cannot be removed here.");

    public static SetupTable ReceiptAdjustments { get; } = new("receipt-adjustments", SetupArea.GlSetup, "Receipt Adjustments", "tblReceiptTypeAdjustments", "ItemID",
    [
        new("ReceiptTypeID", "Receipt type", SetupKind.Int, SetupLookups.ReceiptTypes),
        new("AdjustmentFactor1", "Factor 1", SetupKind.Decimal),
        new("AdjustmentFactor2", "Factor 2", SetupKind.Decimal),
        .. AdjustmentColumns("AdjustmentDebit"),
        .. AdjustmentColumns("AdjustmentCredit"),
        new("IncludeAll", "All", SetupKind.Bool, Help: "Turning this on turns the specific kinds off, and turning one of them on turns this off."),
        new("IncludeFood", "Food", SetupKind.Bool),
        new("IncludeBeverage", "Beverage", SetupKind.Bool),
        new("IncludeOther", "Other", SetupKind.Bool),
        new("IncludeCharge", "Charge", SetupKind.Bool),
        new("IncludeDiscounts", "Discounts", SetupKind.Bool),
        new("IncludeTaxes", "Taxes", SetupKind.Bool),
        new("IncludeTips", "Tips", SetupKind.Bool),
        new("IncludeAllOther", "All other", SetupKind.Bool),
    ], StoreColumn: "StoreID", Order: [new("ReceiptTypeID")],
    Exclusive: new("IncludeAll", ["IncludeFood", "IncludeBeverage", "IncludeOther", "IncludeCharge", "IncludeDiscounts", "IncludeTaxes", "IncludeTips", "IncludeAllOther"]),
    Help: "An extra posting for a receipt type: a factor of the amounts, debited and credited to the accounts given. \"All\" or specific kinds of sales, not both.");

    public static SetupTable Payouts { get; } = new("paid-outs", SetupArea.GlSetup, "Paid Outs", "tblPayouts", "ItemID",
    [
        new("PayoutID", "Paid out id", SetupKind.Int),
        new("Payout", "Paid out"),
        new("PayoutSort", "Sort"),
        new("PayoutDebitAcct", "Debit account"),
        new("DistReference", "Reference"),
        new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
        new("TipsShortfall", "Tips shortfall", SetupKind.Bool),
        new("DefaultQuickBooksType", "QuickBooks type", Suggestions: QuickBooksTypes),
        new("AdjustmentFactor", "Adjustment factor", SetupKind.Decimal),
        .. AdjustmentColumns("AdjustmentDebit"),
        .. AdjustmentColumns("AdjustmentCredit"),
    ], StoreColumn: "StoreID", Order: [new("PayoutSort")],
    Fills: [new("PayoutDebitAcct", "{Payout}"), new("DistReference", "Paid Out - {Payout}")],
    Help: "The accounts each kind of paid out posts to for the chosen store. Tips shortfall only shows in databases that have the column.");

    public static SetupTable GiftCards { get; } = new("gift-cards", SetupArea.GlSetup, "Gift Cards", "tblGiftCardTypes", "ItemID",
    [
        new("GiftCardTypeID", "Gift card type id", SetupKind.Int),
        new("GiftCardType", "Gift card type"),
        new("GiftCardTypeSort", "Sort"),
        new("GiftCardCreditAcct", "Credit account"),
        new("GiftCardDistReference", "Reference"),
        new("AccountClassID", "Class", SetupKind.Int, SetupLookups.AccountClasses),
    ], StoreColumn: "StoreID", Order: [new("GiftCardTypeSort")],
    Fills: [new("GiftCardDistReference", "{GiftCardType}")],
    Help: "The credit account each gift card type posts to for the chosen store.");

    public static IReadOnlyList<SetupTable> All { get; } = [AccountClasses, TaxTypes, TaxRates, RoyaltyFeeRates, SalesAccounts, SalesMapping, ReceiptTypes, ReceiptAdjustments, Payouts, GiftCards];

    public static SetupTable? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
}
