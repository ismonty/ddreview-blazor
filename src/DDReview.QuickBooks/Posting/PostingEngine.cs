using System.Xml.Linq;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Qbxml;

namespace DDReview.QuickBooks.Posting;

public sealed class EngineOptions
{
    /// <summary>QBXML version for queries and list-object requests (DDAdminCS used 4.0).</summary>
    public string QueryVersion { get; set; } = "4.0";
    /// <summary>QBXML version for transactions (DDAdminCS used 6.0).</summary>
    public string TransactionVersion { get; set; } = "6.0";
    /// <summary>QBXML version for journal lines that carry a sales tax item (DDAdminCS used 8.0, for Canada).</summary>
    public string TaxVersion { get; set; } = "8.0";
    /// <summary>Highest version to ever send (DDAdminCS: registry "QuickBooks QBXML Max Version"). QuickBooks Desktop 2019 takes up to 14.0.</summary>
    public string MaxVersion { get; set; } = "14.0";
}

public enum StepOutcome { Created, Modified, Unchanged, Failed }

public sealed record StepResult(string What, StepOutcome Outcome, string? Message = null);

/// <summary>What a run did (or, with commit off, would do).</summary>
public sealed class PostingReport
{
    public bool Committed { get; init; }
    public List<StepResult> Objects { get; } = new();
    public List<StepResult> Transactions { get; } = new();
    public IEnumerable<StepResult> Added => Transactions.Where(t => t.Outcome == StepOutcome.Created);
    public IEnumerable<StepResult> Modified => Transactions.Where(t => t.Outcome == StepOutcome.Modified);
    public IEnumerable<StepResult> Unchanged => Transactions.Where(t => t.Outcome == StepOutcome.Unchanged);
    public IEnumerable<StepResult> Failures => Objects.Concat(Transactions).Where(t => t.Outcome == StepOutcome.Failed);
    public bool HasFailures => Failures.Any();

    /// <summary>The summary line DDAdminCS showed (TransactionMessage).</summary>
    public string Summary =>
        Committed
            ? $"{Added.Count()} Transactions Were Added, {Modified.Count()} Existing Transactions Were Modified, and {Unchanged.Count()} Transactions Were Left Unchanged In QuickBooks."
            : $"{Added.Count()} Transactions Will Be Added, {Modified.Count()} Existing Transactions Will Be Modified, {Unchanged.Count()} Transactions Will Be Left Unchanged In QuickBooks.";
}

/// <summary>
/// Runs a PostingPlan against QuickBooks: make sure every list object exists, then add each transaction, or modify it
/// if QuickBooks already has one with the same reference number and it differs. With commit off nothing is changed:
/// QuickBooks is only asked questions.
/// </summary>
public sealed class PostingEngine
{
    readonly IQbRequestProcessor _qb;
    readonly EngineOptions _opt;

    public PostingEngine(IQbRequestProcessor qb, EngineOptions? options = null)
    {
        _qb = qb;
        _opt = options ?? new EngineOptions();
    }

    static string Cap(string wanted, string max) =>
        double.Parse(wanted, System.Globalization.CultureInfo.InvariantCulture) > double.Parse(max, System.Globalization.CultureInfo.InvariantCulture) ? max : wanted;

    QbResponse Send(XElement request, string version) =>
        QbResponse.Parse(_qb.Send(QbxmlRequests.Document(request, Cap(version, _opt.MaxVersion))));

    static XElement Body(XElement request) => request.Descendants().First(e => e.Name.LocalName.EndsWith("Rq", StringComparison.Ordinal) && e.Parent?.Name == "QBXMLMsgsRq").Elements().First();

    static string Describe(ObjectRequirement o) => $"{o.Kind} {o.FullName}";

    /// <summary>Called with a line of text as each list object and transaction is worked on.</summary>
    public Action<string>? Progress { get; set; }

