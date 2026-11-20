using System.Text;

namespace DDReview.QuickBooks.Qbxml;

/// <summary>
/// The account types QuickBooks Desktop accepts in qbXML (the AccountType enumeration), and the turning of what DD Review stores into one of them.
/// DD Review keeps the type as free text per account ("Income", "Other Current Asset", "Checking or Savings" ...); a store that never set it holds "0" or nothing,
/// which QuickBooks refuses ("The enumerated value "0" ... is unknown or invalid"). <see cref="Normalize"/> only accepts real types; <see cref="Guess"/> picks one when none is set.
/// </summary>
public static class QbAccountTypes
{
    public static readonly IReadOnlyList<string> Valid =
    [
        "AccountsPayable", "AccountsReceivable", "Bank", "CostOfGoodsSold", "CreditCard", "Equity", "Expense", "FixedAsset", "Income",
        "LongTermLiability", "NonPosting", "OtherAsset", "OtherCurrentAsset", "OtherCurrentLiability", "OtherExpense", "OtherIncome",
    ];

    static readonly Dictionary<string, string> Lookup = Build();

    static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in Valid) d[Key(v)] = v;
        void Alias(string type, params string[] names) { foreach (var n in names) d[Key(n)] = type; }
        Alias("Bank", "Checking or Savings", "Checking", "Savings", "Banking");
        Alias("AccountsReceivable", "Accounts Receivable (A/R)", "A/R", "AR");
        Alias("AccountsPayable", "Accounts Payable (A/P)", "A/P", "AP");
        Alias("OtherCurrentAsset", "Other Current Assets");
        Alias("FixedAsset", "Fixed Assets");
        Alias("OtherAsset", "Other Assets", "Long Term Asset", "Long Term Assets");
        Alias("CreditCard", "Credit Cards", "Credit Card Account", "CreditCardAccount");   // the Setup page suggests "CreditCardAccount"
        Alias("OtherCurrentLiability", "Other Current Liabilities");
        Alias("LongTermLiability", "Long Term Liabilities");
        Alias("CostOfGoodsSold", "COGS", "Cost of Goods");
        Alias("Expense", "Expenses");
        Alias("OtherExpense", "Other Expenses");
        Alias("OtherIncome", "Other Income and Expense");
        return d;
    }

    /// <summary>Letters only, lower case: "Other Current Asset", "other-current-assets" and "OtherCurrentAsset" meet.</summary>
    static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>The qbXML type for what DD Review holds, or null when it is empty, "0", or not a type QuickBooks knows.</summary>
    public static string? Normalize(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : Lookup.GetValueOrDefault(Key(raw));

    /// <summary>
    /// A type for an account whose type is not set, from the name and from how the account is used in this package: a name with bank, checking or savings in it is a Bank account;
    /// an account that only ever collects debits (card receipts, clearing) is an Other Current Asset; one that is credited more than debited (sales) is Income.
    /// A guess, and the poster says so in Preview: set the QuickBooks type in DD Review to choose it yourself.
    /// </summary>
    public static string Guess(string accountName, decimal netDebit, bool usedInPackage)
    {
        var k = Key(accountName);
        if (k.Contains("bank") || k.Contains("checking") || k.Contains("savings")) return "Bank";
        if (usedInPackage && netDebit < 0) return "Income";
        return "OtherCurrentAsset";
    }
}
