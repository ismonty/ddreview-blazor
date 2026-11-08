using System.Data;

namespace DDReview.Core.Receipts;

public enum CellFormat { Money, Count, Percent }

/// <summary>One line inside a stacked cell of the old Receipts grid.</summary>
public sealed record StackLine(string Label, string Column, CellFormat Format = CellFormat.Money, bool Bold = false, string? NumeratorColumn = null, string? DenominatorColumn = null);

/// <summary>A grid column that stacks several values vertically, exactly as the old page's header/cells did.</summary>
public sealed record StackColumn(string Title, IReadOnlyList<StackLine> Lines);

/// <summary>The summary grid of the old Receipts Review (the multi-day totals), taken from the live screen and checked against the live result row.</summary>
public static class ReceiptsLayout
{
    static StackLine M(string l, string c, bool b = false) => new(l, c, CellFormat.Money, b);
    static StackLine N(string l, string c) => new(l, c, CellFormat.Count);
    static StackLine P(string l, string num, string den) => new(l, "", CellFormat.Percent, false, num, den);

    public static readonly IReadOnlyList<StackColumn> Columns =
    [
        new("Expected / Variance / Counted / Deposit / Cash / Credit Card",
            [M("Expected", "TotalReceipts", true), M("Variance", "TotalOverShort"), M("Counted", "NetReceipts"), M("Deposit", "TotalDeposit"), M("Cash", "TotalDepositCash"), M("Credit Card", "TotalDepositCreditCard")]),
        new("VISA / MC / AmEx / Discover / Other",
            [M("VISA", "TotalDepositCreditCardVISA"), M("MC", "TotalDepositCreditCardMC"), M("AmEx", "TotalDepositCreditCardAmEx"), M("Discover", "TotalDepositCreditCardDiscover"), M("Other", "TotalDepositCreditCardOther")]),
        new("Swiped / EMV / OLO",
            [M("Swiped", "TotalDepositCreditCardSwiped", true), M("EMV", "TotalDepositCreditCardEMV"), M("OLO", "TotalDepositCreditCardOLO")]),
        new("Counted / GC Redeem / Non Dep / Deposit / Txbl Sales / Non Txbl",
            [M("Counted", "NetReceipts"), M("GC Redeem", "TotalGiftCardRedeemed"), M("Non Dep", "TotalOtherNonDeposit"), M("Deposit", "TotalDeposit", true), M("Txbl Sales", "TotalTaxableSales"), M("Non Txbl", "TotalNonTaxableSales")]),
        new("Collected / Tips Paid / - Paid Out / Turned In / + Paid Out / Gross Rcpt",
            [M("Collected", "TotalReceiptAmount"), M("Tips Paid", "TotalTipsPaidAmount"), M("- Paid Out", "TotalPaidOutAmount"), M("Turned In", "TotalGrossReceiptAmount", true), M("+ Paid Out", "TotalPaidOutAmountN"), M("Gross Rcpt", "TotalGrossRevenueAmount")]),
        new("Tips Withheld / Taxes / Svr Comm / GC Issued / AR Pd In / Charges",
            [M("Tips Withheld", "TotalTipsWithheldAmount"), M("Taxes", "TotalTaxAmount"), M("Svr Comm", "TotalServerCommissionAmount"), M("GC Issued", "TotalGiftCardSoldAmount"), M("AR Pd In", "TotalARPaidInAmount"), M("Charges", "TotalChargeAmount")]),
        new("Total / Food / Beverage / Other / Discounts / Net Sales",
            [M("Total", "TotalSalesAmount", true), M("Food", "TotalFood"), M("Beverage", "TotalBeverage"), M("Other", "TotalOther"), M("Discounts", "TotalDiscount"), M("Net Sales", "TotalNetReceiptAmount", true)]),
        new("Disc% / Tot Tips / Charged Tips / Cash Tips / PR Sales / Tips %",
            [P("Disc%", "TotalDiscountAmount", "TotalSalesAmount"), M("Tot Tips", "TotalTipsAmount"), M("Charged Tips", "TotalCreditCardTip"), M("Cash Tips", "TotalTipsDeclaredAmount"), M("PR Sales", "TotalPayrollSalesAmount"), P("Tips %", "TotalTipsAmount", "TotalPayrollSalesAmount")]),
        new("#Tables / #Guests / #Pmts / Bad Pmts / Chrg Sales / Chrg Tips %",
            [N("#Tables", "TotalCheckCount"), N("#Guests", "TotalDeclaredCoverCount"), N("#Pmts", "TotalCount"), M("Bad Pmts", "TotalBadPayments"), M("Chrg Sales", "TotalCreditCardSales"), P("Chrg Tips %", "TotalCreditCardTip", "TotalCreditCardSales")]),
        new("Net/Table / Net/Guest / Net/Pmt / Tip/Table / Tip/Guest / Tip/Pmt",
            [M("Net/Table", "AverageNetSalesPerTable"), M("Net/Guest", "AverageNetSalesPerGuest"), M("Net/Pmt", "AverageNetSalesPerPayment"), M("Tip/Table", "AverageTipPerTable"), M("Tip/Guest", "AverageTipGuest"), M("Tip/Pmt", "AverageTipsPerPayment")]),
    ];

