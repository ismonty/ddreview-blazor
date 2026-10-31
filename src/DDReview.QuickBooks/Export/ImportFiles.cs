using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Qbxml;

namespace DDReview.QuickBooks.Export;

/// <summary>
/// Files QuickBooks can import without this program talking to it: an IIF file (QuickBooks Desktop, File &gt; Utilities &gt;
/// Import &gt; IIF Files), a CSV of the ledger lines (Excel, or the journal-entry import of QuickBooks Online), and a QBXML
/// file of the same add requests the poster would send.
/// </summary>
public static class ImportFiles
{
    // ---- IIF ----

    static readonly Dictionary<string, string> IifAccountTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bank"] = "BANK", ["AccountsReceivable"] = "AR", ["OtherCurrentAsset"] = "OCASSET", ["FixedAsset"] = "FIXASSET",
        ["OtherAsset"] = "OASSET", ["AccountsPayable"] = "AP", ["CreditCard"] = "CCARD", ["OtherCurrentLiability"] = "OCLIAB",
        ["LongTermLiability"] = "LTLIAB", ["Equity"] = "EQUITY", ["Income"] = "INC", ["CostOfGoodsSold"] = "COGS",
        ["Expense"] = "EXP", ["OtherIncome"] = "EXINC", ["OtherExpense"] = "EXEXP",
    };

    static string D(DateTime d) => d.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
    static string A(decimal a) => a.ToString("0.00", CultureInfo.InvariantCulture);
    static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>
    /// Writes the plan as an IIF file. Journal entries become GENERAL JOURNAL transactions; A/R invoices, payments and credit memos
    /// become INVOICE, PAYMENT and CREDIT MEMO transactions. (IIF cannot say which invoice a payment pays: apply payments in
    /// QuickBooks, or post with the poster, which uses auto-apply.)
    /// </summary>
    public static string Iif(PostingPlan plan)
    {
        var sb = new StringBuilder();
        void Line(params string?[] cols) => sb.Append(string.Join("\t", cols.Select(Clean))).Append("\r\n");

        // Lists. Parents come before children because the objects are in that order in the plan.
        Line("!ACCNT", "NAME", "ACCNTTYPE");
        foreach (var o in plan.Objects.Where(o => o.Kind == ObjectKind.Account).DistinctBy(o => o.FullName))
            Line("ACCNT", o.FullName, IifAccountTypes.GetValueOrDefault(o.DefaultAccountType ?? "", "BANK"));
        Line("!CLASS", "NAME");
        foreach (var o in plan.Objects.Where(o => o.Kind == ObjectKind.Class).DistinctBy(o => o.FullName)) Line("CLASS", o.FullName);
        Line("!VEND", "NAME");
        foreach (var o in plan.Objects.Where(o => o.Kind == ObjectKind.Vendor).DistinctBy(o => o.FullName)) Line("VEND", o.FullName);
        Line("!CUST", "NAME", "CUSTFLD1");
        foreach (var o in plan.Objects.Where(o => o.Kind == ObjectKind.Customer).DistinctBy(o => o.FullName)) Line("CUST", o.FullName, o.AccountNumber);
        Line("!INVITEM", "NAME", "INVITEMTYPE", "ACCNT");
        foreach (var o in plan.Objects.Where(o => o.Kind == ObjectKind.ItemService).DistinctBy(o => o.FullName)) Line("INVITEM", o.FullName, "SERV", o.ItemAccount);

        Line("!TRNS", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "MEMO");
        Line("!SPL", "TRNSTYPE", "DATE", "ACCNT", "NAME", "CLASS", "AMOUNT", "DOCNUM", "MEMO", "INVITEM");
        Line("!ENDTRNS");

        foreach (var t in plan.Transactions)
        {
            switch (t)
            {
                case JournalEntry je:
                    for (int i = 0; i < je.Lines.Count; i++)
                    {
                        var l = je.Lines[i];
                        var amt = l.Debit - l.Credit;
                        Line(i == 0 ? "TRNS" : "SPL", "GENERAL JOURNAL", D(je.Date), l.Account, l.Entity, l.Class, A(amt), je.RefNumber, l.Memo, "");
                    }
                    break;
                case Invoice inv:
                    Line("TRNS", "INVOICE", D(inv.Date), inv.ARAccount ?? "Accounts Receivable", inv.Customer, inv.Class, A(inv.Total), inv.RefNumber, inv.Memo, "");
                    foreach (var l in inv.Lines)
                        Line("SPL", "INVOICE", D(inv.Date), ItemAccount(plan, l.Item), inv.Customer, inv.Class, A(-l.Amount), inv.RefNumber, l.Description, l.Item);
                    break;
                case CreditMemo cm:
                    Line("TRNS", "CREDIT MEMO", D(cm.Date), cm.ARAccount ?? "Accounts Receivable", cm.Customer, cm.Class, A(-cm.Total), cm.RefNumber, cm.Memo, "");
                    foreach (var l in cm.Lines)
                        Line("SPL", "CREDIT MEMO", D(cm.Date), ItemAccount(plan, l.Item), cm.Customer, cm.Class, A(l.Amount), cm.RefNumber, l.Description, l.Item);
                    break;
                case ReceivePayment p:
                    Line("TRNS", "PAYMENT", D(p.Date), p.DepositToAccount, p.Customer, "", A(p.TotalAmount), p.RefNumber, "", "");
                    Line("SPL", "PAYMENT", D(p.Date), p.ARAccount ?? "Accounts Receivable", p.Customer, "", A(-p.TotalAmount), p.RefNumber, "", "");
                    break;
            }
            Line("ENDTRNS");
        }
        return sb.ToString();
    }

    static string ItemAccount(PostingPlan plan, string item) =>
        plan.Objects.FirstOrDefault(o => o.Kind == ObjectKind.ItemService && o.FullName == item)?.ItemAccount ?? "A/R Posting";

    // ---- CSV ----

    static string Q(string? s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    /// <summary>One row per ledger line: Date, Type, Ref, Name, Account, Class, Debit, Credit, Memo.</summary>
    public static string Csv(PostingPlan plan)
    {
        var sb = new StringBuilder("Date,Type,Ref,Name,Account,Class,Debit,Credit,Memo\r\n");
        void Row(DateTime d, string type, string refNo, string? name, string account, string? cls, decimal debit, decimal credit, string? memo) =>
            sb.Append(string.Join(",", new[]
            {
                d.ToString("yyyy-MM-dd"), type, Q(refNo), Q(name), Q(account), Q(cls),
                debit == 0 ? "" : A(debit), credit == 0 ? "" : A(credit), Q(memo),
            })).Append("\r\n");
        foreach (var t in plan.Transactions)
            switch (t)
            {
                case JournalEntry je:
                    foreach (var l in je.Lines) Row(je.Date, "Journal Entry", je.RefNumber, l.Entity, l.Account, l.Class, l.Debit - l.Credit > 0 ? l.Debit - l.Credit : 0, l.Credit - l.Debit > 0 ? l.Credit - l.Debit : 0, l.Memo);
                    break;
                case Invoice inv:
                    Row(inv.Date, "Invoice", inv.RefNumber, inv.Customer, inv.ARAccount ?? "Accounts Receivable", inv.Class, inv.Total, 0, inv.Memo);
                    foreach (var l in inv.Lines) Row(inv.Date, "Invoice", inv.RefNumber, inv.Customer, ItemAccount(plan, l.Item), inv.Class, 0, l.Amount, l.Description);
                    break;
                case CreditMemo cm:
                    Row(cm.Date, "Credit Memo", cm.RefNumber, cm.Customer, cm.ARAccount ?? "Accounts Receivable", cm.Class, 0, cm.Total, cm.Memo);
                    foreach (var l in cm.Lines) Row(cm.Date, "Credit Memo", cm.RefNumber, cm.Customer, ItemAccount(plan, l.Item), cm.Class, l.Amount, 0, l.Description);
                    break;
                case ReceivePayment p:
                    Row(p.Date, "Payment", p.RefNumber, p.Customer, p.DepositToAccount, "", p.TotalAmount, 0, "");
                    Row(p.Date, "Payment", p.RefNumber, p.Customer, p.ARAccount ?? "Accounts Receivable", "", 0, p.TotalAmount, "");
                    break;
            }
        return sb.ToString();
    }

    // ---- QBXML ----

    /// <summary>
    /// A QBXML file of the add requests, in posting order (list objects, then transactions), without the look-ups the poster does.
    /// Parent accounts are listed before children. Account types come from the package. For tools that send QBXML.
    /// </summary>
    public static XDocument Qbxml(PostingPlan plan, string version = "6.0")
    {
        var msgs = new XElement("QBXMLMsgsRq", new XAttribute("onError", "continueOnError"));
        foreach (var o in plan.Objects.DistinctBy(o => $"{o.Kind}|{o.FullName}"))
            foreach (var s in QbxmlRequests.StepsFor(o))
            {
                var rq = new XElement(s.Create.Element("QBXMLMsgsRq")!.Elements().First());
                if (o.Kind == ObjectKind.Account) rq.Elements().First().Add(new XElement("AccountType", o.DefaultAccountType ?? "Bank"));
                msgs.Add(rq);
            }
        foreach (var t in plan.Transactions)
            msgs.Add(new XElement(QbxmlRequests.Add(t).Element("QBXMLMsgsRq")!.Elements().First()));
        return new XDocument(new XProcessingInstruction("qbxml", $"version=\"{version}\""), new XElement("QBXML", msgs));
    }

    /// <summary>Writes all three files into a folder and returns their paths.</summary>
    public static IReadOnlyList<string> WriteAll(PostingPlan plan, string folder, string baseName)
    {
        Directory.CreateDirectory(folder);
        var iif = Path.Combine(folder, baseName + ".iif");
        var csv = Path.Combine(folder, baseName + ".csv");
        var xml = Path.Combine(folder, baseName + ".qbxml");
        File.WriteAllText(iif, Iif(plan), new UTF8Encoding(false));
        File.WriteAllText(csv, Csv(plan), new UTF8Encoding(true));
        File.WriteAllText(xml, "<?xml version=\"1.0\" ?>\r\n" + Qbxml(plan).ToString(), new UTF8Encoding(false));
        return new[] { iif, csv, xml };
    }
}
