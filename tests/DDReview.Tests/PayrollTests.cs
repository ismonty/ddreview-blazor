using System.Data;
using DDReview.Payroll;
using DDReview.Payroll.Adp;

namespace DDReview.Tests;

public class AdpExportTests
{
    static AdpSettings Settings(Action<AdpSettingsBuilder>? tweak = null)
    {
        var b = new AdpSettingsBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    // Small mutable helper so tests read as "what changed from the base setup".
    sealed class AdpSettingsBuilder
    {
        public string FileNameCode = "01", CoCode = "ABC", BatchDescription = "Test", TipsCode = "", SalesCode = "", ChargedTipsCode = "", ChargedSalesCode = "", MealCode = "", WeekMemo = "", TipsNumber = "3";
        public int BatchId = 5, SortMode;
        public bool TipsAsEarnings, IncludeCharged, Separate, ChargedEarnings, Ssn;
        public decimal MealAmount;
        public AdpSettings Build() => new()
        {
            FileNameCode = FileNameCode, CoCode = CoCode, BatchId = BatchId, BatchDescription = BatchDescription,
            TipsAsEarnings = TipsAsEarnings, TipsIncludeChargedTips = IncludeCharged, TipsCode = TipsCode, TipsCodeNumber = TipsNumber, SalesCode = SalesCode,
            ChargedTipsSentSeparately = Separate, ChargedTipsAsEarnings = ChargedEarnings, ChargedTipsCode = ChargedTipsCode, ChargedSalesCode = ChargedSalesCode,
            MealAmount = MealAmount, MealDeductionCode = MealCode, SortMode = SortMode, SsnAsPayrollId = Ssn, WeekNumberMemoCode = WeekMemo,
        };
    }

    static AdpExportRow Row(long id, string? dept = "D1", decimal reg = 0, decimal ot = 0, decimal cashTips = 0, decimal cashSales = 0, string? fileCode = null,
        decimal posCc = 0, decimal ccSales = 0, int week = 1, string? name = "NAME") =>
        new() { PayrollId = id, PayrollDepartment = dept, RegularHours = reg, OvertimeHours = ot, CashTips = cashTips, CashSales = cashSales, PayrollFileCode = fileCode,
                PosTipsNetCreditCard = posCc, CreditCardSales = ccSales, PayrollWeek = week, EmployeeName = name };

    static readonly AdpMealRow[] NoMeals = [];

    [Fact]
    public void One_employee_line_matches_the_old_layout()
    {
        var s = Settings(b => { b.TipsAsEarnings = true; b.TipsCode = "T"; b.SalesCode = "2"; });
        var r = AdpPayrollExporter.Build(s, [Row(1001, reg: 40, ot: 5, cashTips: 100.5m, cashSales: 2000.4m)], NoMeals);
        var f = Assert.Single(r.Files);
        Assert.Equal("EPIABC01.csv", f.Name);
        Assert.Equal("Co Code,Batch ID,File #,Batch Description,Temp Dept,Reg Hours,O/T Hours,Earnings 3 Code,Earnings 3 Amount,Memo Code,Memo Amount\r\n" +
                     "ABC,5,1001,\"Test\",\"D1\",40.00,5.00,\"T\",100.50,\"2\",2000\r\n", f.Content);
        Assert.Equal(6, r.NextBatchId);
    }

    [Fact]
    public void Columns_exist_if_any_row_uses_them_and_empty_cells_stay_empty_and_rows_sort_by_file_number()
    {
        var s = Settings(b => { b.TipsAsEarnings = true; b.TipsCode = "T"; b.SalesCode = "2"; });
        var r = AdpPayrollExporter.Build(s, [Row(1002, dept: null, reg: 10), Row(1001, reg: 40, ot: 5, cashTips: 100.5m, cashSales: 2000.4m)], NoMeals);
        var lines = r.Files[0].Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("ABC,5,1001,\"Test\",\"D1\",40.00,5.00,\"T\",100.50,\"2\",2000", lines[1]);
        Assert.Equal("ABC,5,1002,\"Test\",,10.00,,,,,", lines[2]);
    }

    [Fact]
    public void A_fifth_memo_starts_a_follow_on_line_in_the_same_file()
    {
        var s = Settings(b => { b.WeekMemo = "W"; b.TipsCode = "T"; b.SalesCode = "S"; b.Separate = true; b.ChargedTipsCode = "C"; b.ChargedSalesCode = "D"; });
        var r = AdpPayrollExporter.Build(s, [Row(1001, reg: 40, cashTips: 50, cashSales: 500, posCc: 10, ccSales: 100, week: 3)], NoMeals);
        var f = Assert.Single(r.Files);
        Assert.Equal(2, f.Lines);
        var lines = f.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Co Code,Batch ID,File #,Batch Description,Temp Dept,Reg Hours,O/T Hours,Memo Code,Memo Amount,Memo Code,Memo Amount,Memo Code,Memo Amount,Memo Code,Memo Amount", lines[0]);
        Assert.Equal("ABC,5,1001,\"Test\",\"D1\",40.00,,\"W\",3.00,\"T\",50.00,\"S\",500.00,\"C\",10.00", lines[1]);
        Assert.Equal("ABC,5,1001,\"Test\",\"D1\",,,\"D\",100.00,,,,,,", lines[2]);
    }

    [Fact]
    public void Charged_tips_and_sales_fold_into_the_tip_and_sales_memos_when_the_setting_says_so()
    {
        var s = Settings(b => { b.IncludeCharged = true; b.TipsCode = "T"; b.SalesCode = "S"; });
        var r = AdpPayrollExporter.Build(s, [Row(1001, reg: 8, cashTips: 50, posCc: 10, cashSales: 500, ccSales: 100)], NoMeals);
        Assert.Equal("ABC,5,1001,\"Test\",\"D1\",8.00,,\"T\",60.00,\"S\",600.00", r.Files[0].Content.Split("\r\n")[1]);
    }

    [Fact]
    public void Charged_tips_as_earnings_go_in_the_chosen_earnings_slot()
    {
        var s = Settings(b => { b.Separate = true; b.ChargedEarnings = true; b.ChargedTipsCode = "C"; b.TipsNumber = "4"; });
        var r = AdpPayrollExporter.Build(s, [Row(1001, reg: 8, posCc: 10)], NoMeals);
        var lines = r.Files[0].Content.Split("\r\n");
        Assert.EndsWith("Earnings 4 Code,Earnings 4 Amount", lines[0]);
        Assert.Equal("ABC,5,1001,\"Test\",\"D1\",8.00,,\"C\",10.00", lines[1]);
    }

    [Fact]
    public void A_different_file_code_makes_a_second_file_and_advances_the_batch_twice()
    {
        var r = AdpPayrollExporter.Build(Settings(), [Row(1001, reg: 8), Row(1002, reg: 8, fileCode: "02")], NoMeals);
        Assert.Equal(["EPIABC01.csv", "EPIABC02.csv"], r.Files.Select(f => f.Name));
        Assert.Equal(7, r.NextBatchId);
    }

    [Fact]
    public void Ssn_payroll_ids_are_nine_digits()
    {
        var r = AdpPayrollExporter.Build(Settings(b => b.Ssn = true), [Row(12345, reg: 8)], NoMeals);
        Assert.Contains(",000012345,", r.Files[0].Content);
    }

    [Fact]
    public void Meal_deductions_go_last_in_their_own_line()
    {
        var s = Settings(b => { b.SortMode = 1; b.MealCode = "M"; b.MealAmount = 1.5m; });
        var r = AdpPayrollExporter.Build(s, [Row(1001, reg: 8, name: "ADAMS")], [new AdpMealRow(1001, "ADAMS", 2), new AdpMealRow(1002, "ZED", 0), new AdpMealRow(null, "X", 3)]);
        var lines = r.Files[0].Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",\"M\",3.00", lines[2]);
        Assert.StartsWith("ABC,5,1001,", lines[2]);
    }

