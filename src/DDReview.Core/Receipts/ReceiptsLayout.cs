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

    static decimal Num(DataRow row, string column) =>
        row.Table.Columns.Contains(column) && row[column] is not DBNull ? Convert.ToDecimal(row[column]) : 0m;
}
