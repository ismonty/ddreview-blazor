using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Posting;

namespace DDReview.Tests.QuickBooks;

public class PostingEngineTests
{
    static PostingPlan Plan() => PlanBuilder.Build(CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv")));

    [Fact]
    public void Preview_changes_nothing_and_counts_what_would_happen()
    {
        var qb = new FakeQuickBooks();
        var report = new PostingEngine(qb).Run(Plan(), commit: false);
        Assert.False(report.Committed);
        Assert.Empty(qb.ListNames("Account"));
        Assert.Equal(0, qb.Sent("JournalEntryAddRq"));
        Assert.Equal(25, report.Added.Count());           // 24 journal entries and the payment (the payment's invoice is not in this package)
        Assert.Contains("25 Transactions Will Be Added", report.Summary);
    }

    [Fact]
    public void First_post_creates_the_lists_and_every_transaction()
    {
        var qb = new FakeQuickBooks();
        var report = new PostingEngine(qb).Run(Plan(), commit: true);
        Assert.False(report.HasFailures, string.Join("\n", report.Failures.Select(f => f.What + ": " + f.Message)));
        Assert.Equal(24, qb.Transactions("JournalEntry").Count());
        Assert.Single(qb.Transactions("ReceivePayment"));
        Assert.Contains("0100:NBC Checking Main", qb.ListNames("Account"));
        Assert.Contains("0100", qb.ListNames("Account"));
        Assert.Contains("State of Minnesota", qb.ListNames("Vendor"));
        Assert.Contains("Parties", qb.ListNames("Customer"));
        Assert.Equal("Income", (string?)qb.Find("Account", "0100:NBC Checking Main")!.Element("AccountType"));   // copied from the old behaviour
    }

    [Fact]
    public void Posting_again_leaves_everything_unchanged_and_sends_no_adds_or_mods()
    {
        var qb = new FakeQuickBooks();
        var engine = new PostingEngine(qb);
        engine.Run(Plan(), commit: true);
        qb.Requests.Clear();
        var report = engine.Run(Plan(), commit: true);
        Assert.False(report.HasFailures, string.Join("\n", report.Failures.Select(f => f.What + ": " + f.Message)));
        Assert.Equal(25, report.Unchanged.Count());
        Assert.Empty(report.Added);
        Assert.Empty(report.Modified);
        Assert.DoesNotContain(qb.Requests, r => r.Contains("AddRq") || r.Contains("ModRq"));
    }

    [Fact]
    public void A_changed_amount_modifies_the_existing_journal_entry()
    {
        var qb = new FakeQuickBooks();
        var engine = new PostingEngine(qb);
        engine.Run(Plan(), commit: true);
        var plan = Plan();
        var je = plan.Of<JournalEntry>().First();
        var line = je.Lines.First(l => l.Credit > 0);
        line.Credit += 1.00m;                  // now unbalanced against QuickBooks' copy; the fake does not check balance
        var report = engine.Run(plan, commit: true);
        Assert.Single(report.Modified);
        Assert.Equal(1, qb.Sent("JournalEntryModRq"));
        var stored = qb.Transactions("JournalEntry").First(t => t.Element("RefNumber")!.Value == je.RefNumber);
        Assert.Contains(stored.Elements("JournalCreditLine"), l => l.Element("Amount")!.Value == DDReview.QuickBooks.Qbxml.QbText.Fmt(line.Credit));
    }

    [Fact]
    public void An_existing_parent_account_gives_its_type_to_new_children()
    {
        var qb = new FakeQuickBooks();
        qb.Seed("Account", "0100", "Bank");
        new PostingEngine(qb).Run(Plan(), commit: true);
        Assert.Equal("Bank", (string?)qb.Find("Account", "0100:NBC Checking Main")!.Element("AccountType"));
    }

    [Fact]
    public void A_missing_reference_is_reported_not_thrown()
    {
        var qb = new FakeQuickBooks();
        var plan = Plan();
        plan.Objects.RemoveAll(o => o.Kind == ObjectKind.Class || (o.Kind == ObjectKind.Account && o.FullName.StartsWith("Food Sales")));
        var report = new PostingEngine(qb).Run(plan, commit: true);
        Assert.True(report.HasFailures);
        Assert.All(report.Failures, f => Assert.NotNull(f.Message));
    }
}