    public static string Format(DataRow row, StackLine line)
    {
        if (line.Format == CellFormat.Percent)
        {
            var den = Num(row, line.DenominatorColumn!);
            return den == 0 ? "" : (Num(row, line.NumeratorColumn!) / den * 100m).ToString("N2") + "%";
        }
        var v = Num(row, line.Column);
        return line.Format == CellFormat.Count ? v.ToString("N0") : v.ToString("N2");
    }

    // Average columns are ratios of two totals, so the Total row recomputes them from the summed columns, not by adding averages.
    static readonly Dictionary<string, (string Num, string Den)> Averages = new()
    {
        ["AverageNetSalesPerTable"] = ("TotalNetReceiptAmount", "TotalCheckCount"),
        ["AverageNetSalesPerGuest"] = ("TotalNetReceiptAmount", "TotalDeclaredCoverCount"),
        ["AverageNetSalesPerPayment"] = ("TotalNetReceiptAmount", "TotalCount"),
        ["AverageTipPerTable"] = ("TotalTipsAmount", "TotalCheckCount"),
        ["AverageTipGuest"] = ("TotalTipsAmount", "TotalDeclaredCoverCount"),
        ["AverageTipsPerPayment"] = ("TotalTipsAmount", "TotalCount"),
    };

    /// <summary>Column sums over every row, used by the Total row at the bottom of the grid.</summary>
    public static Dictionary<string, decimal> Sums(DataTable t)
    {
        var sums = new Dictionary<string, decimal>();
        foreach (var name in new[] { "TotalNetReceiptAmount", "TotalCheckCount", "TotalDeclaredCoverCount", "TotalCount", "TotalTipsAmount" }
                     .Concat(Columns.SelectMany(c => c.Lines).SelectMany(l => new[] { l.Column, l.NumeratorColumn, l.DenominatorColumn }))
                     .Where(n => !string.IsNullOrEmpty(n)).Cast<string>())
            if (!sums.ContainsKey(name) && t.Columns.Contains(name))
                sums[name] = t.Rows.Cast<DataRow>().Sum(r => r[name] is DBNull ? 0m : Convert.ToDecimal(r[name]));
        return sums;
    }

    /// <summary>One line of the Total row: sums for money and counts, recomputed ratios for percent and average lines.</summary>
    public static string FormatTotal(IReadOnlyDictionary<string, decimal> sums, StackLine line)
    {
        decimal S(string c) => sums.TryGetValue(c, out var v) ? v : 0m;
        if (line.Format == CellFormat.Percent)
            return S(line.DenominatorColumn!) == 0 ? "" : (S(line.NumeratorColumn!) / S(line.DenominatorColumn!) * 100m).ToString("N2") + "%";
        if (Averages.TryGetValue(line.Column, out var a))
            return S(a.Den) == 0 ? "" : (S(a.Num) / S(a.Den)).ToString("N2");
        return line.Format == CellFormat.Count ? S(line.Column).ToString("N0") : S(line.Column).ToString("N2");
    }

    static decimal Num(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] is not DBNull ? Convert.ToDecimal(row[column]) : 0m;
}

/// <summary>One line of the old page's "Weekdays" view: a label, the Mon..Sun field (the digit 1..7 is appended), and the field of the Total column.</summary>
public sealed record WeekLine(string Label, string DayField, string TotalField, CellFormat Format = CellFormat.Money, bool Bold = false,
    string? DayDenominator = null, string? TotalDenominator = null, bool Ratio = false);

/// <summary>
/// The old Receipts grid in its default "Weekdays" view (taken from the old page code): per row a right-aligned label column, seven day columns
/// (Mon..Sun) and a Total column, with the labels across the top hidden. Lines are in the old order; ratios are the first field divided by the second.
/// </summary>
public static class ReceiptsWeekdays
{
    public static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    static WeekLine M(string l, string d, string t, bool b = false) => new(l, d, t, CellFormat.Money, b);
    static WeekLine N(string l, string d, string t) => new(l, d, t, CellFormat.Count);
    static WeekLine P(string l, string dn, string dd, string tn, string td) => new(l, dn, tn, CellFormat.Percent, false, dd, td, true);
    static WeekLine A(string l, string dn, string dd, string tn, string td) => new(l, dn, tn, CellFormat.Money, false, dd, td, true);

