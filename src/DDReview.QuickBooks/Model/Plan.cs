namespace DDReview.QuickBooks.Model;

public enum ObjectKind { Vendor, Account, Class, ItemService, Customer, CustomerType, ItemSalesTax }

/// <summary>A list object (vendor, account, class, item, customer ...) a transaction refers to, which must exist first.</summary>
public sealed record ObjectRequirement(ObjectKind Kind, string FullName)
{
    /// <summary>Account: the QuickBooks account type to create it as (no spaces, e.g. OtherCurrentAsset).</summary>
    public string? DefaultAccountType { get; init; }
    /// <summary>Service item: the income account its sales go to.</summary>
    public string? ItemAccount { get; init; }
    /// <summary>Customer: customer type, account number, parent, and club minimum figures when present.</summary>
    public string? CustomerType { get; init; }
    public string? AccountNumber { get; init; }
    public string? ParentCustomer { get; init; }
    public string? MinimumExpected { get; init; }
    public string? MinimumSpent { get; init; }
    public string? MinimumRemaining { get; init; }
}

public enum TransactionKind { JournalEntry, Invoice, ReceivePayment, CreditMemo }

public abstract class QbTransaction
{
    public abstract TransactionKind Kind { get; }
    public DateTime Date { get; set; }
    public string RefNumber { get; set; } = "";
    /// <summary>One line describing it for the confirmation and results lists.</summary>
    public string CheckMessage { get; set; } = "";
    /// <summary>The class on the transaction, when any.</summary>
    public string? Class { get; set; }
}

public sealed class JournalLine
{
    public string Account { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Class { get; set; }
    public string? Entity { get; set; }
    public string? EntityListId { get; set; }
    public string? Memo { get; set; }
    public string? SalesTaxItem { get; set; }
    public string? SalesTaxItemListId { get; set; }
    public bool IsDebit => Debit >= Credit;
    public decimal Amount => Math.Abs(Debit - Credit);
}

public sealed class JournalEntry : QbTransaction
{
    public override TransactionKind Kind => TransactionKind.JournalEntry;
    public List<JournalLine> Lines { get; } = new();
    public decimal TotalDebits => Lines.Sum(l => l.IsDebit ? l.Amount : 0m);
}

public sealed class ItemLine
{
    public string Item { get; set; } = "";
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

public sealed class Invoice : QbTransaction
{
    public override TransactionKind Kind => TransactionKind.Invoice;
    public string Customer { get; set; } = "";
    public string? ARAccount { get; set; }
    public string? SalesTaxCode { get; set; }
    public string? Memo { get; set; }
    /// <summary>Send an empty Memo (clears whatever is in QuickBooks).</summary>
    public bool ClearMemo { get; set; }
    public List<ItemLine> Lines { get; } = new();
    public decimal Total => Lines.Sum(l => l.Amount);
}

public sealed class CreditMemo : QbTransaction
{
    public override TransactionKind Kind => TransactionKind.CreditMemo;
    public string Customer { get; set; } = "";
    public string? ARAccount { get; set; }
    public string? Memo { get; set; }
    public List<ItemLine> Lines { get; } = new();
    public decimal Total => Lines.Sum(l => l.Amount);
}

public sealed class ReceivePayment : QbTransaction
{
    public override TransactionKind Kind => TransactionKind.ReceivePayment;
    public string Customer { get; set; } = "";
    public string? ARAccount { get; set; }
    public decimal TotalAmount { get; set; }
    public string DepositToAccount { get; set; } = "";
    public bool AutoApply { get; set; } = true;
}

/// <summary>Everything one posting run does: objects to make sure exist, then transactions in posting order.</summary>
public sealed class PostingPlan
{
    public List<ObjectRequirement> Objects { get; } = new();
    public List<QbTransaction> Transactions { get; } = new();
    public IEnumerable<T> Of<T>() where T : QbTransaction => Transactions.OfType<T>();
}
