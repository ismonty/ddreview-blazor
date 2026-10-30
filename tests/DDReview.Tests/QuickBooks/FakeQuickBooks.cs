using System.Xml.Linq;
using DDReview.QuickBooks.Posting;

namespace DDReview.Tests.QuickBooks;

/// <summary>
/// A small in-memory stand-in for QuickBooks that answers the QBXML requests the poster makes the way QuickBooks does
/// (status 500 for a list name it cannot find, status 1 for a transaction query with no match, status 3140 for an add that
/// refers to something that does not exist), so the poster can be tested end to end without QuickBooks.
/// </summary>
public sealed class FakeQuickBooks : IQbRequestProcessor
{
    sealed class Obj { public string Kind = ""; public string FullName = ""; public XElement Ret = null!; }

    readonly List<Obj> _lists = new();
    readonly List<Obj> _txns = new();
    int _seq = 1000;

    public List<string> Requests { get; } = new();
    public int Sent(string rq) => Requests.Count(r => r.Contains("<" + rq + ">") || r.Contains("<" + rq + " "));

    public void Dispose() { }

    static readonly string[] ListKinds = { "Vendor", "Account", "Class", "ItemService", "Customer", "CustomerType", "ItemSalesTax" };
    static readonly string[] TxnKinds = { "JournalEntry", "Invoice", "ReceivePayment", "CreditMemo" };

    public IEnumerable<string> ListNames(string kind) => _lists.Where(o => o.Kind == kind).Select(o => o.FullName);
    public IEnumerable<XElement> Transactions(string kind) => _txns.Where(o => o.Kind == kind).Select(o => o.Ret);
    public XElement? Find(string kind, string fullName) => _lists.FirstOrDefault(o => o.Kind == kind && o.FullName == fullName)?.Ret;
    public void Seed(string kind, string fullName, string? accountType = null)
    {
        var parts = fullName.Split(':');
        var add = new XElement(kind + "Add", new XElement("Name", parts[^1]));
        if (parts.Length > 1) add.Add(new XElement("ParentRef", new XElement("FullName", string.Join(":", parts[..^1]))));
        if (accountType != null) add.Add(new XElement("AccountType", accountType));
        _lists.Add(MakeList(kind, add));
    }

    string NextId() => (++_seq).ToString("X") + "-1793304628";

    Obj MakeList(string kind, XElement add)
    {
        var name = add.Element("Name")!.Value;
        var parent = add.Element("ParentRef")?.Element("FullName")?.Value;
        var full = parent != null ? parent + ":" + name : name;
        var ret = new XElement(kind + "Ret", new XElement("ListID", NextId()), new XElement("EditSequence", NextId()));
        foreach (var e in add.Elements()) ret.Add(new XElement(e));
        ret.Add(new XElement("FullName", full));
        return new Obj { Kind = kind, FullName = full, Ret = ret };
    }

    static string Response(string name, int code, string msg, params XElement[] rets)
    {
        var rs = new XElement(name, new XAttribute("statusCode", code), new XAttribute("statusSeverity", code == 0 ? "Info" : "Error"),
            new XAttribute("statusMessage", msg), rets);
        return new XElement("QBXML", new XElement("QBXMLMsgsRs", rs)).ToString(SaveOptions.DisableFormatting);
    }

    public string Send(string doc)
    {
        Requests.Add(doc);
        var x = XDocument.Parse(doc.Substring(doc.IndexOf("<QBXML>", StringComparison.Ordinal)));
        var rq = x.Root!.Element("QBXMLMsgsRq")!.Elements().First();
        var n = rq.Name.LocalName;
        var rsName = n[..^2] + "Rs";
        string kind = ListKinds.Concat(TxnKinds).First(k => n.StartsWith(k, StringComparison.Ordinal) && n.Length > k.Length && char.IsUpper(n[k.Length]));
        string verb = n[kind.Length..^2];
        bool txn = TxnKinds.Contains(kind);
        return (verb, txn) switch
        {
            ("Query", false) => QueryList(rq, kind, rsName),
            ("Query", true) => QueryTxn(rq, kind, rsName),
            ("Add", false) => AddList(rq.Elements().First(), kind, rsName),
            ("Add", true) => AddTxn(rq.Elements().First(), kind, rsName),
            ("Mod", false) => ModList(rq.Elements().First(), kind, rsName),
            ("Mod", true) => ModTxn(rq.Elements().First(), kind, rsName),
            _ => Response(rsName, 9999, "unsupported"),
        };
    }

    string QueryList(XElement rq, string kind, string rsName)
    {
        var name = rq.Element("FullName")!.Value;
        var o = _lists.FirstOrDefault(l => l.Kind == kind && l.FullName == name);
        if (o == null) return Response(rsName, 500, $"The query request has not been fully completed. There was a required element (\"{name}\") that could not be found in QuickBooks.");
        var want = rq.Elements("IncludeRetElement").Select(e => e.Value).ToList();
        var ret = want.Count == 0 ? o.Ret : new XElement(o.Ret.Name, o.Ret.Elements().Where(e => want.Contains(e.Name.LocalName)));
        return Response(rsName, 0, "Status OK", ret);
    }

    string QueryTxn(XElement rq, string kind, string rsName)
    {
        var range = rq.Element("RefNumberRangeFilter");
        var from = range?.Element("FromRefNumber")?.Value;
        var entity = rq.Element("EntityFilter")?.Element("FullName")?.Value;
        var hits = _txns.Where(t => t.Kind == kind && (from == null || t.Ret.Element("RefNumber")?.Value == from)
            && (entity == null || t.Ret.Element("CustomerRef")?.Element("FullName")?.Value == entity)).Select(t => t.Ret).ToArray();
        return hits.Length == 0 ? Response(rsName, 1, "A query request did not find a matching object in QuickBooks") : Response(rsName, 0, "Status OK", hits);
    }

