namespace DDReview.Core.Gl;

public enum GlSection { BalanceSheet, ProfitAndLoss, Unclassified }

/// <summary>One account in the Balance Sheet / Profit and Loss view: the whole period added together, with the account's QuickBooks type.</summary>
public sealed record GlFinancialLine(string Account, string AccountType, GlSection Section, decimal Debit, decimal Credit)
{
    /// <summary>Debit less credit: the change this account sees on the books.</summary>
    public decimal Net => Debit - Credit;
}

public sealed record GlFinancials(
    IReadOnlyList<GlFinancialLine> BalanceSheet, IReadOnlyList<GlFinancialLine> ProfitAndLoss, IReadOnlyList<GlFinancialLine> Unclassified)
{
    public decimal BalanceSheetDebit => BalanceSheet.Sum(l => l.Debit);
    public decimal BalanceSheetCredit => BalanceSheet.Sum(l => l.Credit);
    /// <summary>The balance sheet delta: debits less credits across the balance sheet accounts (positive = assets up more than liabilities and equity).</summary>
    public decimal BalanceSheetDelta => BalanceSheetDebit - BalanceSheetCredit;
    public decimal ProfitAndLossDebit => ProfitAndLoss.Sum(l => l.Debit);
    public decimal ProfitAndLossCredit => ProfitAndLoss.Sum(l => l.Credit);
    /// <summary>Net income for the period: income credited less costs and expenses debited.</summary>
    public decimal NetIncome => ProfitAndLossCredit - ProfitAndLossDebit;
    public decimal UnclassifiedNet => Unclassified.Sum(l => l.Net);
}

public static class GlFinancialsBuilder
{
    // The QuickBooks account types that belong on the profit and loss; every other real type is a balance sheet account.
    static readonly string[] Pnl = ["income", "otherincome", "costofgoodssold", "cogs", "costofgoods", "expense", "expenses", "otherexpense", "otherexpenses", "otherincomeandexpense"];
    static readonly string[] BalanceSheetWords = ["bank", "checking", "savings", "asset", "liabilit", "receivable", "payable", "creditcard", "equity"];
    static readonly string[] PnlOrder = ["income", "otherincome", "costofgoodssold", "expense", "otherexpense"];

    /// <summary>Which statement an account belongs on, from its QuickBooks type (spelled any of the ways DD Review holds it).</summary>
    public static GlSection SectionOf(string? qbType)
    {
        var k = Key(qbType);
        if (k.Length == 0) return GlSection.Unclassified;
        if (Array.IndexOf(Pnl, k) >= 0) return GlSection.ProfitAndLoss;
        if (k is "ar" or "ap" || BalanceSheetWords.Any(k.Contains)) return GlSection.BalanceSheet;
        return GlSection.Unclassified;
    }

    /// <summary>Adds the lines up by account over the whole period (date, batch, A/R account and reference ignored) and files each account under its type.</summary>
    public static GlFinancials Build(IEnumerable<GlLine> lines)
    {
        var rows = lines
            .GroupBy(l => (l.Account ?? "").Trim().ToUpperInvariant())
            .Select(g =>
            {
                var type = g.Select(l => l.DefaultQuickBooksType?.Trim()).FirstOrDefault(t => !string.IsNullOrEmpty(t) && t != "0") ?? "";
                return new GlFinancialLine(g.First().Account?.Trim() ?? "", type, SectionOf(type), g.Sum(l => l.Debit), g.Sum(l => l.Credit));
            })
            .Where(r => r.Debit != 0 || r.Credit != 0)
            .ToList();
        int Order(GlFinancialLine l) { var i = Array.IndexOf(PnlOrder, Key(l.AccountType)); return i < 0 ? PnlOrder.Length : i; }
        return new GlFinancials(
            rows.Where(r => r.Section == GlSection.BalanceSheet).OrderBy(r => r.AccountType, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Account, StringComparer.OrdinalIgnoreCase).ToList(),
            rows.Where(r => r.Section == GlSection.ProfitAndLoss).OrderBy(Order).ThenBy(r => r.Account, StringComparer.OrdinalIgnoreCase).ToList(),
            rows.Where(r => r.Section == GlSection.Unclassified).OrderBy(r => r.Account, StringComparer.OrdinalIgnoreCase).ToList());
    }

    static string Key(string? s) => new string((s ?? "").Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
