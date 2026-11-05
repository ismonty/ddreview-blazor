namespace DDReview.QuickBooks.Setup;

public enum ReceiptKind { Cards, GiftCards, Discounts, HouseCharges, Other }

/// <summary>A set of related fields shown together in the receipt type editor's side panel.</summary>
public sealed record ReceiptFieldGroup(string Id, string Title, string Hint, IReadOnlyList<string> Fields);

/// <summary>
/// How the Receipt Account Mapping page presents a receipt type: which kind of tile it is, which settings go together, and what needs a second look.
/// Works on the row's values by column name (as the Setup grid reads them), so it knows nothing about the page.
/// </summary>
public static class ReceiptTypeView
{
    public const string Posting = "posting", Kind = "kind", Discount = "discount", Receivables = "receivables", Display = "display";

    /// <summary>The groups, in the order the side panel shows them. Every other column of the table is "Advanced".</summary>
    public static IReadOnlyList<ReceiptFieldGroup> Groups { get; } =
    [
        new(Posting, "Where it posts", "The accounts this receipt type posts to in QuickBooks.",
            ["ReceiptDebitAcct", "DistReference", "AccountClassID", "ReceiptContraAcct", "DefaultQuickBooksType"]),
        new(Kind, "What kind of receipt", "Decides which group the tile sits in.",
            ["CreditCardReceipt", "GiftCard", "ReceiptPostedAsDiscount"]),
        new(Discount, "Discounts", "Only when this receipt type is posted as a discount.",
            ["DiscountDebitAcct", "DiscountDebitAccountClassID", "DiscountPercent"]),
        new(Receivables, "House charges (A/R)", "Only when receipts of this type go to a house account.",
            ["ARAccount", "ARAccountClassID", "ARCustomerName", "PostARDetailInGL"]),
        new(Display, "On the POS", "The name and whether the register offers it.",
            ["ReceiptType", "Label", "Enabled", "Visible"]),
    ];

    /// <summary>The columns of each group a "copy from another store" may carry over (accounts only: never names, ids or on/off switches).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Copyable { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [Posting] = ["ReceiptDebitAcct", "DistReference", "AccountClassID", "ReceiptContraAcct", "DefaultQuickBooksType"],
        [Discount] = ["DiscountDebitAcct", "DiscountDebitAccountClassID", "DiscountPercent"],
        [Receivables] = ["ARAccount", "ARAccountClassID", "ARCustomerName", "PostARDetailInGL"],
    };

    public static string Title(ReceiptKind k) => k switch
    {
        ReceiptKind.Cards => "Credit cards",
        ReceiptKind.GiftCards => "Gift cards",
        ReceiptKind.Discounts => "Posted as discounts",
        ReceiptKind.HouseCharges => "House charges (A/R)",
        _ => "Cash and other",
    };

    static string Text(IReadOnlyDictionary<string, object?> v, string name) => (v.GetValueOrDefault(name) as string ?? Convert.ToString(v.GetValueOrDefault(name), System.Globalization.CultureInfo.InvariantCulture) ?? "").Trim();
    static bool On(IReadOnlyDictionary<string, object?> v, string name) => v.GetValueOrDefault(name) is true;

    /// <summary>A flag column this database does not have, or that holds nothing, counts as on (a receipt type nobody switched off).</summary>
    public static bool IsEnabled(IReadOnlyDictionary<string, object?> v) => v.GetValueOrDefault("Enabled") is not false;
    public static bool IsVisible(IReadOnlyDictionary<string, object?> v) => v.GetValueOrDefault("Visible") is not false;

    public static ReceiptKind KindOf(IReadOnlyDictionary<string, object?> v)
    {
        if (On(v, "GiftCard")) return ReceiptKind.GiftCards;
        if (On(v, "ReceiptPostedAsDiscount")) return ReceiptKind.Discounts;
        if (On(v, "CreditCardReceipt")) return ReceiptKind.Cards;
        if (Text(v, "ARAccount").Length > 0 || On(v, "PostARDetailInGL")) return ReceiptKind.HouseCharges;
        return ReceiptKind.Other;
    }

    /// <summary>What is wrong or missing on a receipt type, in short words for a tile. A receipt type that is switched off is not nagged about.</summary>
    public static IReadOnlyList<string> Attention(IReadOnlyDictionary<string, object?> v)
    {
        var list = new List<string>();
        bool enabled = IsEnabled(v);
        if (enabled && Text(v, "ReceiptDebitAcct").Length == 0) list.Add("No debit account");
        if (enabled)
        {
            var type = Text(v, "DefaultQuickBooksType");
            if (type.Length == 0 || type == "0") list.Add("No QuickBooks type");
            else if (!IsQuickBooksType(type)) list.Add($"QuickBooks type \"{type}\" is not one QuickBooks accepts");
        }
        if (decimal.TryParse(Text(v, "DiscountPercent"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct != 0 && Text(v, "DiscountDebitAcct").Length == 0)
            list.Add("Discount % but no discount account");
        if (On(v, "PostARDetailInGL") && Text(v, "ARAccount").Length == 0) list.Add("A/R detail is on but there is no A/R account");
        return list;
    }

    static readonly HashSet<string> QbTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AccountsPayable","AccountsReceivable","Bank","CostOfGoodsSold","CreditCard","Equity","Expense","FixedAsset","Income",
        "LongTermLiability","NonPosting","OtherAsset","OtherCurrentAsset","OtherCurrentLiability","OtherExpense","OtherIncome",
    };

    /// <summary>The 16 names qbXML accepts, matched ignoring case, spaces and a trailing "s" ("Other Current Assets" is taken).</summary>
    public static bool IsQuickBooksType(string? type)
    {
        var t = (type ?? "").Replace(" ", "").Trim();
        return QbTypes.Contains(t) || (t.EndsWith('s') && QbTypes.Contains(t[..^1]));
    }

    /// <summary>The lines a "review changes" step shows: only fields that really differ, old then new, in the page's own words.</summary>
    public static IReadOnlyList<(string Label, string Was, string Now)> Changes(IEnumerable<(string Label, string Was, string? Now)> fields) =>
        fields.Where(f => (f.Was ?? "").Trim() != (f.Now ?? "").Trim()).Select(f => (f.Label, (f.Was ?? "").Trim(), (f.Now ?? "").Trim())).ToList();

    /// <summary>Does a receipt type match what was typed in the search box (name, accounts, reference, QuickBooks type)?</summary>
    public static bool Matches(IReadOnlyDictionary<string, object?> v, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        foreach (var part in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!new[] { "ReceiptType", "Label", "ReceiptDebitAcct", "DistReference", "ReceiptContraAcct", "DefaultQuickBooksType", "ARAccount", "DiscountDebitAcct" }
                    .Any(n => Text(v, n).Contains(part, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }
}