    [Fact]
    public void Payroll_id_limits_filter_rows()
    {
        var s = new AdpSettings { CoCode = "ABC", FileNameCode = "01", PayrollIdLowerLimit = 1000, PayrollIdUpperLimit = 1500 };
        var r = AdpPayrollExporter.Build(s, [Row(999, reg: 1), Row(1001, reg: 1), Row(1501, reg: 1)], NoMeals);
        Assert.Equal(1, r.Files[0].Lines);
    }

    [Fact]
    public void No_rows_means_no_files_but_the_batch_still_advances()
    {
        var r = AdpPayrollExporter.Build(Settings(), [], NoMeals);
        Assert.Empty(r.Files);
        Assert.Equal(6, r.NextBatchId);
    }

    [Fact]
    public void A_code_too_long_for_the_import_is_refused_by_name()
    {
        var s = Settings(b => { b.TipsAsEarnings = true; b.TipsCode = "TOOLONG"; });
        var ex = Assert.Throws<InvalidOperationException>(() => AdpPayrollExporter.Build(s, [Row(1, reg: 1, cashTips: 5)], NoMeals));
        Assert.Contains("TOOLONG", ex.Message);
    }

    [Fact]
    public void Settings_read_from_a_tblGlobal_row_with_older_columns_missing()
    {
        var t = new DataTable();
        t.Columns.Add("PayrollExportADPCoCode"); t.Columns.Add("PayrollExportADPBatchID", typeof(int)); t.Columns.Add("PayrollExportADPTipsAsEarnings", typeof(bool));
        t.Rows.Add("XYZ", 12, true);
        var s = AdpSettings.FromRow(t.Rows[0]);
        Assert.Equal("XYZ", s.CoCode); Assert.Equal(12, s.BatchId); Assert.True(s.TipsAsEarnings);
        Assert.Equal("DT", s.DoubleTimeCode); Assert.Equal("3", s.DoubleTimeCodeNumber); Assert.Equal("3", s.TipsCodeNumber);
    }
}

