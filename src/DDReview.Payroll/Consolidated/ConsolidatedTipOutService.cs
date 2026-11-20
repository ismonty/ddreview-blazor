using System.Data;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll.Consolidated;

/// <summary>One row of fn_ConsolidatedTipOut32 (the day screen and report).</summary>
public sealed record TipOutRow(
    int? BatchId, int StoreId, int StoreStaffId, int StaffDepartmentGroupId, string StaffDepartmentGroup, string? PayrollDepartment, string EmployeeName, int SourceLevel,
    decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal TotalSales, decimal TotalCashCreditCardSales,
    decimal DefaultTipOutPercentage, decimal TipOutPortionOfSales, decimal TipOutPortionOfCashCreditCardSales, decimal TipOutPortionOfServiceChargedSales,
    decimal CreditCardTips, decimal NetCreditCardTips, decimal ServiceChargeTips, decimal NetServiceChargeTips, decimal TotalNonCashTips, decimal NonCashTipsLessTipOut,
    decimal CashTips, decimal TotalTips, decimal TipsNotPaid, decimal DiscretionaryTipOnTopOfServiceCharges, bool NonZero);

/// <summary>Column sums, as the Access form's total boxes showed them (null counts as 0).</summary>
public sealed record TipOutTotals(decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal TotalSales,
    decimal TipOutPortionOfSales, decimal CreditCardTips, decimal ServiceChargeTips, decimal CashTips, decimal TotalTips, decimal TipsNotPaid, decimal DiscretionaryTips)
{
    public static TipOutTotals Of(IEnumerable<TipOutRow> rows)
    {
        decimal cs = 0, cc = 0, sc = 0, ts = 0, po = 0, cct = 0, sct = 0, ct = 0, tt = 0, np = 0, dt = 0;
        foreach (var r in rows)
        {
            cs += r.CashSales; cc += r.CreditCardSales; sc += r.ServiceChargeSales; ts += r.TotalSales; po += r.TipOutPortionOfSales;
            cct += r.CreditCardTips; sct += r.ServiceChargeTips; ct += r.CashTips; tt += r.TotalTips; np += r.TipsNotPaid; dt += r.DiscretionaryTipOnTopOfServiceCharges;
        }
        return new(cs, cc, sc, ts, po, cct, sct, ct, tt, np, dt);
    }
}

/// <summary>Adjustment totals for the day from fn_ConsolidatedTipOutTotalAdjustments: the part of each total box that came from adjustments.</summary>
public sealed record TipOutAdjustmentTotals(decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal CashTips, decimal CreditCardTips, decimal ServiceChargeTips);

public sealed record TipOutScreen(IReadOnlyList<TipOutRow> Rows, TipOutTotals Totals, TipOutAdjustmentTotals? Adjustments, bool PayrollGroupMissing);

/// <summary>One adjustment line (tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments). Money amounts are what the Access datasheet let a user type.</summary>
public sealed record TipOutAdjustment(
    int ItemId, int BatchId, int? SubtractStoreStaffId, int? SubtractStaffDepartmentGroupId, decimal SubtractTipOutPercentage,
    int? AddStoreStaffId, int? AddStaffDepartmentGroupId, decimal AddTipOutPercentage, int SourceLevel,
    decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal CreditCardTips, decimal ServiceChargeTips, decimal CashTips,
    string? OutTipper = null, string? InTipper = null);

public sealed record LookupItem(int Id, string Name);

/// <summary>One person's compliance letter data: the period and the year to date.</summary>
public sealed record ComplianceRow(
    int StoreStaffId, string StaffDepartmentGroup, string EmployeeName, int SourceLevel,
    decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal TotalSales, decimal TipOutPortionOfSales,
    decimal CreditCardTips, decimal ServiceChargeTips, decimal NonCashTipsLessTipOut, decimal CashTips, decimal TotalTips, decimal TipsNotPaid,
    decimal YtdCashSales, decimal YtdCreditCardSales, decimal YtdServiceChargeSales, decimal YtdTotalSales, decimal YtdTipOutPortionOfSales,
    decimal YtdCreditCardTips, decimal YtdServiceChargeTips, decimal YtdNonCashTipsLessTipOut, decimal YtdCashTips, decimal YtdTotalTips, decimal YtdTipsNotPaid);

public sealed record ComplianceLetters(IReadOnlyList<ComplianceRow> People, ComplianceRow? StoreTotal, string Note);

