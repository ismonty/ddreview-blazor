using System.Text.Json;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Posting;
using DDReview.QuickBooks.Qbxml;

namespace DDReview.Tests.QuickBooks;

public class AccountTypeTests
{
    [Theory]
    [InlineData("Income", "Income")]
    [InlineData("income", "Income")]
    [InlineData("Other Current Asset", "OtherCurrentAsset")]
    [InlineData("Other Current Assets", "OtherCurrentAsset")]
    [InlineData("OtherCurrentLiability", "OtherCurrentLiability")]
    [InlineData("Checking or Savings", "Bank")]
    [InlineData("Bank", "Bank")]
    [InlineData("Accounts Receivable (A/R)", "AccountsReceivable")]
    [InlineData("AccountsReceivable", "AccountsReceivable")]
    [InlineData("Credit Card", "CreditCard")]
    [InlineData("Cost of Goods Sold", "CostOfGoodsSold")]
    [InlineData("Long Term Liability", "LongTermLiability")]
    [InlineData("  expense ", "Expense")]
    public void Names_DD_Review_may_hold_become_real_QuickBooks_types(string raw, string expected) => Assert.Equal(expected, QbAccountTypes.Normalize(raw));

    [Theory]
    [InlineData("0")] [InlineData("")] [InlineData("  ")] [InlineData(null)] [InlineData("1")] [InlineData("Banana")] [InlineData("Equity; drop table")]
    public void Anything_else_is_not_a_type(string? raw) => Assert.Null(QbAccountTypes.Normalize(raw));

    [Fact] public void Every_valid_type_normalizes_to_itself() { foreach (var v in QbAccountTypes.Valid) Assert.Equal(v, QbAccountTypes.Normalize(v)); }

    [Theory]
    [InlineData("Bank Deposit Account", 500, true, "Bank")]
    [InlineData("Cash: NBC Checking Main", -5, true, "Bank")]
    [InlineData("Food Sales", -1000, true, "Income")]
    [InlineData("Credit Card Receipts:Visa", 800, true, "OtherCurrentAsset")]
    [InlineData("Something never used", 0, false, "OtherCurrentAsset")]
    public void A_guess_follows_the_name_and_how_the_account_is_used(string name, double net, bool used, string expected) =>
        Assert.Equal(expected, QbAccountTypes.Guess(name, (decimal)net, used));

    static List<PackageRow> RowsWithUnsetTypes(params string[] accountsToUnset)
    {
        var rows = CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv"));
        var json = JsonSerializer.SerializeToNode(rows)!.AsArray();
        foreach (var n in json)
            if (n!["PackageNumber"]!.GetValue<int>() == 2 && n["Account"] is { } a && accountsToUnset.Contains(a.GetValue<string>().Trim()))
                n["DefaultQuickBooksType"] = "0";
        return JsonSerializer.Deserialize<List<PackageRow>>(json)!;
    }

    [Fact] public void An_account_with_type_0_is_created_with_a_valid_type_and_Preview_says_it_was_guessed()
    {
        var all = CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv"));
        var names = all.Where(r => r.PackageNumber == 2).Select(r => r.Account!.Trim()).Distinct().ToArray();
        var rows = RowsWithUnsetTypes(names);                       // every account unset, like the store that failed
        var plan = PlanBuilder.Build(rows);
        Assert.All(plan.Objects.Where(o => o.Kind == ObjectKind.Account && o.GuessedTypeFrom != null), o => Assert.Contains(o.DefaultAccountType!, QbAccountTypes.Valid));
        Assert.Contains(plan.Objects, o => o.GuessedTypeFrom == "0");

        var qb = new FakeQuickBooks();
        var preview = new PostingEngine(qb).Run(plan, commit: false);
        Assert.True(preview.Warnings.Count > 0);
        Assert.Contains("DD Review has no QuickBooks type", preview.Warnings[0]);
        Assert.Empty(qb.ListNames("Account"));                      // Preview creates nothing

        var posted = new PostingEngine(new FakeQuickBooks()).Run(plan, commit: true);
        Assert.False(posted.HasFailures, string.Join("\n", posted.Failures.Select(f => f.What + ": " + f.Message)));
        Assert.Equal(25, posted.Added.Count());                     // 24 journal entries and the payment, as in the normal sample
    }

    [Fact] public void Sales_accounts_are_guessed_as_Income_and_receipt_accounts_as_assets()
    {
        var rows = RowsWithUnsetTypes("Alcoholic Beverage Sales: Beer", "Cash: NBC Checking Main", "Charges: Credit Card");
        var plan = PlanBuilder.Build(rows);
        string T(string n) => plan.Objects.First(o => o.Kind == ObjectKind.Account && o.FullName == QbText.TrimSpacesAroundColons(n)).DefaultAccountType!;
        Assert.Equal("Income", T("Alcoholic Beverage Sales: Beer"));
        Assert.Equal("Bank", T("Cash: NBC Checking Main"));
    }

    [Fact] public void Accounts_with_a_real_type_are_untouched_and_raise_no_warning()
    {
        var plan = PlanBuilder.Build(CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv")));
        Assert.All(plan.Objects.Where(o => o.Kind == ObjectKind.Account), o => Assert.Null(o.GuessedTypeFrom));
        Assert.Empty(new PostingEngine(new FakeQuickBooks()).Run(plan, commit: true).Warnings);
    }

    [Fact] public void The_fake_QuickBooks_refuses_a_zero_type_like_the_real_one()
    {
        var qb = new FakeQuickBooks();
        var all = CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv"));
        var plan = PlanBuilder.Build(all);
        var broken = plan.Objects.First(o => o.Kind == ObjectKind.Account) with { DefaultAccountType = "0" };
        var bad = new PostingPlan(); bad.Objects.Add(broken);
        Assert.True(new PostingEngine(qb).Run(bad, commit: true).HasFailures);
    }
}
