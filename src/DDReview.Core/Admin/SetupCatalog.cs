namespace DDReview.Core.Admin;

/// <summary>
/// One entry per old DDAdminCS button (see docs/ADMIN-SETUP-PLAN.md). <see cref="Route"/> is null until the page is built,
/// so the landing page can list what is coming without linking to nothing.
/// </summary>
public sealed record SetupEntry(SetupArea Area, string Section, string Title, string Table, string? Route = null);

public static class SetupCatalog
{
    public const string GlSection = "G/L Setup";
    public const string ArSection = "A/R Setup";
    public const string StoreSection = "Stores and global";
    /// <summary>The tile editor for receipt types (the grid for the same table stays at its table route as the classic view).</summary>
    public const string ReceiptTypesRoute = "/admin/setup/receipt-types";
    /// <summary>The tile editor for sales accounts (the grid for the same table stays at its table route as the classic view).</summary>
    public const string SalesAccountsRoute = "/admin/setup/sales-accounts";

    // The order follows the old button columns (top to bottom).
    public static IReadOnlyList<SetupEntry> All { get; } =
    [
        new(SetupArea.GlSetup, GlSection, "Tax/Royalty/Fee Types", "tblTaxTypes", SetupTables.TaxTypes.Route),
        new(SetupArea.GlSetup, GlSection, "Tax Rates/Accts", "tblTaxRates", SetupTables.TaxRates.Route),
        new(SetupArea.GlSetup, GlSection, "Royalty/Fee Rates/Accts", "tblTaxRates", SetupTables.RoyaltyFeeRates.Route),
        new(SetupArea.GlSetup, GlSection, "Sales Accounts", "tblAccountsCredit", SalesAccountsRoute),
        new(SetupArea.GlSetup, GlSection, "Sales Accounts (classic grid)", "tblAccountsCredit", SetupTables.SalesAccounts.Route),
        new(SetupArea.GlSetup, GlSection, "Sales Account Mapping", "tblAccountsSalesMapping", SetupTables.SalesMapping.Route),
        new(SetupArea.GlSetup, GlSection, "Receipt Account Mapping", "tblReceiptTypes", ReceiptTypesRoute),
        new(SetupArea.GlSetup, GlSection, "Receipt Account Mapping (classic grid)", "tblReceiptTypes", SetupTables.ReceiptTypes.Route),
        new(SetupArea.GlSetup, GlSection, "Receipt Adjustments", "tblReceiptTypeAdjustments", SetupTables.ReceiptAdjustments.Route),
        new(SetupArea.GlSetup, GlSection, "Paid Outs", "tblPayouts", SetupTables.Payouts.Route),
        new(SetupArea.GlSetup, GlSection, "Gift Cards", "tblGiftCardTypes", SetupTables.GiftCards.Route),
        new(SetupArea.GlSetup, GlSection, "Account Classes", "tblAccountsClasses", SetupTables.AccountClasses.Route),
        new(SetupArea.ArSetup, ArSection, "A/R Accounts", "tblAccountsReceivableAccounts", SetupTables.ArAccounts.Route),
        new(SetupArea.ArSetup, ArSection, "A/R Account Types", "tblAccountsReceivableAccountTypes", SetupTables.ArAccountTypes.Route),
        new(SetupArea.ArSetup, ArSection, "A/R Parent Accounts", "tblAccountsReceivableAccounts", SetupTables.ArParents.Route),
        new(SetupArea.ArSetup, ArSection, "Comment Substitution", "tblAccountsReceivableCommentCodeSubstitutionTable", SetupTables.CommentSubstitution.Route),
        new(SetupArea.ArSetup, ArSection, "Other A/R Descriptions", "tblMenuItems", SetupTables.ArMenuDescriptions.Route),
        new(SetupArea.ArSetup, ArSection, "Credit Card Assignment", "tblAccountsReceivableAccountsReceiptCustomerIdentifiers", SetupTables.CardAssignment.Route),
        new(SetupArea.StoreAndGlobal, StoreSection, "Store Setup (G/L fields)", "tblStores", SetupTables.Stores.Route),
        new(SetupArea.StoreAndGlobal, StoreSection, "Global Settings (G/L fields)", "tblGlobal", SetupTables.Global.Route),
    ];
}
