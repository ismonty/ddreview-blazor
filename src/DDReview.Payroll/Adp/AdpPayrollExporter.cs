namespace DDReview.Payroll.Adp;

/// <summary>Port of the Access ExportADPPayroll: turns payroll export rows into ADP EPI files. Pure logic, no database.</summary>
public static class AdpPayrollExporter
{
    public static AdpExportResult Build(AdpSettings s, IEnumerable<AdpExportRow> rows, IEnumerable<AdpMealRow> meals)
    {
        var epip = new EpipFile(s.CoCode, s.BatchId, s.BatchDescription, s.SsnAsPayrollId);
        var limited = rows;
        if (s.PayrollIdLowerLimit is { } lo && s.PayrollIdUpperLimit is { } hi)
            limited = rows.Where(r => { var id = r.PayrollId % 1_000_000_000_000_000L; return id >= lo && id <= hi; });

        foreach (var r in limited)
        {
            epip.AddNew();
            if (s.SortMode != 0)
                epip.SortingData = s.SortMode == 1 ? r.EmployeeName ?? ""
                    : s.SortMode == 2 ? (r.StaffDepartmentName ?? "") + "-" + (r.EmployeeName ?? "")
                    : (r.PayrollDepartment ?? "") + "-" + (r.EmployeeName ?? "");
            if (s.WeekMemoOn) epip.Put(EpipFile.Group.Memo, "W", r.PayrollWeek);
            epip.FileNameCode = r.PayrollFileCode ?? s.FileNameCode;
            if (r.PayrollCompany is not null) epip.CoCode = r.PayrollCompany;
            epip.FileNumber = r.PayrollId;
            if (r.PayrollDepartment is not null) epip.TempDept = r.PayrollDepartment;
            if (s.DigitalDiningRates) { if (r.HourlyRate is { } rate) epip.TempRate = rate; }
            else if (r.HourlyRate == 0.02m) epip.RateCode = "2";
            else if (r.HourlyRate == 0.03m) epip.RateCode = "3";

            if (r.PayrollCode is null) epip.RegHours = r.RegularHours;
            else if (r.RegularHours != 0) epip.Put(EpipFile.Group.Hours3, r.PayrollCode, r.RegularHours);
            epip.OtHours = r.OvertimeHours;

            if (r.DoubletimeHours != 0)
                switch (s.DoubleTimeCodeNumber)
                {
                    case "3": epip.Put(EpipFile.Group.Hours3, s.DoubleTimeCode, r.DoubletimeHours); break;
                    case "4": epip.Put(EpipFile.Group.Hours4, s.DoubleTimeCode, r.DoubletimeHours); break;
                    case "5": epip.Put(EpipFile.Group.Hours5, s.DoubleTimeCode, r.DoubletimeHours); break;
                }

            // Split-shift pay: code "S", and (as in the old code) the slot number is the double-time slot number.
            if (r.SplitShiftAdditionalPay != 0)
                PutEarnings(epip, s.DoubleTimeCodeNumber, "S", r.SplitShiftAdditionalPay);

            var tips = r.CashTips + r.TransferredTips + (s.TipsIncludeChargedTips ? r.PosTipsNetCreditCard : 0);
            var sales = r.CashSales + r.TransferredSales + (s.TipsIncludeChargedTips ? r.CreditCardSales : 0);
            if (tips != 0 || r.CashSales + r.TransferredSales + r.CreditCardSales != 0)
            {
                if (tips != 0 && s.TipsCode.Length > 0)
                {
                    if (s.TipsAsEarnings) PutEarnings(epip, s.TipsCodeNumber, s.TipsCode, tips);
                    else epip.Put(EpipFile.Group.Memo, s.TipsCode, tips);
                }
                if (sales != 0 && s.SalesCode.Length > 0)
                    epip.Put(EpipFile.Group.Memo, s.SalesCode, Math.Floor(sales + 0.5m));
            }

            if (s.ChargedTipsSentSeparately && (r.PosTipsNetCreditCard != 0 || r.CreditCardSales != 0))
            {
                if (r.PosTipsNetCreditCard != 0 && s.ChargedTipsCode.Length > 0)
                {
                    if (s.ChargedTipsAsEarnings) PutEarnings(epip, s.TipsCodeNumber, s.ChargedTipsCode, r.PosTipsNetCreditCard);
                    else epip.Put(EpipFile.Group.Memo, s.ChargedTipsCode, r.PosTipsNetCreditCard);
                }
                if (r.CreditCardSales != 0 && s.ChargedSalesCode.Length > 0)
                    epip.Put(EpipFile.Group.Memo, s.ChargedSalesCode, Math.Floor(r.CreditCardSales + 0.5m));
            }
        }

        foreach (var m in meals)
        {
            if (m.PayrollId is null || m.MealCount == 0) continue;
            epip.AddNew();
            if (s.SortMode != 0) epip.SortingData = "ZZZZZZ" + (m.EmployeeName ?? "");
            epip.FileNumber = m.PayrollId.Value;
            epip.FileNameCode = s.FileNameCode;
            epip.Put(EpipFile.Group.Deduction, s.MealDeductionCode, s.MealAmount * m.MealCount);
        }

        var (files, next) = epip.Export();
        return new AdpExportResult(files, next);
    }

    static void PutEarnings(EpipFile epip, string slot, string code, decimal amount)
    {
        switch (slot)
        {
            case "3": epip.Put(EpipFile.Group.Earnings3, code, amount); break;
            case "4": epip.Put(EpipFile.Group.Earnings4, code, amount); break;
            case "5": epip.Put(EpipFile.Group.Earnings5, code, amount); break;
        }
    }
}