    public static readonly IReadOnlyList<WeekLine> Lines =
    [
        M("Expected", "R", "TotalReceipts", true), M("Variance", "V", "TotalOverShort"), M("Counted", "C", "NetReceipts"),
        M("GC Redeem", "GiftCardRedeemedAmount", "TotalGiftCardRedeemed"), M("Non Deposit", "ND", "TotalOtherNonDeposit"),
        M("Deposit", "D", "TotalDeposit"), M("Cash", "DepositCash", "TotalDepositCash"), M("Credit Card", "DepositCreditCard", "TotalDepositCreditCard"),
        M("VISA", "DepositCreditCardVISA", "TotalDepositCreditCardVISA"), M("MC", "DepositCreditCardMC", "TotalDepositCreditCardMC"),
        M("AmEx", "DepositCreditCardAmEx", "TotalDepositCreditCardAmEx"), M("Discover", "DepositCreditCardDiscover", "TotalDepositCreditCardDiscover"),
        M("Other", "DepositCreditCardOther", "TotalDepositCreditCardOther"), M("Swiped", "DepositCreditCardSwiped", "TotalDepositCreditCardSwiped"),
        M("EMV", "DepositCreditCardEMV", "TotalDepositCreditCardEMV"), M("OLO", "DepositCreditCardOLO", "TotalDepositCreditCardOLO"),
        M("Txbl Sales", "TaxableSales", "TotalTaxableSales"), M("Non Txbl", "NonTaxableSales", "TotalNonTaxableSales"),
        M("Collected", "ReceiptAmount", "TotalReceiptAmount"), M("CC Tips Paid", "TipsPaidAmount", "TotalTipsPaidAmount"),
        M("Paid Out", "PaidOutAmount", "TotalPaidOutAmount"), M("Net Turn In", "GrossReceiptAmount", "TotalGrossReceiptAmount", true),
        M("Tips Withheld", "TipsWithheldAmount", "TotalTipsWithheldAmount"), M("Taxes", "TaxAmount", "TotalTaxAmount"),
        M("Svr Comm", "ServerCommissionAmount", "TotalServerCommissionAmount"), M("GC Issued", "GiftCardSoldAmount", "TotalGiftCardSoldAmount"),
        M("AR Pd In", "ARPaidInAmount", "TotalARPaidInAmount"), M("Charges", "ChargeAmount", "TotalChargeAmount"),
        M("Paid Out", "PaidOutAmount#N", "TotalPaidOutAmountN"), M("Net Sales", "NetReceiptAmount", "TotalNetReceiptAmount", true),
        M("Discounts", "DiscountAmount", "TotalDiscountAmount"), M("Total Sales", "TotalSales", "TotalSalesAmount", true),
        P("Discounts %", "DiscountAmount", "TotalSales", "TotalDiscountAmount", "TotalSalesAmount"),
        M("Tips Rec'd", "TipsAmount", "TotalTipsAmount"), M("Charged Tips", "TipsReceivedAmount", "TotalTipsReceivedAmount"),
        M("Cash Tips", "TipsDeclaredAmount", "TotalTipsDeclaredAmount"), M("Payroll Sales", "PayrollSalesAmount", "TotalPayrollSalesAmount"),
        P("Payroll Tips %", "TipsAmount", "PayrollSalesAmount", "TotalTipsAmount", "TotalPayrollSalesAmount"),
        M("C.C. Sales", "CreditCardSalesAmount", "TotalCreditCardSales"),
        P("C.C. Tip%", "CreditCardTipAmount", "CreditCardSalesAmount", "TotalCreditCardTip", "TotalCreditCardSales"),
        N("# Payments", "Q", "TotalCount"), N("# Tables", "CheckCount", "TotalCheckCount"), N("# Guests", "DeclaredCoverCount", "TotalDeclaredCoverCount"),
        M("Bad Pmts", "BadPayments", "TotalBadPayments"),
        A("Net/Table", "NetReceiptAmount", "CheckCount", "TotalNetReceiptAmount", "TotalCheckCount"),
        A("Net/Guest", "NetReceiptAmount", "DeclaredCoverCount", "TotalNetReceiptAmount", "TotalDeclaredCoverCount"),
        A("Net/Payment", "NetReceiptAmount", "Q", "TotalNetReceiptAmount", "TotalCount"),
        A("Tips/Table", "TipsAmount", "CheckCount", "TotalTipsAmount", "TotalCheckCount"),
        A("Tips/Guest", "TipsAmount", "DeclaredCoverCount", "TotalTipsAmount", "TotalDeclaredCoverCount"),
        A("Tips/Payment", "TipsAmount", "Q", "TotalTipsAmount", "TotalCount"),
    ];

