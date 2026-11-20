using System.Data;

namespace DDReview.Payroll.Adp;

/// <summary>ADP export settings (one row of the client's tblGlobal, the same columns the Access ExportADPPayroll reads).</summary>
public sealed class AdpSettings
{
    public string FileNameCode { get; init; } = "";
    public string CoCode { get; init; } = "";
    public int BatchId { get; init; }
    public string BatchDescription { get; init; } = "";
    public bool TipsAsEarnings { get; init; }
    public bool TipsIncludeChargedTips { get; init; }
    public string TipsCode { get; init; } = "";
    /// <summary>Which earnings slot (3, 4 or 5) the tips go in.</summary>
    public string TipsCodeNumber { get; init; } = "3";
    public string SalesCode { get; init; } = "";
    public bool ChargedTipsSentSeparately { get; init; }
    public bool ChargedTipsAsEarnings { get; init; }
    public string ChargedTipsCode { get; init; } = "";
    public string ChargedSalesCode { get; init; } = "";
    public string DoubleTimeCode { get; init; } = "DT";
    public string DoubleTimeCodeNumber { get; init; } = "3";
    public decimal MealAmount { get; init; }
    public string MealDeductionCode { get; init; } = "";
    /// <summary>0 = none, 1 = employee name, 2 = staff department then name, anything else = payroll department then name.</summary>
    public int SortMode { get; init; }
    public bool SsnAsPayrollId { get; init; }
    public bool DigitalDiningRates { get; init; }
    public long? PayrollIdLowerLimit { get; init; }
    public long? PayrollIdUpperLimit { get; init; }
    public string WeekNumberMemoCode { get; init; } = "";

    public bool WeekMemoOn => WeekNumberMemoCode.Trim().Length > 0;

    /// <summary>Reads the settings from `select top 1 * from tblGlobal`. Columns missing on an older schema keep their defaults (the old code ignored those errors too).</summary>
    public static AdpSettings FromRow(DataRow r)
    {
        string S(string c, string d = "") => r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToString(r[c])!.Trim() : d;
        bool B(string c) => r.Table.Columns.Contains(c) && r[c] is not DBNull && Convert.ToInt64(r[c]) != 0;
        decimal D(string c) => r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToDecimal(r[c]) : 0m;
        long? L(string c) => r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToInt64(r[c]) : null;
        var dt = S("PayrollExportADPDoubleTimeCode");
        var dtn = S("PayrollExportADPDoubleTimeCodeNumber");
        return new AdpSettings
        {
            FileNameCode = S("PayrollExportADPFileNameCode"), CoCode = S("PayrollExportADPCoCode"),
            BatchId = (int)(L("PayrollExportADPBatchID") ?? 0), BatchDescription = S("PayrollExportADPBatchDescription"),
            TipsAsEarnings = B("PayrollExportADPTipsAsEarnings"), TipsIncludeChargedTips = B("PayrollExportADPTipsIncludeChargedTips"),
            TipsCode = S("PayrollExportADPTipsCode"), TipsCodeNumber = S("PayrollExportADPTipsCodeNumber", "3"),
            SalesCode = S("PayrollExportADPSalesCode"),
            ChargedTipsSentSeparately = B("PayrollExportADPChargedTipsSentSeparately"), ChargedTipsAsEarnings = B("PayrollExportADPChargedTipsAsEarnings"),
            ChargedTipsCode = S("PayrollExportADPChargedTipsCode"), ChargedSalesCode = S("PayrollExportADPChargedSalesCode"),
            DoubleTimeCode = dt.Length == 0 ? "DT" : dt, DoubleTimeCodeNumber = dtn.Length == 0 ? "3" : dtn,
            MealAmount = D("PayrollExportADPMealAmount"), MealDeductionCode = S("PayrollExportADPMealDeductionCode"),
            SortMode = (int)(L("PayrollExportADPSortMode") ?? 0),
            SsnAsPayrollId = B("PayrollSSNasPayrollID"), DigitalDiningRates = B("DigitalDiningRates"),
            PayrollIdLowerLimit = L("PayrollIDLowerLimitPayroll"), PayrollIdUpperLimit = L("PayrollIDUpperLimitPayroll"),
            WeekNumberMemoCode = S("PayrollExportADPWeekNumberMemoCode"),
        };
    }
}

/// <summary>One row of fn_LaborPayrollExportByDate3 (only the columns the ADP export uses).</summary>
public sealed class AdpExportRow
{
    public int StoreId { get; init; }
    public string? PayrollCompany { get; init; }
    public string? PayrollFileCode { get; init; }
    public long PayrollId { get; init; }
    public string? PayrollDepartment { get; init; }
    public string? StaffDepartmentName { get; init; }
    public string? EmployeeName { get; init; }
    public int PayrollWeek { get; init; }
    public decimal? HourlyRate { get; init; }
    public string? PayrollCode { get; init; }
    public decimal RegularHours { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal DoubletimeHours { get; init; }
    public decimal SplitShiftAdditionalPay { get; init; }
    public decimal CashTips { get; init; }
    public decimal TransferredTips { get; init; }
    public decimal PosTipsNetCreditCard { get; init; }
    public decimal CashSales { get; init; }
    public decimal TransferredSales { get; init; }
    public decimal CreditCardSales { get; init; }

    public static AdpExportRow FromRow(DataRow r)
    {
        string? S(string c) => r[c] is DBNull ? null : Convert.ToString(r[c]);
        decimal D(string c) => r[c] is DBNull ? 0m : Convert.ToDecimal(r[c]);
        return new AdpExportRow
        {
            StoreId = r["StoreID"] is DBNull ? 0 : Convert.ToInt32(r["StoreID"]),
            PayrollCompany = S("PayrollCompany"), PayrollFileCode = S("PayrollFileCode"),
            PayrollId = r["PayrollID"] is DBNull ? 0 : Convert.ToInt64(r["PayrollID"]),
            PayrollDepartment = S("PayrollDepartment"), StaffDepartmentName = S("StaffDepartmentName"), EmployeeName = S("EmployeeName"),
            PayrollWeek = r["PayrollWeek"] is DBNull ? 0 : Convert.ToInt32(r["PayrollWeek"]),
            HourlyRate = r["HourlyRate"] is DBNull ? null : Convert.ToDecimal(r["HourlyRate"]), PayrollCode = S("PayrollCode"),
            RegularHours = D("RegularHours"), OvertimeHours = D("OvertimeHours"), DoubletimeHours = D("DoubletimeHours"),
            SplitShiftAdditionalPay = D("SplitShiftAdditionalPay"), CashTips = D("CashTips"), TransferredTips = D("TransferredTips"),
            PosTipsNetCreditCard = D("POSTipsNetCreditCard"), CashSales = D("CashSales"), TransferredSales = D("TransferredSales"), CreditCardSales = D("CreditCardSales"),
        };
    }
}

/// <summary>One row of fn_LaborPayrollExportMealsByDate.</summary>
public sealed record AdpMealRow(long? PayrollId, string? EmployeeName, int MealCount);

public sealed record AdpFile(string Name, string Content, int Lines);

/// <summary>NextBatchId is what the old code wrote back to tblGlobal.PayrollExportADPBatchID.</summary>
public sealed record AdpExportResult(IReadOnlyList<AdpFile> Files, int NextBatchId);
