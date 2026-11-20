namespace DDReview.Core.Home;

/// <summary>
/// How the dashboard's sales-by-store chart stacks its series, as the classic (Dundas) chart did: Discounts sit on top of Net sales,
/// Labor sits on top of Theoretical Product Cost, and Last Year, Budget and Purchases stand alone.
/// </summary>
public static class ChartStacks
{
    /// <summary>Height of the tallest column among the stacks for one store (or the total), so the axis leaves room for stacked bars.</summary>
    public static double Max(IReadOnlyDictionary<string, double> v)
    {
        double G(string k) => v.TryGetValue(k, out var d) && !double.IsNaN(d) ? d : 0;
        return Math.Max(Math.Max(G("Last Year"), G("Budget")),
               Math.Max(Math.Max(G("Net") + G("Discounts"), G("Theoretical Product Cost") + G("Labor")), G("Purchases")));
    }
}
