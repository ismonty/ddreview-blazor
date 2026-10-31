using System.Xml.Linq;
using DDReview.QuickBooks.Model;

namespace DDReview.QuickBooks.Qbxml;

/// <summary>
/// Writes a plan in the same layout DDAdminCS saved as QBGeneralLedgerTransaction.xml (QBTransaction / QBObjectCheck ...),
/// so a run can be compared with what the Access program did, and kept as a debug file.
/// </summary>
public static class LegacyPlanXml
{
    public static XElement Write(PostingPlan plan)
    {
        var root = new XElement("QBTransaction");
        foreach (var o in plan.Objects) root.Add(ObjectCheck(o));
        foreach (var t in plan.Transactions) root.Add(TransactionCheck(t));
        return root;
    }

    static XElement ObjectCheck(ObjectRequirement o)
    {
        var e = new XElement("QBObjectCheck", new XElement("ObjectType", o.Kind.ToString()), new XElement("FullName", o.FullName));
        if (o.Kind == ObjectKind.Account) e.Add(new XElement("DefaultAccountType", o.DefaultAccountType ?? "Bank"));
        foreach (var s in QbxmlRequests.StepsFor(o)) e.Add(Step(s));
        return e;
    }

    static XElement TransactionCheck(QbTransaction t) =>
        new("QBObjectCheck", new XElement("ObjectType", t.Kind.ToString()), new XElement("CheckMessage", t.CheckMessage),
            Step(QbxmlRequests.StepFor(t)));

    static XElement Step(CheckStep s)
    {
        var e = new XElement(s.Mode == CheckMode.AddIfMissing ? "CheckAdd" : "CheckAddModOnExist",
            new XElement("QueryQBXML", s.Query), new XElement("CreateQBXML", s.Create));
        if (s.Modify != null) e.Add(new XElement("ModifyQBXML", s.Modify));
        return e;
    }
}