public class PayrollTotalsTests
{
    [Fact]
    public void Totals_add_up_the_way_the_old_form_did_and_treat_missing_values_as_zero()
    {
        var t = new DataTable();
        foreach (var c in new[] { "RegularHours", "OvertimeHours", "DoubletimeHours", "TotalHourlyPay", "TotalSales", "TransferredSales", "TotalTips", "POSTipsNetCash", "POSTipsNetCreditCard", "AdditionalPayAmount", "AdditionalOvertimePay" })
            t.Columns.Add(c, typeof(decimal));
        t.Rows.Add(40m, 5m, 1m, 500m, 2000m, 100m, 150m, 90m, 40m, 10m, 2m);
        var r = t.NewRow(); r["RegularHours"] = 10m; t.Rows.Add(r);       // everything else NULL
        var x = PayrollTotals.Compute(t.Rows.Cast<DataRow>());
        Assert.Equal(new PayrollTotals(50m, 5m, 1m, 56m, 500m, 2000m, 2100m, 150m, 130m, 40m, 12m), x);
        Assert.Equal(new PayrollTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), PayrollTotals.Compute([]));
    }
}

public class FeatureSwitchTests
{
    static DDReview.Core.Tenancy.UserAccess User(bool admin) => new() { Administrator = admin, ClientWebUserId = 7 };

    [Fact]
    public async Task Administrators_always_have_every_feature_without_touching_the_database()
    {
        var f = new DDReview.Core.Features.FeatureService();
        var on = await f.EnabledForAsync("not a connection string", User(true));
        Assert.Equal(Enum.GetValues<DDReview.Core.Features.WebFeature>().Length, on.Count);
        Assert.True(await f.IsEnabledAsync("not a connection string", User(true), DDReview.Core.Features.WebFeature.ConsolidatedPayroll));
    }

