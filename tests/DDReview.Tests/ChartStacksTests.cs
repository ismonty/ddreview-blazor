using DDReview.Core.Home;

namespace DDReview.Tests;

public class ChartStacksTests
{
    static Dictionary<string, double> V(double lastYear = 0, double budget = 0, double net = 0, double disc = 0, double prod = 0, double labor = 0, double purch = 0)
        => new() { ["Last Year"] = lastYear, ["Budget"] = budget, ["Net"] = net, ["Discounts"] = disc,
                   ["Theoretical Product Cost"] = prod, ["Labor"] = labor, ["Purchases"] = purch };

    [Fact] public void Discounts_stack_on_net() => Assert.Equal(1300, ChartStacks.Max(V(net: 1000, disc: 300)));
    [Fact] public void Labor_stacks_on_product_cost() => Assert.Equal(900, ChartStacks.Max(V(prod: 600, labor: 300, net: 100)));
    [Fact] public void Standalone_series_count_on_their_own() => Assert.Equal(2000, ChartStacks.Max(V(lastYear: 2000, budget: 1500, net: 800, disc: 100, purch: 1900)));
    [Fact] public void Empty_or_missing_values_are_zero()
    {
        Assert.Equal(0, ChartStacks.Max(new Dictionary<string, double>()));
        Assert.Equal(5, ChartStacks.Max(new Dictionary<string, double> { ["Net"] = 5, ["Discounts"] = double.NaN }));
    }
}

public class DailySummaryStackTests
{
    [Fact]
    public void Stack_ends_after_net_discounts_and_voids_whatever_their_sign()
    {
        Assert.Equal(1150m, new DailySummaryLine { Sales = 1000, Discounts = -100, Voids = 50 }.StackedSalesEnd);   // discounts are stored negative
        Assert.Equal(1150m, new DailySummaryLine { Sales = 1000, Discounts = 100, Voids = -50 }.StackedSalesEnd);
        Assert.Equal(1000m, new DailySummaryLine { Sales = 1000 }.StackedSalesEnd);
    }
}