/// <summary>
/// Port of the Access "Data Entry/Review" form for consolidated tip outs (frmPayrollConsolidatedTipOuts), its adjustments datasheet,
/// the day report (rptConsolidatedTipOuts) and the compliance letters (rptConsolidatedTipOutComplianceLetters).
/// It calls the same database objects Access did (cs_spConsolidatedTipOut3Crunch2 and fn_ConsolidatedTipOut32, ...ComplianceLetters2), so the
/// rules live in the client database and stay the same for both programs.
/// </summary>
public sealed class ConsolidatedTipOutService
{
    const string WorkTable = "tblConsolidatedTipOutTempTable2";
    const string LettersWorkTable = "tblConsolidatedTipOutTempTableComplianceLetters2";

    static void RequireStore(IReadOnlySet<int> authorized, int storeId)
    {
        if (!authorized.Contains(storeId)) throw new UnauthorizedAccessException("You are not authorized for that store.");
    }

    static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    static decimal Dec(IDataRecord r, string col) { var v = r[col]; return v is DBNull ? 0m : Convert.ToDecimal(v); }
    static string? Str(IDataRecord r, string col) { var v = r[col]; return v is DBNull ? null : Convert.ToString(v); }
    static int? IntN(IDataRecord r, string col) { var v = r[col]; return v is DBNull ? null : Convert.ToInt32(v); }

    static TipOutRow ReadRow(SqlDataReader r) => new(
        IntN(r, "BatchID"), Convert.ToInt32(r["StoreID"]), IntN(r, "StoreStaffID") ?? 0, IntN(r, "StaffDepartmentGroupID") ?? 0,
        Str(r, "StaffDepartmentGroup") ?? "", Str(r, "PayrollDepartment"), Str(r, "EmployeeName") ?? "", IntN(r, "SourceLevel") ?? 0,
        Dec(r, "CashSales"), Dec(r, "CreditCardSales"), Dec(r, "ServiceChargeSales"), Dec(r, "TotalSales"), Dec(r, "TotalCashCreditCardSales"),
        Dec(r, "ConsolidatedDefaultTipOutPercentageOfSales"), Dec(r, "ConsolidatedTipOutPortionOfSales"),
        Dec(r, "ConsolidatedTipOutPortionOfCashCreditCardSales"), Dec(r, "ConsolidatedTipOutPortionOfServiceChargedSales"),
        Dec(r, "CreditCardTips"), Dec(r, "NetCreditCardTips"), Dec(r, "ServiceChargeTips"), Dec(r, "NetServiceChargeTips"), Dec(r, "TotalNonCashTips"), Dec(r, "NonCashTipsLessTipOut"),
        Dec(r, "CashTips"), Dec(r, "TotalTips"), Dec(r, "TipsNotPaid"), Dec(r, "DiscretionaryTipOnTopOfServiceCharges"), Dec(r, "NonZeroBit") != 0m);

    static async Task<int> PayrollGroupAsync(SqlConnection cn, int storeId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select PayrollGroupNumber from dbo.tblStores where StoreID = @s", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is null or DBNull ? 0 : Convert.ToInt32(v);
    }

    static async Task CrunchAsync(SqlConnection cn, string proc, int storeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var crunch = new SqlCommand(proc, cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 };
        crunch.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        crunch.Parameters.Add("@TipDate1", SqlDbType.DateTime).Value = D(from);
        crunch.Parameters.Add("@TipDate2", SqlDbType.DateTime).Value = D(to);
        crunch.Parameters.Add("@StoreStaffID", SqlDbType.Int).Value = 0;
        crunch.Parameters.Add("@ClassifyByIndividualReceipt", SqlDbType.Int).Value = 0;
        await crunch.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The screen. Like the Access Retrieve: nothing is shown for a store whose PayrollGroupNumber is not above zero; otherwise crunch the day and read the
    /// function. <paramref name="bySource"/> is the Display Mode bit 1 (source level detail), <paramref name="byReceipt"/> is bit 16 (classify by individual receipt),
    /// <paramref name="storeStaffId"/> is the one-employee drill (0 = everyone).
    /// </summary>
    public async Task<TipOutScreen> RetrieveAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, DateOnly date,
        bool bySource, bool byReceipt, int storeStaffId = 0, CancellationToken ct = default)
    {
        RequireStore(authorizedStoreIds, storeId);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var group = await PayrollGroupAsync(cn, storeId, ct);
        if (group <= 0) return new TipOutScreen([], TipOutTotals.Of([]), null, true);

        var rows = new List<TipOutRow>();
        var gate = CrunchGate.For(connectionString, WorkTable);
        await gate.WaitAsync(ct);
        try
        {
            await CrunchAsync(cn, "dbo.cs_spConsolidatedTipOut3Crunch2", storeId, date, date, ct);
            await using var cmd = new SqlCommand("select * from dbo.fn_ConsolidatedTipOut32(@s, @d, @d, @src, @staff, @cls, @pg) where SourceLevel < 99", cn) { CommandTimeout = 300 };
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = D(date);
            cmd.Parameters.Add("@src", SqlDbType.Bit).Value = bySource;
            cmd.Parameters.Add("@staff", SqlDbType.Int).Value = storeStaffId;
            cmd.Parameters.Add("@cls", SqlDbType.Bit).Value = byReceipt;
            cmd.Parameters.Add("@pg", SqlDbType.BigInt).Value = (long)group;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) rows.Add(ReadRow(rd));
        }
        finally { gate.Release(); }

