using System.Xml.Linq;
using DDReview.QuickBooks.Model;

namespace DDReview.QuickBooks.Qbxml;

public enum CheckMode
{
    /// <summary>Create it if it is not there; leave it alone if it is.</summary>
    AddIfMissing,
    /// <summary>Create it if it is not there; compare and modify if it is.</summary>
    AddOrModify,
}

/// <summary>One look-up-then-create(-or-modify) step: the query, the add, and (for AddOrModify) the modify, as QBXML requests.</summary>
public sealed record CheckStep(CheckMode Mode, XElement Query, XElement Create, XElement? Modify);

/// <summary>Builds the QBXML requests. Names are put in as text; XML escaping is done by the XML library.</summary>
public static class QbxmlRequests
{
    public static XElement Envelope(XElement request) =>
        new("QBXML", new XElement("QBXMLMsgsRq", new XAttribute("onError", "stopOnError"), request));

    public static XElement T(string name, string value) => new(name, value);
    public static XElement Ref(string name, string fullName, string? listId = null)
    {
        var e = new XElement(name);
        if (!string.IsNullOrWhiteSpace(listId)) e.Add(new XElement("ListID", listId));
        if (!string.IsNullOrWhiteSpace(fullName)) e.Add(new XElement("FullName", fullName));
        return e;
    }

    static XElement Rq(string name, params object?[] content) => Envelope(new XElement(name, content));

    // ---- list objects ----

    public static List<CheckStep> StepsFor(ObjectRequirement o) => o.Kind switch
    {
        ObjectKind.Vendor => new() { VendorStep(o.FullName) },
        ObjectKind.Account => AccountSteps(o.FullName),
        ObjectKind.Class => ClassSteps(o.FullName),
        ObjectKind.ItemService => ItemServiceSteps(o),
        ObjectKind.Customer => CustomerSteps(o),
        ObjectKind.CustomerType => CustomerTypeSteps(o.FullName),
        ObjectKind.ItemSalesTax => new() { ItemSalesTaxStep() },
        _ => throw new ArgumentOutOfRangeException(nameof(o)),
    };

    static CheckStep VendorStep(string name) => new(CheckMode.AddIfMissing,
        Rq("VendorQueryRq", T("FullName", name)),
        Rq("VendorAddRq", new XElement("VendorAdd", T("Name", name))), null);

    static CheckStep ItemSalesTaxStep() => new(CheckMode.AddIfMissing,
        Rq("ItemSalesTaxQueryRq", T("FullName", "Non")),
        Rq("ItemSalesTaxAddRq", new XElement("ItemSalesTaxAdd", T("Name", "Non"))), null);

    /// <summary>Accounts: "a number is a name": a bracketed account number after '[' is dropped from the name, as before.</summary>
    static List<CheckStep> AccountSteps(string fullName)
    {
        var steps = new List<CheckStep>();
        string parent = "";
        foreach (var raw in fullName.Split(':'))
        {
            var part = raw;
            int br = part.IndexOf('[');
            if (br >= 0) part = part[..br].Trim();
            part = QbText.TrimSpacesAroundColons(part);
            var full = parent.Length > 0 ? parent + ":" + part : part;
            steps.Add(new CheckStep(CheckMode.AddIfMissing,
                Rq("AccountQueryRq", T("FullName", full)),
                Rq("AccountAddRq", new XElement("AccountAdd", T("Name", part),
                    parent.Length > 0 ? new XElement("ParentRef", T("FullName", parent)) : null)), null));
            parent = full;
        }
        return steps;
    }

    static List<CheckStep> ClassSteps(string fullName) =>
        QbText.Chain(fullName).Select(c => new CheckStep(CheckMode.AddIfMissing,
            Rq("ClassQueryRq", T("FullName", c.FullName)),
            Rq("ClassAddRq", new XElement("ClassAdd", T("Name", c.Name),
                c.Parent.Length > 0 ? new XElement("ParentRef", T("FullName", c.Parent)) : null)), null)).ToList();