    static readonly Dictionary<string, string> RefKinds = new()
    {
        ["AccountRef"] = "Account", ["ARAccountRef"] = "Account", ["DepositToAccountRef"] = "Account",
        ["CustomerRef"] = "Customer", ["ClassRef"] = "Class", ["ItemRef"] = "ItemService", ["ItemSalesTaxRef"] = "ItemSalesTax",
        ["CustomerTypeRef"] = "CustomerType",
    };

    string? BadRef(XElement add, string ownKind)
    {
        foreach (var r in add.Descendants().Where(e => e.Name.LocalName.EndsWith("Ref", StringComparison.Ordinal)))
        {
            var nm = r.Element("FullName")?.Value;
            if (nm == null) continue;
            var k = r.Name.LocalName == "ParentRef" ? ownKind : r.Name.LocalName == "EntityRef" ? null : RefKinds.GetValueOrDefault(r.Name.LocalName);
            if (r.Name.LocalName == "EntityRef") { if (!_lists.Any(l => (l.Kind == "Vendor" || l.Kind == "Customer") && l.FullName == nm)) return nm; continue; }
            if (r.Name.LocalName == "ItemRef" && nm == "Purchase Details") continue;
            if (k != null && !_lists.Any(l => l.Kind == k && l.FullName == nm)) return nm;
        }
        return null;
    }

    string AddList(XElement add, string kind, string rsName)
    {
        var bad = BadRef(add, kind);
        if (bad != null) return Response(rsName, 3000, $"Invalid reference \"{bad}\"");
        var o = MakeList(kind, add);
        if (_lists.Any(l => l.Kind == kind && l.FullName == o.FullName)) return Response(rsName, 3100, "name already in use");
        if (kind == "Account" && add.Element("AccountType") == null) return Response(rsName, 3180, "AccountType required");
        _lists.Add(o);
        return Response(rsName, 0, "Status OK", o.Ret);
    }

    string ModList(XElement mod, string kind, string rsName)
    {
        var listId = mod.Element("ListID")!.Value;
        var o = _lists.FirstOrDefault(l => l.Kind == kind && l.Ret.Element("ListID")!.Value == listId);
        if (o == null) return Response(rsName, 3200, "not found");
        foreach (var e in mod.Elements().Where(e => e.Name.LocalName is not ("ListID" or "EditSequence")))
        {
            o.Ret.Element(e.Name)?.Remove();
            if (e.Name.LocalName != "ParentRef" || true) o.Ret.Add(new XElement(e));
        }
        o.Ret.Element("EditSequence")!.Value = NextId();
        return Response(rsName, 0, "Status OK", o.Ret);
    }

    string AddTxn(XElement add, string kind, string rsName)
    {
        var bad = BadRef(add, kind);
        if (bad != null) return Response(rsName, 3140, $"There is an invalid reference to a QuickBooks object. \"{bad}\"");
        var ret = new XElement(kind + "Ret", new XElement("TxnID", NextId()), new XElement("EditSequence", NextId()));
        foreach (var e in add.Elements())
        {
            var ln = e.Name.LocalName;
            if (ln.EndsWith("LineAdd", StringComparison.Ordinal))
            {
                var line = new XElement(ln.Replace("LineAdd", "LineRet"), new XElement("TxnLineID", NextId()));
                foreach (var c in e.Elements()) line.Add(new XElement(c));
                ret.Add(line);
            }
            else if (ln is "JournalDebitLine" or "JournalCreditLine")
            {
                var line = new XElement(ln, new XElement("TxnLineID", NextId()));
                foreach (var c in e.Elements()) line.Add(new XElement(c));
                ret.Add(line);
            }
            else if (ln == "Memo" && e.Value.Length == 0) { }
            else if (ln != "IsAutoApply") ret.Add(new XElement(e));
        }
        _txns.Add(new Obj { Kind = kind, FullName = ret.Element("RefNumber")?.Value ?? "", Ret = ret });
        return Response(rsName, 0, "Status OK", ret);
    }

    string ModTxn(XElement mod, string kind, string rsName)
    {
        var id = mod.Element("TxnID")!.Value;
        var o = _txns.FirstOrDefault(t => t.Kind == kind && t.Ret.Element("TxnID")!.Value == id);
        if (o == null) return Response(rsName, 3200, "txn not found");
        if (o.Ret.Element("EditSequence")!.Value != mod.Element("EditSequence")!.Value) return Response(rsName, 3200, "edit sequence out of date");
        var bad = BadRef(mod, kind);
        if (bad != null) return Response(rsName, 3140, $"invalid reference \"{bad}\"");
        var ret = new XElement(o.Ret.Name, new XElement("TxnID", id), new XElement("EditSequence", NextId()));
        foreach (var e in mod.Elements().Where(e => e.Name.LocalName is not ("TxnID" or "EditSequence")))
        {
            var ln = e.Name.LocalName;
            if (ln.EndsWith("LineMod", StringComparison.Ordinal))
            {
                var typ = e.Element("JournalLineType")?.Value;
                var name = typ != null ? "Journal" + typ + "Line" : ln.Replace("LineMod", "LineRet");
                var lid = e.Element("TxnLineID")!.Value;
                var line = new XElement(name, new XElement("TxnLineID", lid == "-1" ? NextId() : lid));
                foreach (var c in e.Elements().Where(c => c.Name.LocalName is not ("TxnLineID" or "JournalLineType"))) line.Add(new XElement(c));
                ret.Add(line);
            }
            else if (ln == "Memo" && e.Value.Length == 0) { }
            else ret.Add(new XElement(e));
        }
        o.Ret = ret;
        return Response(rsName, 0, "Status OK", ret);
    }
}