    /// <summary>The field name for day <paramref name="day"/> (0 = Monday): the digit goes at the end, or in place of '#' when the name has one.</summary>
    public static string DayName(string field, int day) => field.Contains('#') ? field.Replace("#", (day + 1).ToString()) : field + (day + 1);

    /// <summary>Every field a line reads, so the Total row can sum them first.</summary>
    public static IEnumerable<string> Fields(WeekLine l)
    {
        for (int d = 0; d < 7; d++)
        {
            yield return DayName(l.DayField, d);
            if (l.DayDenominator is not null) yield return DayName(l.DayDenominator, d);
        }
        yield return l.TotalField;
        if (l.TotalDenominator is not null) yield return l.TotalDenominator;
    }

    // TotalSales# and TipsAmount# are not stored in the result; the old page derived them the same way (Net + Discount, Tips received + declared).
    static decimal Raw(DataRow row, string c) => row.Table.Columns.Contains(c) && row[c] is not DBNull ? Convert.ToDecimal(row[c]) : 0m;
    public static decimal Value(DataRow row, string c)
    {
        if (row.Table.Columns.Contains(c)) return Raw(row, c);
        if (c.Length == 11 && c.StartsWith("TotalSales")) { var d = c[^1]; return Raw(row, "NetReceiptAmount" + d) + Raw(row, "DiscountAmount" + d); }
        if (c.Length == 11 && c.StartsWith("TipsAmount")) { var d = c[^1]; return Raw(row, "TipsReceivedAmount" + d) + Raw(row, "TipsDeclaredAmount" + d); }
        return 0m;
    }

    public static Dictionary<string, decimal> Sums(DataTable t)
    {
        var sums = new Dictionary<string, decimal>();
        foreach (var f in Lines.SelectMany(Fields).Distinct())
            sums[f] = t.Rows.Cast<DataRow>().Sum(r => Value(r, f));
        return sums;
    }

    /// <summary>Text for one cell: <paramref name="day"/> 0..6 is a weekday, 7 is the Total column.</summary>
    public static string Cell(Func<string, decimal> get, WeekLine l, int day)
    {
        var num = day == 7 ? l.TotalField : DayName(l.DayField, day);
        var den = day == 7 ? l.TotalDenominator : l.DayDenominator is null ? null : DayName(l.DayDenominator, day);
        if (l.Ratio)
        {
            var dv = get(den!);
            if (l.Format == CellFormat.Percent) return dv == 0 ? "" : (get(num) / dv * 100m).ToString("N2") + "%";
            return dv == 0 ? "0.00" : (get(num) / dv).ToString("N2");
        }
        return l.Format == CellFormat.Count ? get(num).ToString("N0") : get(num).ToString("N2");
    }

    public static string Cell(DataRow row, WeekLine l, int day) => Cell(c => Value(row, c), l, day);
    public static string Cell(IReadOnlyDictionary<string, decimal> sums, WeekLine l, int day) => Cell(c => sums.TryGetValue(c, out var v) ? v : 0m, l, day);
}

public static class ReceiptsExport
{
    /// <summary>Flattens the stacked cells into one Excel column per line ("Title: Label"); leading columns come first.</summary>
    public static (List<string> Headers, List<object?[]> Rows) Build(DataTable t, IReadOnlyList<(string Label, string Column)> leading)
    {
        var lines = ReceiptsLayout.Columns.SelectMany(c => c.Lines).ToList();
        var headers = leading.Select(l => l.Label).Concat(lines.Select(l => l.Label)).ToList();
        var rows = t.Rows.Cast<DataRow>().Select(r =>
            leading.Select(l => r.Table.Columns.Contains(l.Column) && r[l.Column] is not DBNull ? r[l.Column] : null)
                .Concat(lines.Select(l => l.Format == CellFormat.Percent ? (object?)ReceiptsLayout.Format(r, l)
                    : r.Table.Columns.Contains(l.Column) && r[l.Column] is not DBNull ? Convert.ToDecimal(r[l.Column]) : (object?)null))
                .ToArray()).ToList();
        return (headers, rows);
    }
}