    [Fact]
    public async Task Only_an_administrator_may_list_or_change_the_switches()
    {
        var f = new DDReview.Core.Features.FeatureService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.ListUsersAsync("Server=.", User(false)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.SetAsync("Server=.", User(false), 7, DDReview.Core.Features.WebFeature.Payroll, true));
    }

    [Fact]
    public void The_switch_script_is_registered_after_the_grant_procedure_and_adds_only_a_table()
    {
        var s = DDReview.Core.Updater.ClientScripts.All.Single(x => x.Id == "005");
        Assert.Equal("005", DDReview.Core.Updater.ClientScripts.All[^1].Id);
        var sql = s.ReadSql();
        Assert.Contains("create table dbo.tblWebUserFeatures", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter ", sql, StringComparison.OrdinalIgnoreCase);
    }
}

public class ConsolidatedTipFileTests
{
    static DDReview.Payroll.Consolidated.ConsolidatedTipRow Row(long file, decimal earn, decimal memo1, decimal ded, string? memo2 = null, decimal memo2Amt = 0) =>
        new("T7S", 999, file, "Digital Dining Tips", 3, "T", earn, "2", memo1, memo2, memo2Amt, null, null, "W", ded);

    [Fact]
    public void Header_comes_from_the_first_row_and_each_row_writes_only_the_pieces_it_has()
    {
        var f = DDReview.Payroll.Consolidated.ConsolidatedTipFile.Build([Row(1234, 100.5m, 2000, 12.34m), Row(1235, 50, 0.4m, 0.4m)])!;
        var lines = f.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Co Code,Batch ID,File #,Batch Description,Earnings 3 Code,Earnings 3 Amount,Memo Code,Memo Amount,Adjust DED Code,Adjust DED Amount", lines[0]);
        Assert.Equal("T7S,999,1234,Digital Dining Tips,T,100.5,2,2000,W,12.34", lines[1]);
        Assert.Equal("T7S,999,1235,Digital Dining Tips,T,50", lines[2]);     // memo rounds to 0 and the deduction rounds to 0, so both are left out
        Assert.Equal(2, f.Lines);
    }

    [Fact]
    public void Memos_round_to_whole_dollars_and_the_deduction_is_written_as_is()
    {
        var f = DDReview.Payroll.Consolidated.ConsolidatedTipFile.Build([Row(1, 10, 1999.5m, 7.5m, "C", 10.4m)])!;
        var lines = f.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Co Code,Batch ID,File #,Batch Description,Earnings 3 Code,Earnings 3 Amount,Memo Code,Memo Amount,Memo Code,Memo Amount,Adjust DED", lines[0]);
        Assert.Equal("T7S,999,1,Digital Dining Tips,T,10,2,2000,C,10,W,7.5", lines[1]);
    }

    [Fact]
    public void Earnings_slot_four_and_no_rows()
    {
        var row = Row(1, 5, 0, 0) with { EarningsType = 4 };
        Assert.Contains("Earnings 4 Code,Earnings 4 Amount", DDReview.Payroll.Consolidated.ConsolidatedTipFile.Build([row])!.Content);
        Assert.Null(DDReview.Payroll.Consolidated.ConsolidatedTipFile.Build([]));
    }

    [Fact]
    public async Task Settings_can_only_be_saved_by_an_administrator_and_the_store_must_be_authorized()
    {
        var svc = new DDReview.Payroll.Consolidated.ConsolidatedPayrollService();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SaveSettingsAsync("Server=.", new DDReview.Core.Tenancy.UserAccess { Administrator = false }, new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ExportTipFileAsync("Server=.", 5, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7), false, new HashSet<int> { 1, 2 }));
    }
}
