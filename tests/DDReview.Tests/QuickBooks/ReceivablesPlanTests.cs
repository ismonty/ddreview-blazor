using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Posting;

namespace DDReview.Tests.QuickBooks;

/// <summary>Accounts receivable rows (synthetic customers): invoices, payments, credit memos, adjustments.</summary>
public class ReceivablesPlanTests
{
    const string ARC = "Accounts Receivable:Accounts Receivable-Control";

    static PackageRow Row(int batch, string customer, long check, long txn, int flag, int sorter, string account,
        decimal? debit, decimal? credit, string reference, string date = "2026-03-02") => new()
    {
        PackageNumber = 99, BatchID = batch, ARAccount = customer, CheckNumber = check, TransactionNumber = txn,
        DebitCreditFlag = flag, Sorter = sorter, Account = account, Debit = debit, Credit = credit, DistReference = reference,
        PostingDate = DateTime.Parse(date), AccountClassID = 1, AccountClass = "<None>", DefaultQuickBooksType = "Other Current Asset",
        QuickBooksRefNumber = "DD" + batch,
    };

    static PackageRow Obj(int pkg, string? account = null, string? ar = null, string? item = null, string? type = null) => new()
    { PackageNumber = pkg, Account = account, ARAccount = ar, DistReference = item, DefaultQuickBooksType = type };

    static List<PackageRow> Package(params PackageRow[] lines)
    {
        var rows = new List<PackageRow>
        {
            Obj(2, ARC, type: "Accounts Receivable"), Obj(2, "A/R Posting", type: "Other Current Asset"),
            Obj(2, "Cash:Deposits", type: "Bank"),
            Obj(4, "A/R Posting", item: "House Charge - Food"), Obj(4, "A/R Posting", item: "House Charge - Tax"),
            Obj(4, "A/R Posting", item: "Adjustment"),
            Obj(5, ar: "Test Customer"),
        };
        rows.AddRange(lines);
        return rows;
    }

    static List<PackageRow> InvoiceRows(long check = 5001, long txn = 70001) => new()
    {
        Row(1, "Test Customer", check, txn, 1, 100, ARC, 100m, null, "Check #: 5001"),
        Row(1, "Test Customer", check, txn, -1, 101, "A/R Posting", null, 60m, "House Charge - Food"),
        Row(1, "Test Customer", check, txn, -1, 102, "A/R Posting", null, 40m, "House Charge - Tax"),
    };

    [Fact]
    public void An_invoice_gets_a_line_per_item_and_the_check_and_transaction_in_its_reference()
    {
        var plan = PlanBuilder.Build(Package(InvoiceRows().ToArray()));
        var inv = Assert.Single(plan.Of<Invoice>());
        Assert.Equal("5001-1", inv.RefNumber);
        Assert.Equal("Test Customer", inv.Customer);
        Assert.Equal(ARC, inv.ARAccount);
        Assert.True(inv.ClearMemo);
        Assert.Equal(new[] { "House Charge - Food", "House Charge - Tax" }, inv.Lines.Select(l => l.Item));
        Assert.Equal(100m, inv.Total);
        Assert.Contains("Invoice 5001-1 - Test Customer - 100.00", inv.CheckMessage);
    }

    [Fact]
    public void A_payment_is_one_receive_payment_deposited_to_the_offset_account()
    {
        var plan = PlanBuilder.Build(Package(
            Row(2, "Test Customer", 5002, 70002, 1, 110, ARC, null, 100m, "Payment On Account"),
            Row(2, "Test Customer", 5002, 70002, -1, 111, "Cash:Deposits", 100m, null, "A/R Paid In")));
        var p = Assert.Single(plan.Of<ReceivePayment>());
        Assert.Equal("5002-2", p.RefNumber);
        Assert.Equal(100m, p.TotalAmount);
        Assert.Equal(ARC, p.ARAccount);
        Assert.Equal("Cash:Deposits", p.DepositToAccount);
        Assert.True(p.AutoApply);
        Assert.Empty(plan.Of<Invoice>());
    }

