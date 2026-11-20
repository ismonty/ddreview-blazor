using DDReview.Core.Gl;

namespace DDReview.Tests;

public class GlPostingGateTests
{
    static GlBalanceReport Balanced() => new(100m, 100m, [new BatchBalance(1, new DateTime(2026, 10, 1), 100m, 100m, 4, false)]);
    static GlBalanceReport OneBatchOff() => new(100m, 100m, [
        new BatchBalance(1, new DateTime(2026, 10, 1), 60m, 50m, 3, false),
        new BatchBalance(2, new DateTime(2026, 10, 2), 40m, 50m, 3, false)]);

    [Fact] public void No_report_means_no_data() => Assert.False(GlPostingGate.HasData(null, false));
    [Fact] public void A_report_on_the_page_is_data() => Assert.True(GlPostingGate.HasData(Balanced(), false));
    [Fact] public void A_run_in_progress_is_not_data() => Assert.False(GlPostingGate.HasData(Balanced(), true));

    [Fact] public void A_balanced_report_asks_nothing() => Assert.Null(GlPostingGate.BalanceQuestion(Balanced()));
    [Fact] public void No_report_asks_nothing() => Assert.Null(GlPostingGate.BalanceQuestion(null));

    [Fact]
    public void A_batch_out_of_balance_is_asked_about_even_when_the_totals_match()
    {
        var q = GlPostingGate.BalanceQuestion(OneBatchOff());
        Assert.NotNull(q);
        Assert.Contains("2 batches do not balance", q);
    }

    [Fact]
    public void Totals_that_differ_are_asked_about()
    {
        var q = GlPostingGate.BalanceQuestion(new GlBalanceReport(100m, 90m, []));
        Assert.NotNull(q);
        Assert.Contains("do not match", q);
    }
}
