using DDReview.Payroll.Consolidated;
using Xunit;

namespace DDReview.Tests;

public class ConsolidatedTipOutTests
{
    static TipOutRow Row(decimal cash = 0, decimal cc = 0, decimal sc = 0, decimal tipOut = 0, decimal ccTips = 0, decimal scTips = 0, decimal cashTips = 0, decimal totalTips = 0, decimal notPaid = 0, decimal disc = 0) =>
        new(1, 13, 7, 3, "Servers", "SRV", "Smith, Pat", 1, cash, cc, sc, cash + cc + sc, cash + cc, 4m, tipOut, 0, 0, ccTips, 0, scTips, 0, ccTips + scTips, ccTips + scTips - tipOut, cashTips, totalTips, notPaid, disc, true);

    [Fact]
    public void Totals_add_each_column()
    {
        var t = TipOutTotals.Of([Row(cash: 10, cc: 20, sc: 5, tipOut: 1.4m, ccTips: 4, scTips: 1, cashTips: 2, totalTips: 5.6m, notPaid: -3.6m, disc: .5m),
                                 Row(cash: 1, cc: 2, sc: 3, tipOut: .24m, ccTips: 1, scTips: 1, cashTips: 0, totalTips: 1.76m, notPaid: -1.76m)]);
        Assert.Equal(11m, t.CashSales); Assert.Equal(22m, t.CreditCardSales); Assert.Equal(8m, t.ServiceChargeSales); Assert.Equal(41m, t.TotalSales);
        Assert.Equal(1.64m, t.TipOutPortionOfSales); Assert.Equal(5m, t.CreditCardTips); Assert.Equal(2m, t.ServiceChargeTips); Assert.Equal(2m, t.CashTips);
        Assert.Equal(7.36m, t.TotalTips); Assert.Equal(-5.36m, t.TipsNotPaid); Assert.Equal(.5m, t.DiscretionaryTips);
    }

    [Fact]
    public void Totals_of_nothing_are_zero() => Assert.Equal(0m, TipOutTotals.Of([]).TotalSales);

    static TipOutAdjustment Adj(int? outStaff = 5, int? outGroup = 2, int? inStaff = 6, int? inGroup = 3, decimal outPct = 0, decimal inPct = 0, int level = 3) =>
        new(0, 100, outStaff, outGroup, outPct, inStaff, inGroup, inPct, level, 0, 0, 0, 0, 0, 0);

    [Fact] public void A_move_between_two_people_is_valid() => Assert.Null(ConsolidatedTipOutService.Validate(Adj()));
    [Fact] public void An_add_only_line_is_valid() => Assert.Null(ConsolidatedTipOutService.Validate(Adj(outStaff: null, outGroup: null)));
    [Fact] public void A_subtract_only_line_is_valid() => Assert.Null(ConsolidatedTipOutService.Validate(Adj(inStaff: null, inGroup: null)));
    [Fact] public void A_line_with_nobody_is_refused() => Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(null, null, null, null)));
    [Fact] public void A_person_without_a_group_is_refused() { Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(outGroup: null))); Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(inGroup: null))); }
    [Fact] public void A_group_without_a_person_is_refused() { Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(outStaff: null))); Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(inStaff: null))); }
    [Theory] [InlineData(-1)] [InlineData(100.5)] public void Percent_out_of_range_is_refused(double pct) { Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(outPct: (decimal)pct))); Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(inPct: (decimal)pct))); }
    [Fact] public void Percent_at_the_edges_is_valid() => Assert.Null(ConsolidatedTipOutService.Validate(Adj(outPct: 0, inPct: 100)));
    [Theory] [InlineData(0)] [InlineData(99)] public void Source_level_out_of_range_is_refused(int level) => Assert.NotNull(ConsolidatedTipOutService.Validate(Adj(level: level)));
}
