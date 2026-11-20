using System.Data;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll.Tips;

/// <summary>One employee in one department group, from dbo.fn_TipsByEmployee (script 020).</summary>
public sealed record TipLine(
    int StoreStaffID, long PayrollID, string EmployeeName, int GroupId, string Group, string PayrollDepartment, string GroupSort,
    decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales,
    decimal TipOutOnCashCreditSales, decimal TipOutOnServiceChargeSales, decimal TipOutPercent,
    decimal ServiceChargeGross, decimal ServiceChargeFee, decimal VoluntaryTipOnServiceCharge,
    decimal Ctp, decimal CreditCardTipsLessTipOut, decimal Cct, decimal Svc)
{
    public decimal VoluntarySales => CashSales + CreditCardSales;
    /// <summary>Compulsory service charge after the card fee and the tip out: the SVC pay line.</summary>
    public decimal ServiceChargeLessTipOut => Svc;
    /// <summary>Everything the employee is paid in tips and service charge (CTP + CCT + SVC).</summary>
    public decimal Total => Ctp + Cct + Svc;
}

/// <summary>Sums a set of lines the way the report footers do.</summary>
public sealed record TipTotals(
    decimal CashSales, decimal CreditCardSales, decimal ServiceChargeSales, decimal TipOutOnCashCreditSales, decimal TipOutOnServiceChargeSales,
    decimal ServiceChargeGross, decimal ServiceChargeFee, decimal VoluntaryTipOnServiceCharge, decimal Ctp, decimal CreditCardTipsLessTipOut, decimal Cct, decimal Svc)
{
    public decimal VoluntarySales => CashSales + CreditCardSales;
    public static TipTotals Of(IEnumerable<TipLine> lines) => lines.Aggregate(
        new TipTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        (a, l) => new TipTotals(a.CashSales + l.CashSales, a.CreditCardSales + l.CreditCardSales, a.ServiceChargeSales + l.ServiceChargeSales,
            a.TipOutOnCashCreditSales + l.TipOutOnCashCreditSales, a.TipOutOnServiceChargeSales + l.TipOutOnServiceChargeSales,
            a.ServiceChargeGross + l.ServiceChargeGross, a.ServiceChargeFee + l.ServiceChargeFee, a.VoluntaryTipOnServiceCharge + l.VoluntaryTipOnServiceCharge,
            a.Ctp + l.Ctp, a.CreditCardTipsLessTipOut + l.CreditCardTipsLessTipOut, a.Cct + l.Cct, a.Svc + l.Svc));
}

/// <summary>The two-part tip report: voluntary tips, then compulsory service charge (names on both). Page rows may be empty for a part.</summary>
public sealed record TipReport(IReadOnlyList<TipLine> Lines)
{
    /// <summary>Employees with voluntary sales or tips, by department group then name.</summary>
    public IEnumerable<TipLine> Voluntary => Lines.Where(l => l.VoluntarySales != 0 || l.CreditCardTipsLessTipOut != 0 || l.Ctp != 0 || l.TipOutOnCashCreditSales != 0);
    /// <summary>Employees with compulsory service charge sales or amounts.</summary>
    public IEnumerable<TipLine> Compulsory => Lines.Where(l => l.ServiceChargeSales != 0 || l.ServiceChargeGross != 0 || l.VoluntaryTipOnServiceCharge != 0 || l.Svc != 0);
}

/// <summary>One person's letter: this period and year to date, each in the two blocks.</summary>
public sealed record TipLetter(TipLine Period, TipLine Ytd);

public sealed class TipReportService
{
    /// <summary>The report for one store. The store must be one the caller may see.</summary>
    public async Task<TipReport> RunAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, DateOnly start, DateOnly end, CancellationToken ct = default)
        => new(await LoadAsync(connectionString, authorizedStoreIds, storeId, start, end, ct));

    /// <summary>Letters: for each employee and group with any activity in the period, the period and the year-to-date lines.</summary>
    public async Task<IReadOnlyList<TipLetter>> LettersAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, DateOnly ytdStart, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (ytdStart > start) throw new ArgumentException("The year-to-date start is after the period start.");
        var period = await LoadAsync(connectionString, authorizedStoreIds, storeId, start, end, ct);
        var ytd = (await LoadAsync(connectionString, authorizedStoreIds, storeId, ytdStart, end, ct)).ToDictionary(l => (l.StoreStaffID, l.GroupId));
        return period.Where(l => l.Total != 0 || l.VoluntarySales != 0 || l.ServiceChargeSales != 0)
            .Select(l => new TipLetter(l, ytd.TryGetValue((l.StoreStaffID, l.GroupId), out var y) ? y : l)).ToList();
    }

    static async Task<IReadOnlyList<TipLine>> LoadAsync(string cs, IReadOnlySet<int> authorized, int storeId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        if (end < start) throw new ArgumentException("The end date is before the start date.");
        if (!authorized.Contains(storeId)) throw new UnauthorizedAccessException("You are not authorized for that store.");
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
select StoreStaffID, PayrollID, EmployeeName, StaffDepartmentGroupID, StaffDepartmentGroup, PayrollDepartment, StaffDepartmentGroupSort,
       CashSales, CreditCardSales, ServiceChargeSales, TipOutOnCashCreditSales, TipOutOnServiceChargeSales, TipOutPercent,
       ServiceChargeGross, ServiceChargeFee, VoluntaryTipOnServiceCharge, CTP, CreditCardTipsLessTipOut, CCT, SVC
from dbo.fn_TipsByEmployee(@s, @d1, @d2)
order by StaffDepartmentGroupSort, StaffDepartmentGroup, EmployeeName", cn) { CommandTimeout = 300 };
        cmd.Parameters.Add("@s", SqlDbType.VarChar, -1).Value = storeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cmd.Parameters.Add("@d1", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@d2", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
        var list = new List<TipLine>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            decimal D(int i) => r.IsDBNull(i) ? 0m : r.GetDecimal(i);
            list.Add(new TipLine(r.GetInt32(0), r.GetInt64(1), r.IsDBNull(2) ? "" : r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? "" : r.GetString(6),
                D(7), D(8), D(9), D(10), D(11), D(12), D(13), D(14), D(15), D(16), D(17), D(18), D(19)));
        }
        return list;
    }
}