    public PostingReport Run(PostingPlan plan, bool commit, CancellationToken cancel = default)
    {
        var report = new PostingReport { Committed = commit };
        var done = new HashSet<string>();   // the package lists the same account more than once; ask once
        foreach (var o in plan.Objects)
        {
            var key = $"{o.Kind}|{o.FullName}|{o.CustomerType}|{o.AccountNumber}|{o.MinimumExpected}|{o.MinimumSpent}|{o.MinimumRemaining}";
            if (!done.Add(key)) continue;
            cancel.ThrowIfCancellationRequested();
            Progress?.Invoke(Describe(o));
            EnsureObject(o, commit, report);
        }
        foreach (var t in plan.Transactions)
        {
            cancel.ThrowIfCancellationRequested();
            Progress?.Invoke(t.CheckMessage);
            PostTransaction(t, commit, report);
        }
        return report;
    }

    void EnsureObject(ObjectRequirement o, bool commit, PostingReport report)
    {
        string? accountType = o.DefaultAccountType;       // children of an account take the type of their parent
        foreach (var step in QbxmlRequests.StepsFor(o))
        {
            var name = Body(step.Query).Element("FullName")?.Value ?? o.FullName;
            var q = Send(step.Query, _opt.QueryVersion);
            bool found = q.Ok && q.FirstRet != null;
            if (found)
            {
                if (o.Kind == ObjectKind.Account) accountType = (string?)q.FirstRet!.Element("AccountType") ?? accountType;
                if (step.Mode == CheckMode.AddOrModify && step.Modify != null && !Matching.ListObjectMatches(Body(step.Modify), q.FirstRet!))
                {
                    if (commit)
                    {
                        var mod = new XElement(step.Modify);
                        var body = Body(mod);
                        var prepared = Matching.PrepareListMod(body, q.FirstRet!);
                        body.ReplaceWith(prepared);
                        var r = Send(mod, _opt.TransactionVersion);
                        report.Objects.Add(r.Ok ? new($"{o.Kind} {name}", StepOutcome.Modified) : new($"{o.Kind} {name}", StepOutcome.Failed, r.StatusMessage));
                    }
                    else report.Objects.Add(new($"{o.Kind} {name}", StepOutcome.Modified));
                }
                continue;
            }
            if (!commit) { report.Objects.Add(new($"{o.Kind} {name}", StepOutcome.Created)); continue; }
            var create = new XElement(step.Create);
            if (o.Kind == ObjectKind.Account)
                Body(create).Add(new XElement("AccountType", accountType ?? "Bank"));
            var c = Send(create, _opt.QueryVersion);
            report.Objects.Add(c.Ok ? new($"{o.Kind} {name}", StepOutcome.Created)
                                    : new($"{o.Kind} {name}", StepOutcome.Failed, "Error Creating Object In QuickBooks. " + c.StatusMessage));
        }
    }

    string AddVersion(QbTransaction t) =>
        t is JournalEntry je && je.Lines.Any(l => l.SalesTaxItem != null || l.SalesTaxItemListId != null) ? _opt.TaxVersion : _opt.TransactionVersion;

    void PostTransaction(QbTransaction t, bool commit, PostingReport report)
    {
        var step = QbxmlRequests.StepFor(t);
        var q = Send(step.Query, _opt.QueryVersion);
        var ret = q.Ok ? q.FirstRet : null;
        if (ret == null)
        {
            if (!q.Ok && q.StatusCode != 1 && q.StatusCode != 500)
            {
                report.Transactions.Add(new(t.CheckMessage, StepOutcome.Failed, q.StatusMessage));
                return;
            }
            if (!commit) { report.Transactions.Add(new(t.CheckMessage, StepOutcome.Created)); return; }
            var a = Send(step.Create, AddVersion(t));
            report.Transactions.Add(a.Ok ? new(t.CheckMessage, StepOutcome.Created) : new(t.CheckMessage, StepOutcome.Failed, a.StatusMessage));
            return;
        }
        var modBody = Body(step.Modify!);
        if (Matching.TransactionMatches(modBody, ret))
        {
            report.Transactions.Add(new(t.CheckMessage, StepOutcome.Unchanged));
            return;
        }
        if (!commit) { report.Transactions.Add(new(t.CheckMessage, StepOutcome.Modified)); return; }
        var mod = new XElement(step.Modify!);
        var body = Body(mod);
        body.ReplaceWith(Matching.PrepareTransactionMod(body, ret));
        var m = Send(mod, _opt.TransactionVersion);
        report.Transactions.Add(m.Ok ? new(t.CheckMessage, StepOutcome.Modified) : new(t.CheckMessage, StepOutcome.Failed, m.StatusMessage));
    }
}
