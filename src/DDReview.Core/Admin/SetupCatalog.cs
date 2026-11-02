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

    // The order follows the old button columns (top to bottom).
    public static IReadOnlyList<SetupEntry> All { get; } =
    [
        new(SetupArea.GlSetup, GlSection, "Tax/Royalty/Fee Types", "tblTaxTypes"),
        new(SetupArea.GlSetup, GlSection, "Tax Rates/Accts", "tblTaxRates"),
        new(SetupArea.GlSetup, GlSection, "Royalty/Fee Rates/Accts", "tblTaxRates"),
        new(SetupArea.GlSetup, GlSection, "Sales Accounts", "tblAccountsCredit"),
        new(SetupArea.GlSetup, GlSection, "Sales Account Mapping", "tblAccountsSalesMapping"),
        new(SetupArea.GlSetup, GlSection, "Receipt Account Mapping", "tblReceiptTypes"),
        new(SetupArea.GlSetup, GlSection, "Receipt Adjustments", "tblReceiptTypeAdjustments"),
        new(SetupArea.GlSetup, GlSection, "Paid Outs", "tblPayouts"),
        new(SetupArea.GlSetup, GlSection, "Gift Cards", "tblGiftCardTypes"),
        new(SetupArea.GlSetup, GlSection, "Account Classes", "tblAccountsClasses"),
        new(SetupArea.ArSetup, ArSection, "A/R Accounts", "tblAccountsReceivableAccounts"),
        new(SetupArea.ArSetup, ArSection, "A/R Account Types", "tblAccountsReceivableAccountTypes"),
        new(SetupArea.ArSetup, ArSection, "A/R Parent Accounts", "tblAccountsReceivableAccounts"),
        new(SetupArea.ArSetup, ArSection, "Comment Substitution", "tblAccountsReceivableCommentCodeSubstitutionTable"),
        new(SetupArea.ArSetup, ArSection, "Other A/R Descriptions", "tblMenuItems"),
        new(SetupArea.ArSetup, ArSection, "Credit Card Assignment", "tblAccountsReceivableAccountsReceiptCustomerIdentifiers"),
        new(SetupArea.StoreAndGlobal, StoreSection, "Store Setup (G/L fields)", "tblStores"),
        new(SetupArea.StoreAndGlobal, StoreSection, "Global Settings (G/L fields)", "tblGlobal"),
    ];
}
