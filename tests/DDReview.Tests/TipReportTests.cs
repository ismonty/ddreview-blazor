using DDReview.Payroll.Tips;
using Xunit;

namespace DDReview.Tests;

public class TipReportTests
{
    static TipLine L(string name = "Demoss, Pat", decimal cash = 0, decimal cc = 0, decimal sc = 0, decimal tipOutCc = 0, decimal tipOutSc = 0, decimal gross = 0, decimal fee = 0,
                     decimal vol = 0, decimal ctp = 0, decimal cctLess = 0, decimal svc = 0, int staff = 1, int group = 3) =>
        new(staff, 12345, name, group, "Server", "SRV", "01", cash, cc, sc, tipOutCc, tipOutSc, 3.2m, gross, fee, vol, ctp, cctLess, cctLess + vol, svc);

    [Fact]
    public void Service_charge_line_matches_the_worked_example()
    {
        // service charge sales 1134 -> tip out 3.2% = 36.29; service charge 226.80, card fee 3.97 => SVC 186.54
        var l = L(sc: 1134m, tipOutSc: 36.29m, gross: 226.80m, fee: 3.97m, svc: 226.80m - 3.97m - 36.29m);
        Assert.Equal(186.54m, l.Svc);
        Assert.Equal(186.54m, l.ServiceChargeLessTipOut);
    }

    [Fact]
    public void Total_is_the_three_pay_lines()
    {
        var l = L(ctp: 10m, cctLess: 20m, vol: 5m, svc: 30m);
        Assert.Equal(10m + 25m + 30m, l.Total);
    }

    [Fact]
    public void Parts_list_only_employees_with_activity_in_that_part()
    {
        var onlyVoluntary = L("A", cc: 100m, tipOutCc: 3.2m, cctLess: 10m);
        var onlySc = L("B", sc: 500m, gross: 100m, svc: 80m, staff: 2);
        var report = new TipReport([onlyVoluntary, onlySc]);
        Assert.Equal(["A"], report.Voluntary.Select(l => l.EmployeeName));
        Assert.Equal(["B"], report.Compulsory.Select(l => l.EmployeeName));
    }

    [Fact]
    public void Voluntary_tip_on_a_service_charge_check_belongs_to_the_service_charge_part()
    {
        var l = L("C", sc: 300m, gross: 60m, vol: 98.25m, svc: 50m);
        var report = new TipReport([l]);
        Assert.Single(report.Compulsory);
        Assert.Empty(report.Voluntary);
    }

    [Fact]
    public void Totals_add_each_column()
    {
        var t = TipTotals.Of([L(cash: 1, cc: 2, sc: 3, tipOutCc: .5m, tipOutSc: .25m, gross: 10, fee: 1, vol: 2, ctp: 4, cctLess: 5, svc: 6),
                              L(cash: 10, cc: 20, sc: 30, tipOutCc: 5m, tipOutSc: 2.5m, gross: 100, fee: 10, vol: 20, ctp: 40, cctLess: 50, svc: 60, staff: 2)]);
        Assert.Equal(11m, t.CashSales); Assert.Equal(22m, t.CreditCardSales); Assert.Equal(33m, t.VoluntarySales);
        Assert.Equal(33m, t.ServiceChargeSales); Assert.Equal(5.5m, t.TipOutOnCashCreditSales); Assert.Equal(2.75m, t.TipOutOnServiceChargeSales);
        Assert.Equal(110m, t.ServiceChargeGross); Assert.Equal(11m, t.ServiceChargeFee); Assert.Equal(22m, t.VoluntaryTipOnServiceCharge);
        Assert.Equal(44m, t.Ctp); Assert.Equal(55m, t.CreditCardTipsLessTipOut); Assert.Equal(66m, t.Svc);
        Assert.Equal(55m + 22m, t.Cct);
    }
}