    static List<CheckStep> ItemServiceSteps(ObjectRequirement o)
    {
        // The sales tax code on items comes from the same setting as on invoices; it is passed in the requirement's CustomerType-free
        // way by the poster (see Poster), so here only the account is used.
        return QbText.Chain(o.FullName).Select(c => new CheckStep(CheckMode.AddIfMissing,
            Rq("ItemServiceQueryRq", T("FullName", c.FullName)),
            Rq("ItemServiceAddRq", new XElement("ItemServiceAdd",
                T("Name", c.Name),
                c.Parent.Length > 0 ? new XElement("ParentRef", T("FullName", c.Parent)) : null,
                new XElement("SalesOrPurchase", T("Price", "0.00"),
                    new XElement("AccountRef", T("FullName", o.ItemAccount ?? ""))))), null)).ToList();
    }

    // Customers carry club-minimum figures in unused fields: AltPhone (expected), AltContact (remaining), ResaleNumber (spent),
    // and the spent figure again in Fax for display. These field names are what DDAdminCS and existing QuickBooks files use.
    static List<CheckStep> CustomerSteps(ObjectRequirement o)
    {
        string acct = o.AccountNumber ?? "";
        var steps = new List<CheckStep>();
        var chain = QbText.Chain(o.FullName);
        for (int i = 0; i < chain.Count; i++)
        {
            var (parent, name, full) = chain[i];
            string bill1 = name;
            if (acct.Trim().Length > 0 && bill1.EndsWith(acct.Trim(), StringComparison.Ordinal))
                bill1 = bill1[..^acct.Trim().Length].Trim();
            bool hasMin = !string.IsNullOrEmpty(o.MinimumExpected);
            var query = Rq("CustomerQueryRq",
                T("FullName", full),
                T("IncludeRetElement", "ListID"), T("IncludeRetElement", "EditSequence"),
                T("IncludeRetElement", "Name"), T("IncludeRetElement", "ParentRef"), T("IncludeRetElement", "FullName"),
                T("IncludeRetElement", "AltPhone"), T("IncludeRetElement", "ResaleNumber"), T("IncludeRetElement", "AltContact"),
                T("IncludeRetElement", "Fax"), T("IncludeRetElement", "CustomerTypeRef"));
            var add = Rq("CustomerAddRq", new XElement("CustomerAdd",
                T("Name", name), T("IsActive", "1"),
                parent.Length > 0 ? new XElement("ParentRef", T("FullName", parent)) : null,
                new XElement("BillAddress", T("Addr1", bill1)),
                hasMin ? T("AltPhone", o.MinimumExpected!) : null,
                hasMin ? T("Fax", o.MinimumSpent ?? "") : null,
                hasMin ? T("AltContact", o.MinimumRemaining ?? "") : null,
                !string.IsNullOrEmpty(o.CustomerType) ? new XElement("CustomerTypeRef", T("FullName", o.CustomerType!)) : null,
                hasMin ? T("ResaleNumber", o.MinimumSpent ?? "") : null,
                T("AccountNumber", acct)));
            var mod = Rq("CustomerModRq", new XElement("CustomerMod",
                T("Name", name), T("IsActive", "1"),
                parent.Length > 0 ? new XElement("ParentRef", T("FullName", parent)) : null,
                new XElement("BillAddress", T("Addr1", bill1)),
                T("AltPhone", o.MinimumExpected ?? ""),
                T("AltContact", o.MinimumRemaining ?? ""),
                !string.IsNullOrEmpty(o.CustomerType) ? new XElement("CustomerTypeRef", T("FullName", o.CustomerType!)) : null,
                T("ResaleNumber", o.MinimumSpent ?? ""),
                T("AccountNumber", acct)));
            steps.Add(new CheckStep(CheckMode.AddOrModify, query, add, mod));
        }
        return steps;
    }

