using System.Data;
using System.Globalization;

namespace DDReview.Core.Home;

/// <summary>One store (or the total) on the Daily Summary tab, with the old page's ratios.</summary>
public sealed class DailySummaryLine
{
    public int StoreNumber; public string StoreName = ""; public DateTime? MaxDateProcessed;
    public decimal Sales, PreviousSales, Budget, Gross, Discounts, Voids, Labor, ProductCost, Purchases, Checks, PreviousChecks;
    public decimal[] DayPart = new decimal[6], PreviousDayPart = new decimal[6];   // 1..4, drive through, catering

    public decimal VsPrevious => Sales - PreviousSales;
    public decimal VsPreviousPct => Ratio(Sales - PreviousSales, PreviousSales);
    public decimal VsBudget => Sales - Budget;
    public decimal VsBudgetPct => Ratio(Sales - Budget, Budget);
    public decimal OfGross(decimal x) => Ratio(x, Gross);
    public decimal ChecksPct => Ratio(Checks - PreviousChecks, PreviousChecks);
    public decimal DayPartPct(int i) => Ratio(DayPart[i] - PreviousDayPart[i], PreviousDayPart[i]);

    static decimal Ratio(decimal a, decimal b) => b == 0 ? 0 : a / b;

    public static readonly string[] DayPartNames = ["Day Part 1", "Day Part 2", "Day Part 3", "Day Part 4", "Drive Up", "Catering"];
    static readonly string[] DayPartColumns = ["DayPart1Sales", "DayPart2Sales", "DayPart3Sales", "DayPart4Sales", "DriveThroughSales", "CateringSales"];

    public static DailySummaryLine From(DataRow r)
    {
        decimal N(string c) => r.Table.Columns.Contains(c) && r[c] is not DBNull ? Convert.ToDecimal(r[c], CultureInfo.InvariantCulture) : 0m;
        var l = new DailySummaryLine
        {
            StoreNumber = (int)N("StoreNumber"),
            StoreName = r.Table.Columns.Contains("StoreName") && r["StoreName"] is not DBNull ? r["StoreName"].ToString()!.Trim() : "",
            MaxDateProcessed = r.Table.Columns.Contains("MaxDateProcessed") && r["MaxDateProcessed"] is DateTime d ? d : null,
            Sales = N("TotalSales"), PreviousSales = N("PreviousTotalSales"), Budget = N("TotalSalesBudget"), Gross = N("TotalGrossSales"),
            Discounts = N("TotalDiscounts"), Voids = N("TotalVoids"), Labor = N("TotalLabor"), ProductCost = N("TotalProductCost"),
            Purchases = N("TotalPurchases"), Checks = N("CheckCount"), PreviousChecks = N("PreviousCheckCount"),
        };
        for (int i = 0; i < 6; i++) { l.DayPart[i] = N(DayPartColumns[i]); l.PreviousDayPart[i] = N("Previous" + DayPartColumns[i]); }
        return l;
    }

    /// <summary>The total row: column sums, gross = sales minus (negative) discounts as the old code did.</summary>
    public static DailySummaryLine Total(IEnumerable<DailySummaryLine> lines)
    {
        var t = new DailySummaryLine();
        foreach (var l in lines)
        {
            t.Sales += l.Sales; t.PreviousSales += l.PreviousSales; t.Budget += l.Budget; t.Discounts += l.Discounts; t.Voids += l.Voids;
            t.Labor += l.Labor; t.ProductCost += l.ProductCost; t.Purchases += l.Purchases; t.Checks += l.Checks; t.PreviousChecks += l.PreviousChecks;
            for (int i = 0; i < 6; i++) { t.DayPart[i] += l.DayPart[i]; t.PreviousDayPart[i] += l.PreviousDayPart[i]; }
        }
        t.Gross = t.Sales - t.Discounts;
        return t;
    }

    public static string Money(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
    public static string Pct(decimal ratio) => (ratio * 100m).ToString("N2", CultureInfo.InvariantCulture) + " %";
}

/// <summary>The dashboard gauges: percentages of gross, and the round-number scale each gauge gets.</summary>
public sealed class DashboardTotals
{
    public decimal Sales, PreviousSales, Discounts, Labor, Budget, Voids, ProductCost, Purchases, Gross;
    public decimal Base => Sales + Discounts;                       // discounts are stored negative in the old code path (it negates first)
    public double VoidPct => Pct(Voids); public double DiscountPct => Pct(Discounts);
    public double LaborPct => Pct(Labor); public double CostPct => Pct(ProductCost); public double PurchasesPct => Pct(Purchases);
    double Pct(decimal v) => Base == 0 ? 0 : Math.Abs((double)(100m * v / Base));

    public static DashboardTotals From(DataTable t)
    {
        var d = new DashboardTotals();
        decimal Sum(string c) => t.Columns.Contains(c) ? t.Rows.Cast<DataRow>().Sum(r => r[c] is DBNull ? 0m : Convert.ToDecimal(r[c], CultureInfo.InvariantCulture)) : 0m;
        d.Sales = Sum("TotalSales"); d.PreviousSales = Sum("PreviousTotalSales"); d.Discounts = -Sum("TotalDiscounts");
        d.Labor = Sum("TotalLabor"); d.Budget = Sum("TotalSalesBudget"); d.Voids = Sum("TotalVoids");
        d.ProductCost = Sum("TotalProductCost"); d.Purchases = Sum("TotalPurchases"); d.Gross = Sum("TotalGrossSales");
        return d;
    }

    /// <summary>A round gauge maximum above <paramref name="value"/> (1, 2, 2.5, 5 or 10 times a power of ten); 100 when there is nothing.</summary>
    public static double NiceMax(double value)
    {
        if (double.IsNaN(value) || value <= 0) return 100;
        var v = value * 1.05;
        var p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * p >= v) return m * p;
        return 10 * p;
    }
}

/// <summary>A section of the This Week / Last Week grid (SectionNumber / SectionName, rows Descriptor + D0..D6 + LineTotal).</summary>
public sealed record NumbersSection(int Number, string Name, string StoreName, DateTime? Start, DateTime? End, List<DataRow> Rows)
{
    public static List<NumbersSection> From(DataTable t)
    {
        var list = new List<NumbersSection>();
        foreach (DataRow r in t.Rows)
        {
            if (r["SectionNumber"] is DBNull) continue;
            var n = Convert.ToInt32(r["SectionNumber"], CultureInfo.InvariantCulture);
            if (list.Count == 0 || list[^1].Number != n)
                list.Add(new NumbersSection(n, r["SectionName"] as string ?? "", r["StoreName"] as string ?? "",
                    r["StartingDate"] as DateTime?, r["EndingDate"] as DateTime?, []));
            list[^1].Rows.Add(r);
        }
        return list;
    }

    /// <summary>The "MM/dd" + weekday labels for the seven day columns: the grid starts on a Monday, rotated to the first date.</summary>
    public static List<(string Date, string Day)> DayHeaders(DateTime? start)
    {
        var s = start ?? DateTime.Today;
        var dayZero = new DateTime(2003, 9, 29);
        var offset = (int)((s - dayZero).TotalDays % 7);
        var res = new List<(string, string)>();
        for (int i = 0; i < 7; i++)
        {
            var td = i < offset ? s.AddDays(i - offset + 7) : s.AddDays(i - offset);
            res.Add((td.ToString("MM/dd", CultureInfo.InvariantCulture), dayZero.AddDays(i).ToString("ddd", CultureInfo.InvariantCulture)));
        }
        return res;
    }
}
