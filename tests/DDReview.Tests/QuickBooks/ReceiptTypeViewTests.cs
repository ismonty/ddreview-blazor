using DDReview.QuickBooks.Setup;

namespace DDReview.Tests.QuickBooks;

public class ReceiptTypeViewTests
{
    static Dictionary<string, object?> Row(params (string Name, object? Value)[] v) => v.ToDictionary(x => x.Name, x => x.Value);

    [Fact] public void Kind_follows_the_flags_in_a_fixed_order()
    {
        Assert.Equal(ReceiptKind.GiftCards, ReceiptTypeView.KindOf(Row(("GiftCard", true), ("CreditCardReceipt", true))));
        Assert.Equal(ReceiptKind.Discounts, ReceiptTypeView.KindOf(Row(("ReceiptPostedAsDiscount", true), ("CreditCardReceipt", true))));
        Assert.Equal(ReceiptKind.Cards, ReceiptTypeView.KindOf(Row(("CreditCardReceipt", true))));
        Assert.Equal(ReceiptKind.HouseCharges, ReceiptTypeView.KindOf(Row(("ARAccount", "House"))));
        Assert.Equal(ReceiptKind.HouseCharges, ReceiptTypeView.KindOf(Row(("PostARDetailInGL", true))));
        Assert.Equal(ReceiptKind.Other, ReceiptTypeView.KindOf(Row(("ReceiptType", "Cash"))));
        Assert.Equal(ReceiptKind.Other, ReceiptTypeView.KindOf(Row()));
    }

    [Fact] public void A_complete_receipt_type_needs_no_attention() =>
        Assert.Empty(ReceiptTypeView.Attention(Row(("Enabled", true), ("ReceiptDebitAcct", "1100"), ("DefaultQuickBooksType", "Bank"))));

    [Fact] public void Missing_debit_account_and_the_zero_type_are_flagged()
    {
        var a = ReceiptTypeView.Attention(Row(("Enabled", true), ("ReceiptDebitAcct", " "), ("DefaultQuickBooksType", "0")));
        Assert.Contains("No debit account", a);
        Assert.Contains("No QuickBooks type", a);
    }

    [Fact] public void An_unknown_type_name_is_flagged_but_a_spelling_QuickBooks_takes_is_not()
    {
        Assert.Contains(ReceiptTypeView.Attention(Row(("ReceiptDebitAcct", "1"), ("DefaultQuickBooksType", "Banana"))), s => s.Contains("Banana"));
        Assert.Empty(ReceiptTypeView.Attention(Row(("ReceiptDebitAcct", "1"), ("DefaultQuickBooksType", "Other Current Assets"))));
    }

    [Fact] public void A_receipt_type_switched_off_is_not_nagged_about_accounts()
    {
        Assert.Empty(ReceiptTypeView.Attention(Row(("Enabled", false))));
        Assert.NotEmpty(ReceiptTypeView.Attention(Row(("Enabled", null))));    // nothing set means on
    }

    [Fact] public void Discount_and_receivable_rules()
    {
        Assert.Contains("Discount % but no discount account", ReceiptTypeView.Attention(Row(("Enabled", false), ("DiscountPercent", 10m))));
        Assert.Empty(ReceiptTypeView.Attention(Row(("Enabled", false), ("DiscountPercent", 0m))));
        Assert.Contains(ReceiptTypeView.Attention(Row(("Enabled", false), ("PostARDetailInGL", true))), s => s.StartsWith("A/R detail"));
    }

    [Fact] public void Changes_lists_only_what_differs()
    {
        var c = ReceiptTypeView.Changes([("Debit account", "1100", "1150"), ("Reference", "Visa", "Visa "), ("Class", "", null), ("Contra", "x", "")]);
        Assert.Equal(2, c.Count);
        Assert.Equal(("Debit account", "1100", "1150"), c[0]);
        Assert.Equal(("Contra", "x", ""), c[1]);
    }

    [Fact] public void Search_matches_every_word_somewhere_on_the_receipt_type()
    {
        var r = Row(("ReceiptType", "Visa"), ("ReceiptDebitAcct", "Receipts: Visa"), ("DefaultQuickBooksType", "Bank"));
        Assert.True(ReceiptTypeView.Matches(r, null));
        Assert.True(ReceiptTypeView.Matches(r, "visa"));
        Assert.True(ReceiptTypeView.Matches(r, "visa bank"));
        Assert.False(ReceiptTypeView.Matches(r, "visa amex"));
    }

    [Fact] public void Groups_do_not_share_a_field_and_name_real_columns()
    {
        var all = ReceiptTypeView.Groups.SelectMany(g => g.Fields).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        var declared = DDReview.Core.Admin.SetupTables.ReceiptTypes.Columns.Select(c => c.Name).ToHashSet();
        Assert.All(all, f => Assert.Contains(f, declared));
        foreach (var kv in ReceiptTypeView.Copyable) Assert.All(kv.Value, f => Assert.Contains(f, ReceiptTypeView.Groups.First(g => g.Id == kv.Key).Fields));
    }
}
