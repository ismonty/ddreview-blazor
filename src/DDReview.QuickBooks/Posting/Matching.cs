using System.Globalization;
using System.Xml.Linq;

namespace DDReview.QuickBooks.Posting;

/// <summary>
/// Decides whether what QuickBooks already has equals what we would send, field by field, for the fields we send.
/// (DDAdminCS compared XML nodes and, in practice, re-sent every journal entry and payment; this compares values,
/// so an unchanged transaction is left alone.)
/// </summary>
public static class Matching
{
    static readonly HashSet<string> IdNames = new() { "TxnID", "EditSequence", "TxnLineID", "ListID", "JournalLineType" };

    static string Norm(string? s) => (s ?? "").Trim();

    static bool SameText(string a, string b)
    {
        a = Norm(a); b = Norm(b);
        if (a == b) return true;
        return decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var x)
            && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var y) && x == y;
    }

    /// <summary>One field of the request against the same field of the object QuickBooks returned.</summary>
    static bool SameField(XElement mod, XElement? ret)
    {
        if (ret == null) return Norm(mod.Value).Length == 0 && !mod.HasElements;
        if (mod.HasElements)
        {
            // a reference: compare by full name
            var mf = mod.Element("FullName");
            if (mf != null) return SameText(mf.Value, ret.Element("FullName")?.Value ?? "");
            return mod.Elements().All(c => IdNames.Contains(c.Name.LocalName) || SameField(c, ret.Element(c.Name)));
        }
        return SameText(mod.Value, ret.Value);
    }

    static bool SameFields(XElement mod, XElement ret, string? skipPrefix = null) =>
        mod.Elements().Where(e => !IdNames.Contains(e.Name.LocalName) && !e.Name.LocalName.EndsWith("LineMod", StringComparison.Ordinal))
           .All(e => SameField(e, ret.Element(e.Name)));

    /// <summary>A transaction Mod request body (e.g. InvoiceMod) against the *Ret that a query returned.</summary>
    public static bool TransactionMatches(XElement modBody, XElement ret)
    {
        if (!SameFields(modBody, ret)) return false;
        var modLines = modBody.Elements().Where(e => e.Name.LocalName.EndsWith("LineMod", StringComparison.Ordinal)).ToList();
        var retLines = ret.Elements().Where(e => e.Name.LocalName is "JournalDebitLine" or "JournalCreditLine"
            || e.Name.LocalName.EndsWith("LineRet", StringComparison.Ordinal)).ToList();
        if (modLines.Count != retLines.Count) return false;
        for (int i = 0; i < modLines.Count; i++)
        {
            var m = modLines[i];
            var r = retLines[i];
            var type = (string?)m.Element("JournalLineType");
            if (type != null && r.Name.LocalName != "Journal" + type + "Line") return false;
            if (!SameFields(m, r)) return false;
        }
        return true;
    }

    /// <summary>
    /// A list object: only the fields the query asked for can be compared. A field QuickBooks did not return is treated
    /// as the same, except a missing customer type, which means the customer needs it set.
    /// </summary>
    public static bool ListObjectMatches(XElement modBody, XElement ret)
    {
        foreach (var f in modBody.Elements())
        {
            if (IdNames.Contains(f.Name.LocalName)) continue;
            var r = ret.Element(f.Name);
            if (r == null)
            {
                if (f.Name.LocalName == "CustomerTypeRef") return false;
                continue;
            }
            if (!SameField(f, r)) return false;
        }
        return true;
    }

    /// <summary>Copies the identifying values of the existing transaction into a Mod body (TxnID, EditSequence, line ids).</summary>
    public static XElement PrepareTransactionMod(XElement modBody, XElement ret)
    {
        var body = new XElement(modBody);
        body.Element("TxnID")!.Value = (string?)ret.Element("TxnID") ?? "";
        body.Element("EditSequence")!.Value = (string?)ret.Element("EditSequence") ?? "";
        var retIds = ret.Elements().Where(e => e.Name.LocalName is "JournalDebitLine" or "JournalCreditLine"
            || e.Name.LocalName.EndsWith("LineRet", StringComparison.Ordinal)).Select(e => (string?)e.Element("TxnLineID") ?? "-1").ToList();
        int i = 0;
        foreach (var line in body.Elements().Where(e => e.Name.LocalName.EndsWith("LineMod", StringComparison.Ordinal)))
            line.Element("TxnLineID")!.Value = i < retIds.Count ? retIds[i++] : "-1";
        return body;
    }

    /// <summary>Puts ListID and EditSequence first in a list-object Mod body.</summary>
    public static XElement PrepareListMod(XElement modBody, XElement ret)
    {
        var body = new XElement(modBody);
        body.AddFirst(new XElement("ListID", (string?)ret.Element("ListID") ?? ""), new XElement("EditSequence", (string?)ret.Element("EditSequence") ?? ""));
        return body;
    }
}
