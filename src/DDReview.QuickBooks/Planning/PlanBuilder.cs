using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Qbxml;

namespace DDReview.QuickBooks.Planning;

/// <summary>
/// Turns the export package rows (ordered by PackageNumber, BatchID, ARAccount, CheckNumber, TransactionNumber,
/// abs(Sorter), DebitCreditFlag desc, Account, AccountClassID) into a PostingPlan. This is a port of
/// QBPostTransactionCreateGeneralLedgerTransaction in DDAdminCS; the control flow is kept as it was because the rules
/// for which rows make an invoice, a payment, a credit memo or a journal entry live in it.
/// </summary>
public static class PlanBuilder
{
    /// <summary>
    /// The order the rows must be in: the ORDER BY DDAdminCS put on the export query. The builder sorts for itself so it
    /// does not matter whether the rows came from SQL already ordered or from a saved file.
    /// </summary>
    public static List<PackageRow> InPostingOrder(IEnumerable<PackageRow> rows)
    {
        static int S(string? a, string? b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        var list = rows.ToList();
        var cmp = Comparer<PackageRow>.Create((x, y) =>
        {
            int c = x.PackageNumber.CompareTo(y.PackageNumber); if (c != 0) return c;
            c = Nullable.Compare(x.BatchID, y.BatchID); if (c != 0) return c;
            c = S(x.ARAccount, y.ARAccount); if (c != 0) return c;
            c = Nullable.Compare(x.CheckNumber, y.CheckNumber); if (c != 0) return c;
            c = Nullable.Compare(x.TransactionNumber, y.TransactionNumber); if (c != 0) return c;
            c = Nullable.Compare<int>(x.Sorter is { } a ? Math.Abs(a) : null, y.Sorter is { } b ? Math.Abs(b) : null); if (c != 0) return c;
            c = Nullable.Compare(y.DebitCreditFlag, x.DebitCreditFlag); if (c != 0) return c;   // descending
            c = S(x.Account, y.Account); if (c != 0) return c;
            return Nullable.Compare(x.AccountClassID, y.AccountClassID);
        });
        // List.Sort is not stable; order by index to keep ties in their original order.
        return list.Select((r, i) => (r, i)).OrderBy(t => t.r, cmp).ThenBy(t => t.i).Select(t => t.r).ToList();
    }

    public static PostingPlan Build(IReadOnlyList<PackageRow> unsortedRows, PostingOptions? options = null)
    {
        options ??= new PostingOptions();
        var rows = InPostingOrder(unsortedRows);
        var plan = new PostingPlan();
        bool package7Present = rows.Any(r => r.PackageNumber == 7);

        foreach (var r in rows)
        {
            switch (r.PackageNumber)
            {
                case 1:
                    plan.Objects.Add(new ObjectRequirement(ObjectKind.Vendor, QbText.TrimSpacesAroundColons(r.ItemName ?? "")));
                    break;
                case 2:
                    if (r.DefaultQuickBooksType != null && r.Account != null)
                        plan.Objects.Add(new ObjectRequirement(ObjectKind.Account, QbText.TrimSpacesAroundColons(r.Account))
                        { DefaultAccountType = QbText.AccountType(r.DefaultQuickBooksType) });
                    break;
                case 3:
                    {
                        var st = (r.AccountClass ?? "").Trim();
                        if (!(st.StartsWith('[') && st.EndsWith(']')))
                            plan.Objects.Add(new ObjectRequirement(ObjectKind.Class, st));
                        break;
                    }
                case 4:
                    plan.Objects.Add(new ObjectRequirement(ObjectKind.ItemService, (r.DistReference ?? "").Trim())
                    { ItemAccount = QbText.TrimSpacesAroundColons(r.Account ?? "") });
                    break;
                case 5:
                    if (!package7Present)
                        plan.Objects.Add(new ObjectRequirement(ObjectKind.Customer, (r.ARAccount ?? "").Trim()));
                    break;
                case 6:
                    plan.Objects.Add(new ObjectRequirement(ObjectKind.CustomerType, (r.ARAccountType ?? "").Trim()));
                    break;
                case 7:
                    if (r.QuickBooksRefNumber != null)
                    {
                        var c = new ObjectRequirement(ObjectKind.Customer, (r.ARAccount ?? "").Trim())
                        {
                            CustomerType = r.ARAccountType,
                            AccountNumber = r.QuickBooksRefNumber,
                            ParentCustomer = r.ParentARAccount ?? "",
                        };
                        if (r.ClubMinimumSpendingExpected != null)
                            c = c with
                            {
                                MinimumExpected = QbText.Fmt(r.ClubMinimumSpendingExpected.Value),
                                MinimumSpent = QbText.Fmt(r.ClubMinimumSpendingActual ?? 0),
                                MinimumRemaining = QbText.Fmt(r.ClubMinimumSpendingRemaining ?? 0),
                            };
                        plan.Objects.Add(c);
                    }
                    break;
                case 8:
                    if (r.ARAccount != null) plan.Objects.Add(new ObjectRequirement(ObjectKind.ItemSalesTax, "Non"));
                    break;
            }
        }

        BuildTransactions(rows.Where(r => r.PackageNumber == 99).ToList(), options, plan);
        return plan;
    }

    // ---- the transaction state machine ----

    static string ClassOf(PackageRow r)
    {
        var st = (r.AccountClass ?? "").Trim();
        return st.StartsWith('[') && st.EndsWith(']') ? "" : st;
    }

    static bool ClassApplies(PackageRow r) => r.AccountClassID is { } id && id != 1;
    static decimal Net(PackageRow r) => (r.Debit ?? 0) - (r.Credit ?? 0);
    static string AR(PackageRow r) => r.ARAccount ?? "";
    static string Ref(PackageRow r) => Math.Abs(r.CheckNumber ?? 0) + "-" + ((r.TransactionNumber ?? 0) % 10000);
    static string Date(PackageRow r) => r.PostingDate?.ToString("yyyy-MM-dd") ?? "";

    static void BuildTransactions(List<PackageRow> rows, PostingOptions o, PostingPlan plan)
    {
        var jes = new List<JournalEntry>();
        var invoices = new List<Invoice>();
        var payments = new List<ReceivePayment>();
        var memos = new List<CreditMemo>();

        JournalEntry? je = null;
        Invoice? inv = null;
        CreditMemo? crmem = null;
        int batch = -1;
        string? varAR = null;
        long? varCheck = null, varTxn = null;
        string? detailMessage = null;
        string? invoiceMessage = null;      // null = Null; ClearSentinel = clear
        const string Clear = "\u0001CLEAR";
        string glForPayment = "";
        string? glAR = null;
        bool isCredit = false, isInvoice = false, isPayment = false, isAdjustment = false;
        string duringPeriod = "";

        Invoice NewInvoice(PackageRow r, string? arAccount)
        {
            var i = new Invoice { Customer = r.ARAccount ?? "", ARAccount = arAccount };
            var st = ClassOf(r);
            if (ClassApplies(r) && st.Length > 0) i.Class = st;
            i.Date = r.PostingDate ?? default;
            i.RefNumber = Ref(r);
            if (o.ItemSalesTaxCode.Trim().Length > 0) i.SalesTaxCode = o.ItemSalesTaxCode.Trim();
            return i;
        }

        CreditMemo NewCreditMemo(PackageRow r, string? arAccount)
        {
            var c = new CreditMemo { Customer = QbText.TrimSpacesAroundColons(r.ARAccount ?? "") };
            var st = ClassOf(r);
            if (ClassApplies(r) && st.Length > 0) c.Class = st;
            if (!string.IsNullOrEmpty(arAccount)) c.ARAccount = QbText.TrimSpacesAroundColons(arAccount);
            c.Date = r.PostingDate ?? default;
            c.RefNumber = Ref(r);
            c.Memo = (r.DistReference ?? "").Trim();
            return c;
        }

        ItemLine NewLine(PackageRow r, decimal amount) => new()
        {
            Item = (r.DistReference ?? "").Trim(),
            Description = (r.DistReference ?? "").Trim(),
            Amount = amount,
        };

        string DetailMemo(string msg) => msg.Replace("\r\n  ", ", ");

        foreach (var r in rows)
        {
            if (r.DuringPeriodInvoiceMessage != null) duringPeriod = r.DuringPeriodInvoiceMessage;

            bool groupChanged = batch != (r.BatchID ?? 0)
                || varAR != r.ARAccount
                || (r.ARAccount != null && (varCheck != Math.Abs(r.CheckNumber ?? 0) || varTxn != (r.TransactionNumber == null ? null : r.TransactionNumber % 10000)));

            if (groupChanged)
            {
                batch = r.BatchID ?? 0;
                varAR = r.ARAccount;
                varCheck = Math.Abs(r.CheckNumber ?? 0);
                detailMessage = r.DetailInvoiceMessage;
                if (detailMessage != null && detailMessage.Contains('�')) detailMessage = detailMessage.Replace('�', 'n');
                varTxn = r.ARAccount != null && r.TransactionNumber != null ? r.TransactionNumber % 10000 : null;

                if (je != null) { jes.Add(je); je = null; }
                if (inv != null)
                {
                    if (detailMessage != null && r.Account != null && o.DetailPostingActive)
                    {
                        if (o.PostDetailAsInvoiceMemo)
                        {
                            if (!inv.ClearMemo)
                            {
                                var m = (DetailMemo(detailMessage) + "\r\n" + (inv.Memo ?? "").Trim()).Trim();
                                inv.Memo = m.Length > 4090 ? m[..4090] : m;
                            }
                        }
                        else if (detailMessage.Trim().Length > 0)
                        {
                            var d = DetailMemo(detailMessage).Trim();
                            inv.Lines.Add(new ItemLine { Item = "Purchase Details", Description = d.Length > 4090 ? d[..4090] : d });
                        }
                        detailMessage = null;
                    }
                    invoices.Add(inv); inv = null;
                }
                if (crmem != null) { memos.Add(crmem); crmem = null; }

                isInvoice = isCredit = isPayment = isAdjustment = false;

                if (r.ARAccount == null)
                {
                    je = new JournalEntry { RefNumber = r.QuickBooksRefNumber ?? "", Date = r.PostingDate ?? default };
                    varAR = null; varCheck = null; varTxn = null;
                }
                else if (r.DebitCreditFlag == 1)
                {
                    if (r.Sorter == 110)
                    {
                        isPayment = true;
                        glForPayment = QbText.TrimSpacesAroundColons(r.Account ?? "");
                    }
                    else if (r.Sorter == 113)
                    {
                        isAdjustment = true;
                        glAR = QbText.TrimSpacesAroundColons(r.Account ?? "");
                    }
                    else if (r.Sorter == 114)
                    {
                        isCredit = true;
                        crmem = NewCreditMemo(r, glAR);
                        crmem.Lines.Add(NewLine(r, Net(r)));
                    }
                    else
                    {
                        if (Net(r) < 0) isCredit = true;
                        if (Net(r) > 0)
                        {
                            isInvoice = true;
                            glAR = QbText.TrimSpacesAroundColons(r.Account ?? "");
                        }
                    }
                }
                else if (r.DebitCreditFlag < 0)
                {
                    if (isCredit)
                    {
                        crmem ??= NewCreditMemo(r, glAR);
                    }
                    else if (inv == null)
                    {
                        inv = NewInvoice(r, r.Account == null ? null : QbText.TrimSpacesAroundColons(r.Account));
                        inv.Memo = (r.DistReference ?? "").Trim();
                    }
                    batch = r.BatchID ?? 0;
                    varAR = r.ARAccount;
                    varCheck = Math.Abs(r.CheckNumber ?? 0);
                    varTxn = (r.TransactionNumber ?? 0) % 10000;
                }
            }

            if (r.ARAccount == null)
            {
                if (Net(r) != 0 && r.DefaultQuickBooksType != null)
                {
                    je ??= new JournalEntry { RefNumber = r.QuickBooksRefNumber ?? "", Date = r.PostingDate ?? default };
                    var net = Net(r);
                    var line = new JournalLine
                    {
                        Account = QbText.TrimSpacesAroundColons(r.Account ?? ""),
                        Debit = net < 0 ? 0 : net,
                        Credit = net < 0 ? -net : 0,
                    };
                    if (r.AccountClass != null && ClassApplies(r))
                    {
                        var st = ClassOf(r);
                        if (st.Length > 0) line.Class = QbText.TrimSpacesAroundColons(st);
                    }
                    if (r.ItemName != null) line.Entity = QbText.TrimSpacesAroundColons(r.ItemName);
                    if (r.DistReference != null) line.Memo = r.DistReference.Trim();
                    if (r.TaxRate is { } tr && tr != 0)
                    {
                        if (o.SalesTaxEntityListId.Trim().Length > 0) line.EntityListId = o.SalesTaxEntityListId;
                        else if (o.SalesTaxEntityName.Trim().Length > 0) line.Entity = o.SalesTaxEntityName;
                        if (o.SalesTaxItemListId.Trim().Length > 0) line.SalesTaxItemListId = o.SalesTaxItemListId;
                        else if (o.SalesTaxItemName.Trim().Length > 0) line.SalesTaxItem = o.SalesTaxItemName;
                    }
                    je.Lines.Add(line);
                }
            }
            else if (r.DebitCreditFlag < 1)
            {
                if (isPayment)
                {
                    payments.Add(new ReceivePayment
                    {
                        Customer = r.ARAccount,
                        ARAccount = glForPayment,
                        Date = r.PostingDate ?? default,
                        RefNumber = Ref(r),
                        TotalAmount = -((r.Credit ?? 0) - (r.Debit ?? 0)),
                        DepositToAccount = QbText.TrimSpacesAroundColons(r.Account ?? ""),
                        AutoApply = true,
                    });
                }
                else if (isInvoice)
                {
                    if (inv == null)
                    {
                        inv = NewInvoice(r, r.Account == null ? null : glAR);
                        if (o.PostDetailAsInvoiceMemo && detailMessage != null)
                        {
                            if (invoiceMessage != Clear) inv.Memo = detailMessage + "\r\n" + (invoiceMessage ?? "");
                            else inv.Memo = detailMessage;
                            invoiceMessage = null;
                            detailMessage = null;
                        }
                        else if (duringPeriod.Trim().Length > 0)
                        {
                            inv.Memo = duringPeriod;
                            duringPeriod = "";
                        }
                        else inv.ClearMemo = true;
                    }
                    inv.Lines.Add(NewLine(r, -Net(r)));
                }
                else if (isCredit)
                {
                    crmem ??= NewCreditMemo(r, glAR);
                    crmem.Lines.Add(NewLine(r, Net(r)));
                }
            }
            else if (isAdjustment && r.Sorter == 114)
            {
                if (-Net(r) > 0)
                {
                    if (inv == null)
                    {
                        inv = NewInvoice(r, r.Account == null ? null : glAR);
                        if (invoiceMessage != null) inv.Memo = invoiceMessage == Clear ? null : invoiceMessage;
                        invoiceMessage = null;
                    }
                    inv.Lines.Add(NewLine(r, -Net(r)));
                }
                else
                {
                    crmem ??= NewCreditMemo(r, r.Account == null ? null : glAR);
                    crmem.Lines.Add(NewLine(r, Net(r)));
                }
            }
            else
            {
                detailMessage ??= r.DetailInvoiceMessage;
                if ((invoiceMessage ?? "").Length == 0) invoiceMessage = Clear;
            }
        }

        if (je != null) jes.Add(je);
        if (inv != null)
        {
            // The original looked at a closed recordset here; the intent is the same as in the group change.
            if (detailMessage != null && o.DetailPostingActive)
            {
                if (o.PostDetailAsInvoiceMemo) inv.Memo = DetailMemo(detailMessage);
                else if (detailMessage.Trim().Length > 0)
                    inv.Lines.Add(new ItemLine { Item = "Purchase Details", Description = DetailMemo(detailMessage) });
            }
            invoices.Add(inv);
        }
        if (crmem != null) memos.Add(crmem);

        foreach (var j in jes)
        {
            j.CheckMessage = $"{j.Date:MM/dd/yyyy} - Journal Entry {j.RefNumber} - Debits {QbText.Fmt(j.TotalDebits)}";
            plan.Transactions.Add(j);
        }
        foreach (var i in invoices)
        {
            i.CheckMessage = $"{i.Date:MM/dd/yyyy} - Invoice {i.RefNumber} - {i.Customer} - {QbText.Fmt(i.Total)}";
            plan.Transactions.Add(i);
        }
        foreach (var p in payments)
        {
            p.CheckMessage = $"{p.Date:MM/dd/yyyy} - ReceivePayment {p.RefNumber} - {p.Customer} - {QbText.Fmt(p.TotalAmount)}";
            plan.Transactions.Add(p);
        }
        foreach (var c in memos)
        {
            c.CheckMessage = $"{c.Date:MM/dd/yyyy} - CreditMemo {c.RefNumber} - {c.Customer} - {QbText.Fmt(c.Total)}";
            plan.Transactions.Add(c);
        }
    }
}
