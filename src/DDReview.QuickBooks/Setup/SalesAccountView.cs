using DDReview.QuickBooks.Qbxml;

namespace DDReview.QuickBooks.Setup;

public enum SalesAccountKind { Sales, TotalSales, Franchise, Suspense, Excluded, Other }

/// <summary>
/// How the Sales Accounts page presents a sales (credit) account: which kind of tile it is, which settings go together, and what needs a second look.
/// The same idea as <see cref="ReceiptTypeView"/>, for tblAccountsCredit; it works on the row's values by column name and knows nothing about the page.
/// </summary>
public static class SalesAccountView
{
    public const string Posting = "posting", Kind = "kind", Tax = "tax", Redirects = "redirects", Adjustments = "adjustments", Display = "display";

    /// <summary>The groups, in the order the side panel shows them. Every other column of the table is "Advanced".</summary>
    public static IReadOnlyList<ReceiptFieldGroup> Groups { get; } =
    [
        new(Posting, "Where it posts", "The credit account sales post to in QuickBooks.",
            ["CreditAccount", "DistReference", "AccountClassID", "DefaultQuickBooksType"]),
        new(Kind, "What kind of account", "Decides which group the tile sits in.",
            ["SalesItem", "FranchiseSale", "TotalSalesAccount", "Suspense", "Exclude"]),
        new(Tax, "Tax", "Which tax type applies to sales posted here.",
            ["TaxTypeID", "ExemptTaxTypeID", "UseTaxApplies"]),
        new(Redirects, "Redirects", "Where sales or discounts go instead, when they should not post to this account.",
            ["SalesRedirectCreditAccountID", "SalesRedirectCreditAccount", "SalesRedirectDistReference", "SalesRedirectAccountClassID",
             "DiscountRedirectCreditAccount", "DiscountRedirectDistReference", "DiscountRedirectAccountClassID"]),
        new(Adjustments, "Adjustments", "Factors and the debit and credit accounts an adjustment posts to.",
            ["AdjustmentFactor", "AdjustmentFactor2", "AdjustmentDebitAccount", "AdjustmentDebitDistReference", "AdjustmentDebitAccountClassID",
             "AdjustmentCreditAccount", "AdjustmentCreditDistReference", "AdjustmentCreditAccountClassID"]),
        new(Display, "Reports and POS", "The report title it appears under and the POS window.",
            ["AccountReportTitleID", "POSWindowID"]),
    ];

    /// <summary>The columns a "copy from another store" may carry over: accounts, references, classes, types and factors (never names, kind switches or tax types).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Copyable { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [Posting] = ["DistReference", "AccountClassID", "DefaultQuickBooksType"],
        [Redirects] = ["SalesRedirectCreditAccount", "SalesRedirectDistReference", "SalesRedirectAccountClassID",
                       "DiscountRedirectCreditAccount", "DiscountRedirectDistReference", "DiscountRedirectAccountClassID"],
        [Adjustments] = Groups.First(g => g.Id == Adjustments).Fields,
    };

    public static string Title(SalesAccountKind k) => k switch
    {
        SalesAccountKind.Sales => "Sales",
        SalesAccountKind.TotalSales => "Total sales",
        SalesAccountKind.Franchise => "Franchise sales",
        SalesAccountKind.Suspense => "Suspense",
        SalesAccountKind.Excluded => "Excluded",
        _ => "Other",
    };

    static string Text(IReadOnlyDictionary<string, object?> v, string name) => (v.GetValueOrDefault(name) as string ?? Convert.ToString(v.GetValueOrDefault(name), System.Globalization.CultureInfo.InvariantCulture) ?? "").Trim();
    static bool On(IReadOnlyDictionary<string, object?> v, string name) => v.GetValueOrDefault(name) is true;

    /// <summary>An account is "on" unless it is excluded (the page's "enabled" filter and the greyed-out tiles).</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, object?> v) => !On(v, "Exclude");

    public static SalesAccountKind KindOf(IReadOnlyDictionary<string, object?> v)
    {
        if (On(v, "Exclude")) return SalesAccountKind.Excluded;
        if (On(v, "Suspense")) return SalesAccountKind.Suspense;
        if (On(v, "TotalSalesAccount")) return SalesAccountKind.TotalSales;
        if (On(v, "FranchiseSale")) return SalesAccountKind.Franchise;
        if (On(v, "SalesItem")) return SalesAccountKind.Sales;
        return SalesAccountKind.Other;
    }

    /// <summary>What is wrong or missing on an account, in short words for a tile. Excluded and suspense accounts are not nagged about a QuickBooks type (suspense lines never get one).</summary>
    public static IReadOnlyList<string> Attention(IReadOnlyDictionary<string, object?> v)
    {
        var list = new List<string>();
        if (On(v, "Exclude")) return list;
        if (Text(v, "CreditAccount").Length == 0) list.Add("No credit account");
        if (!On(v, "Suspense"))
        {
            var type = Text(v, "DefaultQuickBooksType");
            if (type.Length == 0 || type == "0") list.Add("No QuickBooks type");
            else if (QbAccountTypes.Normalize(type) is null) list.Add($"QuickBooks type \"{type}\" is not one QuickBooks accepts");
        }
        if (Text(v, "SalesRedirectDistReference").Length > 0 && Text(v, "SalesRedirectCreditAccount").Length == 0 && Text(v, "SalesRedirectCreditAccountID").Length == 0)
            list.Add("Sales redirect has no account");
        if (Text(v, "DiscountRedirectDistReference").Length > 0 && Text(v, "DiscountRedirectCreditAccount").Length == 0)
            list.Add("Discount redirect has no account");
        return list;
    }

    /// <summary>The lines a "review changes" step shows: only fields that really differ, old then new.</summary>
    public static IReadOnlyList<(string Label, string Was, string Now)> Changes(IEnumerable<(string Label, string Was, string? Now)> fields) =>
        ReceiptTypeView.Changes(fields);

    /// <summary>Does an account match what was typed in the search box (account, reference, redirect accounts, QuickBooks type)?</summary>
    public static bool Matches(IReadOnlyDictionary<string, object?> v, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        foreach (var part in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!new[] { "CreditAccount", "DistReference", "DefaultQuickBooksType", "SalesRedirectCreditAccount", "DiscountRedirectCreditAccount", "AdjustmentDebitAccount", "AdjustmentCreditAccount" }
                    .Any(n => Text(v, n).Contains(part, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }
}
