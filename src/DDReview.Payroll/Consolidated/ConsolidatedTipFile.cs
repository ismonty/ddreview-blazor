using System.Globalization;
using System.Text;
using DDReview.Payroll.Adp;

namespace DDReview.Payroll.Consolidated;

/// <summary>One row of fn_ConsolidatedTipOutADPExport3 (the columns carry the same names as the function's).</summary>
public sealed record ConsolidatedTipRow(string? CoCode, int BatchId, long FileNumber, string? BatchDescription, int? EarningsType, string? EarningsCode, decimal? EarningsAmount,
    string? MemoCode1, decimal? MemoAmount1, string? MemoCode2, decimal? MemoAmount2, string? MemoCode3, decimal? MemoAmount3, string? DeductionCode, decimal? DeductionAmount);

/// <summary>Port of the file writing in frmPayrollConsolidatedTipOutExport: one CSV for ADP with sales and tips per employee.</summary>
public static class ConsolidatedTipFile
{
    /// <summary>
    /// The header is decided by the FIRST row only (as the old code did); every row then writes the pieces it has.
    /// Memo amounts are rounded to whole dollars and a memo with a zero rounded amount is left out; the deduction is written unrounded.
    /// Returns null when there are no rows (the old export wrote no file then).
    /// </summary>
    public static AdpFile? Build(IReadOnlyList<ConsolidatedTipRow> rows, string fileName = "ConsolidatedTips.csv")
    {
        if (rows.Count == 0) return null;
        var first = rows[0];
        var sb = new StringBuilder("Co Code,Batch ID,File #,Batch Description");
        if (Has(first.EarningsCode))
        {
            if (first.EarningsType == 4) sb.Append(",Earnings 4 Code,Earnings 4 Amount");
            else if (first.EarningsType == 3) sb.Append(",Earnings 3 Code,Earnings 3 Amount");
        }
        if (Has(first.MemoCode1)) sb.Append(",Memo Code,Memo Amount");
        if (Has(first.MemoCode2)) sb.Append(",Memo Code,Memo Amount");
        if (Has(first.MemoCode3)) sb.Append(",Memo Code,Memo Amount");
        if (Has(first.DeductionCode)) sb.Append(",Adjust DED Code,Adjust DED Amount");
        sb.Append("\r\n");

        foreach (var r in rows)
        {
            sb.Append(r.CoCode ?? "").Append(',').Append(r.BatchId.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.FileNumber.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.BatchDescription ?? "");
            if (Has(r.EarningsCode) && r.EarningsType is 3 or 4)
                sb.Append(',').Append(r.EarningsCode).Append(',').Append(r.EarningsAmount is { } ea ? Vba(ea) : "");
            Memo(sb, r.MemoCode1, r.MemoAmount1); Memo(sb, r.MemoCode2, r.MemoAmount2); Memo(sb, r.MemoCode3, r.MemoAmount3);
            if (Has(r.DeductionCode) && RoundWhole(r.DeductionAmount ?? 0) != 0)
                sb.Append(',').Append(r.DeductionCode).Append(',').Append(Vba(r.DeductionAmount ?? 0));
            sb.Append("\r\n");
        }
        return new AdpFile(fileName, sb.ToString(), rows.Count);
    }

    static bool Has(string? s) => !string.IsNullOrEmpty(s);
    static decimal RoundWhole(decimal v) => Math.Floor(v + 0.5m);   // VBA Int(x + 0.5)

    static void Memo(StringBuilder sb, string? code, decimal? amount)
    {
        var whole = RoundWhole(amount ?? 0);
        if (Has(code) && whole != 0) sb.Append(',').Append(code).Append(',').Append(whole.ToString("0", CultureInfo.InvariantCulture));
    }

    /// <summary>How VBA turns a currency value into text when concatenated: up to four decimals, no trailing zeros.</summary>
    internal static string Vba(decimal v) => decimal.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