        TipOutAdjustmentTotals? adj = null;
        await using (var cmd = new SqlCommand("select * from dbo.fn_ConsolidatedTipOutTotalAdjustments(@s, @d, @d, 0)", cn) { CommandTimeout = 120 })
        {
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = D(date);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct))     // the Access form used the first row only
                adj = new(Dec(rd, "CashSales"), Dec(rd, "CreditCardSales"), Dec(rd, "ServiceChargeSales"), Dec(rd, "CashTips"), Dec(rd, "CreditCardTips"), Dec(rd, "ServiceChargeTips"));
        }
        return new TipOutScreen(rows, TipOutTotals.Of(rows), adj, false);
    }

    /// <summary>The day report (rptConsolidatedTipOuts): no source detail, everyone, payroll group 0, only rows that have an amount.</summary>
    public async Task<IReadOnlyList<TipOutRow>> DayReportAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, DateOnly date, bool byReceipt, CancellationToken ct = default)
    {
        RequireStore(authorizedStoreIds, storeId);
        var rows = new List<TipOutRow>();
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var gate = CrunchGate.For(connectionString, WorkTable);
        await gate.WaitAsync(ct);
        try
        {
            await CrunchAsync(cn, "dbo.cs_spConsolidatedTipOut3Crunch2", storeId, date, date, ct);
            await using var cmd = new SqlCommand("select * from dbo.fn_ConsolidatedTipOut32(@s, @d, @d, 0, 0, @cls, 0) where NonZeroBit <> 0", cn) { CommandTimeout = 300 };
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = D(date);
            cmd.Parameters.Add("@cls", SqlDbType.Bit).Value = byReceipt;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) rows.Add(ReadRow(rd));
        }
        finally { gate.Release(); }
        return rows;
    }

    /// <summary>
    /// The compliance letters. As in Access, the crunch covers the year-to-date start through the period end; the function then splits the period from the year.
    /// The function adds one store total row (source level 99), returned separately.
    /// </summary>
    public async Task<ComplianceLetters> ComplianceLettersAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId,
        DateOnly ytdStart, DateOnly periodStart, DateOnly periodEnd, bool byReceipt, CancellationToken ct = default)
    {
        RequireStore(authorizedStoreIds, storeId);
        if (periodEnd < periodStart) throw new ArgumentException("The period end is before the period start.");
        if (periodStart < ytdStart) throw new ArgumentException("The period starts before the year-to-date start.");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var people = new List<ComplianceRow>();
        ComplianceRow? total = null;
        var gate = CrunchGate.For(connectionString, LettersWorkTable);
        await gate.WaitAsync(ct);
        try
        {
            await CrunchAsync(cn, "dbo.cs_spConsolidatedTipOut3CrunchComplianceLetters2", storeId, ytdStart, periodEnd, ct);
            await using var cmd = new SqlCommand("select * from dbo.fn_ConsolidatedTipOutDateRangeWithYearToDate32(@s, @p1, @p2, @y, @cls, 1) where NonZeroBit <> 0", cn) { CommandTimeout = 300 };
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            cmd.Parameters.Add("@p1", SqlDbType.DateTime).Value = D(periodStart);
            cmd.Parameters.Add("@p2", SqlDbType.DateTime).Value = D(periodEnd);
            cmd.Parameters.Add("@y", SqlDbType.DateTime).Value = D(ytdStart);
            cmd.Parameters.Add("@cls", SqlDbType.Bit).Value = byReceipt;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var row = new ComplianceRow(IntN(rd, "StoreStaffID") ?? 0, Str(rd, "StaffDepartmentGroup") ?? "", Str(rd, "EmployeeName") ?? "", IntN(rd, "SourceLevel") ?? 0,
                    Dec(rd, "CashSales"), Dec(rd, "CreditCardSales"), Dec(rd, "ServiceChargeSales"), Dec(rd, "TotalSales"), Dec(rd, "ConsolidatedTipOutPortionOfSales"),
                    Dec(rd, "CreditCardTips"), Dec(rd, "ServiceChargeTips"), Dec(rd, "NonCashTipsLessTipOut"), Dec(rd, "CashTips"), Dec(rd, "TotalTips"), Dec(rd, "TipsNotPaid"),
                    Dec(rd, "YTDCashSales"), Dec(rd, "YTDCreditCardSales"), Dec(rd, "YTDServiceChargeSales"), Dec(rd, "YTDTotalSales"), Dec(rd, "YTDConsolidatedTipOutPortionOfSales"),
                    Dec(rd, "YTDCreditCardTips"), Dec(rd, "YTDServiceChargeTips"), Dec(rd, "YTDNonCashTipsLessTipOut"), Dec(rd, "YTDCashTips"), Dec(rd, "YTDTotalTips"), Dec(rd, "YTDTipsNotPaid"));
                if (row.SourceLevel >= 99) total = row; else people.Add(row);
            }
        }
        finally { gate.Release(); }

        string note = "";
        await using (var cmd = new SqlCommand("select top 1 ConsolidatedTipReportingComplianceLetterNote from dbo.tblGlobal", cn) { CommandTimeout = 60 })
        {
            var v = await cmd.ExecuteScalarAsync(ct);
            if (v is not null and not DBNull) note = Convert.ToString(v) ?? "";
        }
        return new ComplianceLetters(people, total, note);
    }

    // ---- adjustments (the datasheet opened by the form's Adjustments button) ----

    static async Task<(int StoreId, int DayNumber)> BatchAsync(SqlConnection cn, IReadOnlySet<int> authorized, int batchId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select StoreID, DayNumber from dbo.tblCashAndSalesBatches where BatchID = @b", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = batchId;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        if (!await rd.ReadAsync(ct)) throw new ArgumentException("That batch was not found.");
        var storeId = Convert.ToInt32(rd["StoreID"]);
        RequireStore(authorized, storeId);
        return (storeId, Convert.ToInt32(rd["DayNumber"]));
    }

    /// <summary>The names for the adjustment drop-downs: staff and department groups for the batch's store.</summary>
    public async Task<(IReadOnlyList<LookupItem> Staff, IReadOnlyList<LookupItem> Groups)> LookupsAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, CancellationToken ct = default)
    {
        RequireStore(authorizedStoreIds, storeId);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var staff = new List<LookupItem>();
        await using (var cmd = new SqlCommand("select StoreStaffID, isnull(ltrim(rtrim(LastName)) + ', ', '') + isnull(ltrim(rtrim(FirstName)), '') as FullName from dbo.tblStaff where StoreID = @s order by LastName, FirstName", cn) { CommandTimeout = 60 })
        {
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) staff.Add(new(Convert.ToInt32(rd[0]), Convert.ToString(rd[1]) ?? ""));
        }
        var groups = new List<LookupItem>();
        await using (var cmd = new SqlCommand("select StaffDepartmentGroupID, StaffDepartmentGroup from dbo.tblStaffDepartmentGroups where StoreID = @s order by StaffDepartmentGroupSort", cn) { CommandTimeout = 60 })
        {
            cmd.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) groups.Add(new(Convert.ToInt32(rd[0]), Convert.ToString(rd[1]) ?? ""));
        }
        return (staff, groups);
    }

    /// <summary>Adjustments for one batch; with <paramref name="storeStaffId"/> only those that add to or subtract from that person (the drill).</summary>
    public async Task<IReadOnlyList<TipOutAdjustment>> ListAdjustmentsAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int batchId, int storeStaffId = 0, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await BatchAsync(cn, authorizedStoreIds, batchId, ct);
        var list = new List<TipOutAdjustment>();
        await using var cmd = new SqlCommand(@"
select adj.ItemID, adj.BatchID, adj.SubtractStoreStaffID, adj.SubtractStaffDepartmentGroupID, adj.SubtractTipOutPercentage,
       adj.AddStoreStaffID, adj.AddStaffDepartmentGroupID, adj.AddTipOutPercentage, adj.SourceLevel,
       adj.CashSales, adj.CreditCardSales, adj.ServiceChargeSales, adj.CreditCardTips, adj.ServiceChargeTips, adj.CashTips,
       (select rtrim(LastName) + ', ' + rtrim(FirstName) from dbo.tblStaff where StoreID = adj.StoreID and StoreStaffID = adj.SubtractStoreStaffID) as OutTipper,
       (select rtrim(LastName) + isnull(', ' + rtrim(FirstName), '') from dbo.tblStaff where StoreID = adj.StoreID and StoreStaffID = adj.AddStoreStaffID) as InTipper
from dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments adj
where adj.BatchID = @b and (@staff = 0 or adj.AddStoreStaffID = @staff or adj.SubtractStoreStaffID = @staff)
order by adj.ItemID", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = batchId;
        cmd.Parameters.Add("@staff", SqlDbType.Int).Value = storeStaffId;
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
            list.Add(new TipOutAdjustment(Convert.ToInt32(rd["ItemID"]), Convert.ToInt32(rd["BatchID"]), IntN(rd, "SubtractStoreStaffID"), IntN(rd, "SubtractStaffDepartmentGroupID"), Dec(rd, "SubtractTipOutPercentage"),
                IntN(rd, "AddStoreStaffID"), IntN(rd, "AddStaffDepartmentGroupID"), Dec(rd, "AddTipOutPercentage"), IntN(rd, "SourceLevel") ?? 3,
                Dec(rd, "CashSales"), Dec(rd, "CreditCardSales"), Dec(rd, "ServiceChargeSales"), Dec(rd, "CreditCardTips"), Dec(rd, "ServiceChargeTips"), Dec(rd, "CashTips"),
                Str(rd, "OutTipper"), Str(rd, "InTipper")));
        return list;
    }

    /// <summary>
    /// What the web adds to the Access datasheet's rules (which accepted anything): a line needs a person on at least one side, a department group with each person,
    /// and percentages from 0 to 100. The crunch silently drops a line whose person or group does not exist, which would hide a mistake.
    /// </summary>
    public static string? Validate(TipOutAdjustment a)
    {
        if (a.SubtractStoreStaffId is null && a.AddStoreStaffId is null) return "Choose who the amount comes from, who it goes to, or both.";
        if (a.SubtractStoreStaffId is not null && a.SubtractStaffDepartmentGroupId is null) return "Choose the department group the amount comes from.";
        if (a.AddStoreStaffId is not null && a.AddStaffDepartmentGroupId is null) return "Choose the department group the amount goes to.";
        if (a.SubtractStoreStaffId is null && a.SubtractStaffDepartmentGroupId is not null) return "A department group was chosen for the 'from' side but no person.";
        if (a.AddStoreStaffId is null && a.AddStaffDepartmentGroupId is not null) return "A department group was chosen for the 'to' side but no person.";
        if (a.SubtractTipOutPercentage is < 0 or > 100 || a.AddTipOutPercentage is < 0 or > 100) return "A tip out percentage must be from 0 to 100.";
        if (a.SourceLevel is < 1 or > 98) return "The source level must be from 1 to 98.";
        return null;
    }

    /// <summary>Adds (ItemId 0) or changes one adjustment. The store and day come from the batch, as the Access datasheet defaulted them.</summary>
    public async Task<int> SaveAdjustmentAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, TipOutAdjustment a, CancellationToken ct = default)
    {
        var problem = Validate(a);
        if (problem is not null) throw new ArgumentException(problem);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var (storeId, dayNumber) = await BatchAsync(cn, authorizedStoreIds, a.BatchId, ct);

        // Both people and both groups must belong to this store.
        foreach (var staff in new[] { a.SubtractStoreStaffId, a.AddStoreStaffId }.Where(x => x is not null))
        {
            await using var chk = new SqlCommand("select count(*) from dbo.tblStaff where StoreID = @s and StoreStaffID = @id", cn);
            chk.Parameters.Add("@s", SqlDbType.Int).Value = storeId; chk.Parameters.Add("@id", SqlDbType.Int).Value = staff!.Value;
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) == 0) throw new ArgumentException("A chosen person does not belong to this store.");
        }
        foreach (var grp in new[] { a.SubtractStaffDepartmentGroupId, a.AddStaffDepartmentGroupId }.Where(x => x is not null))
        {
            await using var chk = new SqlCommand("select count(*) from dbo.tblStaffDepartmentGroups where StoreID = @s and StaffDepartmentGroupID = @id", cn);
            chk.Parameters.Add("@s", SqlDbType.Int).Value = storeId; chk.Parameters.Add("@id", SqlDbType.Int).Value = grp!.Value;
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct)) == 0) throw new ArgumentException("A chosen department group does not belong to this store.");
        }

        var insert = a.ItemId == 0;
        var sql = insert
            ? @"insert into dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments
                (BatchID, StoreID, DayNumber, SubtractStoreStaffID, SubtractStaffDepartmentGroupID, SubtractTipOutPercentage, AddStoreStaffID, AddStaffDepartmentGroupID, AddTipOutPercentage,
                 SourceLevel, CashSales, CreditCardSales, ServiceChargeSales, CreditCardTips, ServiceChargeTips, CashTips)
                values (@b, @store, @dn, @os, @og, @op, @as, @ag, @ap, @lvl, @cs, @cc, @sc, @cct, @sct, @ct);
                select cast(scope_identity() as int)"
            : @"update dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments set
                SubtractStoreStaffID = @os, SubtractStaffDepartmentGroupID = @og, SubtractTipOutPercentage = @op, AddStoreStaffID = @as, AddStaffDepartmentGroupID = @ag, AddTipOutPercentage = @ap,
                SourceLevel = @lvl, CashSales = @cs, CreditCardSales = @cc, ServiceChargeSales = @sc, CreditCardTips = @cct, ServiceChargeTips = @sct, CashTips = @ct
                where ItemID = @id and BatchID = @b;
                select @id";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = a.ItemId;
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = a.BatchId;
        cmd.Parameters.Add("@store", SqlDbType.Int).Value = storeId;
        cmd.Parameters.Add("@dn", SqlDbType.Int).Value = dayNumber;
        cmd.Parameters.Add("@os", SqlDbType.Int).Value = (object?)a.SubtractStoreStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@og", SqlDbType.Int).Value = (object?)a.SubtractStaffDepartmentGroupId ?? DBNull.Value;
        cmd.Parameters.Add("@op", SqlDbType.Decimal).Value = a.SubtractTipOutPercentage;
        cmd.Parameters.Add("@as", SqlDbType.Int).Value = (object?)a.AddStoreStaffId ?? DBNull.Value;
        cmd.Parameters.Add("@ag", SqlDbType.Int).Value = (object?)a.AddStaffDepartmentGroupId ?? DBNull.Value;
        cmd.Parameters.Add("@ap", SqlDbType.Decimal).Value = a.AddTipOutPercentage;
        cmd.Parameters.Add("@lvl", SqlDbType.Int).Value = a.SourceLevel;
        cmd.Parameters.Add("@cs", SqlDbType.Money).Value = a.CashSales;
        cmd.Parameters.Add("@cc", SqlDbType.Money).Value = a.CreditCardSales;
        cmd.Parameters.Add("@sc", SqlDbType.Money).Value = a.ServiceChargeSales;
        cmd.Parameters.Add("@cct", SqlDbType.Money).Value = a.CreditCardTips;
        cmd.Parameters.Add("@sct", SqlDbType.Money).Value = a.ServiceChargeTips;
        cmd.Parameters.Add("@ct", SqlDbType.Money).Value = a.CashTips;
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        if (!insert)
        {
            // an update that matched no row (another user deleted it, or a wrong batch) is reported, not hidden
            await using var exists = new SqlCommand("select count(*) from dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments where ItemID = @id and BatchID = @b", cn);
            exists.Parameters.Add("@id", SqlDbType.Int).Value = a.ItemId; exists.Parameters.Add("@b", SqlDbType.Int).Value = a.BatchId;
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 0) throw new ArgumentException("That adjustment no longer exists.");
        }
        return id;
    }

    public async Task DeleteAdjustmentAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int batchId, int itemId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await BatchAsync(cn, authorizedStoreIds, batchId, ct);
        await using var cmd = new SqlCommand("delete from dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments where ItemID = @id and BatchID = @b", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = itemId;
        cmd.Parameters.Add("@b", SqlDbType.Int).Value = batchId;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