    [Fact]
    public void A_negative_charge_becomes_a_credit_memo()
    {
        var plan = PlanBuilder.Build(Package(
            Row(3, "Test Customer", 5003, 70003, 1, 100, ARC, null, 25m, "Refund"),
            Row(3, "Test Customer", 5003, 70003, -1, 101, "A/R Posting", 25m, null, "House Charge - Food")));
        var cm = Assert.Single(plan.Of<CreditMemo>());
        Assert.Equal("5003-3", cm.RefNumber);
        Assert.Equal(25m, cm.Total);
        Assert.Equal("House Charge - Food", Assert.Single(cm.Lines).Item);
        Assert.Empty(plan.Of<Invoice>());
    }

    [Fact]
    public void Adjustments_go_either_way_by_sign()
    {
        var plan = PlanBuilder.Build(Package(
            Row(4, "Test Customer", 5004, 70004, 1, 113, ARC, null, null, "Adjustment header"),
            Row(4, "Test Customer", 5004, 70004, 1, 114, ARC, null, 15m, "Adjustment"),      // increases the balance: an invoice line
            Row(5, "Test Customer", 5005, 70005, 1, 113, ARC, null, null, "Adjustment header"),
            Row(5, "Test Customer", 5005, 70005, 1, 114, ARC, 7m, null, "Adjustment")));      // decreases it: a credit memo line
        Assert.Equal(15m, Assert.Single(plan.Of<Invoice>()).Total);
        Assert.Equal(7m, Assert.Single(plan.Of<CreditMemo>()).Total);
    }

    [Fact]
    public void Posting_the_receivables_package_twice_is_stable()
    {
        var rows = Package(InvoiceRows().Concat(new[]
        {
            Row(2, "Test Customer", 5002, 70002, 1, 110, ARC, null, 100m, "Payment On Account", "2026-03-09"),
            Row(2, "Test Customer", 5002, 70002, -1, 111, "Cash:Deposits", 100m, null, "A/R Paid In", "2026-03-09"),
            Row(3, "Test Customer", 5003, 70003, 1, 100, ARC, null, 25m, "Refund", "2026-03-10"),
            Row(3, "Test Customer", 5003, 70003, -1, 101, "A/R Posting", 25m, null, "House Charge - Food", "2026-03-10"),
        }).ToArray());
        var qb = new FakeQuickBooks();
        var engine = new PostingEngine(qb);
        var first = engine.Run(PlanBuilder.Build(rows), commit: true);
        Assert.False(first.HasFailures, string.Join("\n", first.Failures.Select(f => f.What + ": " + f.Message)));
        Assert.Equal(3, first.Added.Count());
        qb.Requests.Clear();
        var second = engine.Run(PlanBuilder.Build(rows), commit: true);
        Assert.Equal(3, second.Unchanged.Count());
        Assert.DoesNotContain(qb.Requests, r => r.Contains("AddRq") || r.Contains("ModRq"));
    }

    [Fact]
    public void Invoice_memo_comes_from_the_during_period_message_when_there_is_one()
    {
        var rows = InvoiceRows();
        rows[0] = new PackageRow
        {
            PackageNumber = 99, BatchID = 1, ARAccount = "Test Customer", CheckNumber = 5001, TransactionNumber = 70001, DebitCreditFlag = 1,
            Sorter = 100, Account = ARC, Debit = 100m, DistReference = "x", PostingDate = DateTime.Parse("2026-03-02"), AccountClassID = 1,
            DuringPeriodInvoiceMessage = "Thank you for your business", QuickBooksRefNumber = "DD1",
        };
        var inv = Assert.Single(PlanBuilder.Build(Package(rows.ToArray())).Of<Invoice>());
        Assert.Equal("Thank you for your business", inv.Memo);
        Assert.False(inv.ClearMemo);
    }
}