    static List<CheckStep> CustomerTypeSteps(string fullName) =>
        QbText.Chain(fullName).Select(c => new CheckStep(CheckMode.AddOrModify,
            Rq("CustomerTypeQueryRq", T("FullName", c.FullName),
                T("IncludeRetElement", "ListID"), T("IncludeRetElement", "EditSequence"),
                T("IncludeRetElement", "Name"), T("IncludeRetElement", "FullName")),
            Rq("CustomerTypeAddRq", new XElement("CustomerTypeAdd", T("Name", c.Name), T("IsActive", "1"),
                c.Parent.Length > 0 ? new XElement("ParentRef", T("FullName", c.Parent)) : null)),
            Rq("CustomerTypeModRq", new XElement("CustomerTypeMod", T("Name", c.Name), T("IsActive", "1"),
                c.Parent.Length > 0 ? new XElement("ParentRef", T("FullName", c.Parent)) : null)))).ToList();

    // ---- transactions ----

    public static CheckStep StepFor(QbTransaction t) => new(CheckMode.AddOrModify, Query(t), Add(t), Mod(t));

    static string D(DateTime d) => d.ToString("yyyy-MM-dd");

    public static XElement Query(QbTransaction t) => t switch
    {
        JournalEntry je => Rq("JournalEntryQueryRq",
            new XElement("RefNumberRangeFilter", T("FromRefNumber", je.RefNumber), T("ToRefNumber", je.RefNumber)),
            T("IncludeLineItems", "true")),
        Invoice i => Rq("InvoiceQueryRq",
            new XElement("TxnDateRangeFilter", T("FromTxnDate", D(i.Date)), T("ToTxnDate", D(i.Date))),
            new XElement("RefNumberRangeFilter", T("FromRefNumber", i.RefNumber), T("ToRefNumber", i.RefNumber)),
            T("IncludeLineItems", "true")),
        ReceivePayment p => Rq("ReceivePaymentQueryRq",
            new XElement("EntityFilter", T("FullName", p.Customer)),
            new XElement("RefNumberRangeFilter", T("FromRefNumber", p.RefNumber), T("ToRefNumber", p.RefNumber)),
            T("IncludeLineItems", "true")),
        CreditMemo c => Rq("CreditMemoQueryRq",
            new XElement("EntityFilter", T("FullName", c.Customer)),
            new XElement("RefNumberRangeFilter", T("FromRefNumber", c.RefNumber), T("ToRefNumber", c.RefNumber)),
            T("IncludeLineItems", "true")),
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    public static XElement Add(QbTransaction t) => Body(t, modify: false);
    public static XElement Mod(QbTransaction t) => Body(t, modify: true);

    static XElement Body(QbTransaction t, bool modify)
    {
        string suffix = modify ? "Mod" : "Add";
        object[] ids = modify ? new object[] { new XElement("TxnID"), new XElement("EditSequence") } : Array.Empty<object>();
        switch (t)
        {
            case JournalEntry je:
                return Rq($"JournalEntry{suffix}Rq", new XElement($"JournalEntry{suffix}", ids,
                    T("TxnDate", D(je.Date)),
                    string.IsNullOrWhiteSpace(je.RefNumber) ? null : T("RefNumber", je.RefNumber),
                    je.Lines.Select(l => JournalLineXml(l, modify))));
            case Invoice i:
                return Rq($"Invoice{suffix}Rq", new XElement($"Invoice{suffix}", ids,
                    Ref("CustomerRef", i.Customer),
                    string.IsNullOrWhiteSpace(i.Class) ? null : Ref("ClassRef", i.Class!),
                    string.IsNullOrWhiteSpace(i.ARAccount) ? null : Ref("ARAccountRef", QbText.TrimSpacesAroundColons(i.ARAccount!)),
                    T("TxnDate", D(i.Date)),
                    string.IsNullOrWhiteSpace(i.RefNumber) ? null : T("RefNumber", i.RefNumber),
                    string.IsNullOrWhiteSpace(i.SalesTaxCode) ? null : Ref("ItemSalesTaxRef", i.SalesTaxCode!),
                    i.ClearMemo ? new XElement("Memo") : string.IsNullOrWhiteSpace(i.Memo) ? null : T("Memo", i.Memo!),
                    i.Lines.Select(l => ItemLineXml($"InvoiceLine{suffix}", l, modify))));
            case CreditMemo c:
                return Rq($"CreditMemo{suffix}Rq", new XElement($"CreditMemo{suffix}", ids,
                    Ref("CustomerRef", c.Customer),
                    string.IsNullOrWhiteSpace(c.Class) ? null : Ref("ClassRef", c.Class!),
                    string.IsNullOrWhiteSpace(c.ARAccount) ? null : Ref("ARAccountRef", QbText.TrimSpacesAroundColons(c.ARAccount!)),
                    T("TxnDate", D(c.Date)),
                    string.IsNullOrWhiteSpace(c.RefNumber) ? null : T("RefNumber", c.RefNumber),
                    c.Lines.Select(l => ItemLineXml($"CreditMemoLine{suffix}", l, modify))));
            case ReceivePayment p:
                return Rq($"ReceivePayment{suffix}Rq", new XElement($"ReceivePayment{suffix}", ids,
                    Ref("CustomerRef", p.Customer),
                    string.IsNullOrWhiteSpace(p.ARAccount) ? null : Ref("ARAccountRef", p.ARAccount!),
                    T("TxnDate", D(p.Date)),
                    string.IsNullOrWhiteSpace(p.RefNumber) ? null : T("RefNumber", p.RefNumber),
                    T("TotalAmount", QbText.Fmt(p.TotalAmount)),
                    Ref("DepositToAccountRef", p.DepositToAccount),
                    modify ? null : T("IsAutoApply", p.AutoApply ? "1" : "0")));
            default:
                throw new ArgumentOutOfRangeException(nameof(t));
        }
    }

    static XElement JournalLineXml(JournalLine l, bool modify)
    {
        var e = new XElement(modify ? "JournalLineMod" : l.IsDebit ? "JournalDebitLine" : "JournalCreditLine");
        if (modify) e.Add(new XElement("TxnLineID"), T("JournalLineType", l.IsDebit ? "Debit" : "Credit"));
        e.Add(Ref("AccountRef", l.Account), T("Amount", QbText.Fmt(l.Amount)));
        if (!string.IsNullOrWhiteSpace(l.Memo)) e.Add(T("Memo", l.Memo!));
        if (!string.IsNullOrWhiteSpace(l.Entity) || !string.IsNullOrWhiteSpace(l.EntityListId))
            e.Add(Ref("EntityRef", l.Entity ?? "", l.EntityListId));
        if (!string.IsNullOrWhiteSpace(l.Class)) e.Add(Ref("ClassRef", l.Class!));
        if (!string.IsNullOrWhiteSpace(l.SalesTaxItem) || !string.IsNullOrWhiteSpace(l.SalesTaxItemListId))
            e.Add(Ref("ItemSalesTaxRef", l.SalesTaxItem ?? "", l.SalesTaxItemListId));
        return e;
    }

    static XElement ItemLineXml(string name, ItemLine l, bool modify)
    {
        var e = new XElement(name);
        if (modify) e.Add(new XElement("TxnLineID"));
        e.Add(Ref("ItemRef", l.Item));
        if (!string.IsNullOrWhiteSpace(l.Description)) e.Add(T("Desc", l.Description!));
        e.Add(T("Amount", QbText.Fmt(l.Amount)));
        return e;
    }

    /// <summary>A request as the string sent to QuickBooks (version header + QBXML).</summary>
    public static string Document(XElement qbxml, string version) =>
        $"<?xml version=\"1.0\" ?><?qbxml version=\"{version}\"?>" + qbxml.ToString(SaveOptions.DisableFormatting);
}
