using System.Data;
using System.Globalization;

namespace DDReview.Core.Sales;

public enum SalesCellKind { Text, Number, Percent }

/// <summary>One visible column of the Sales Review grid. Percent columns divide Numerator by Denominator.</summary>
public sealed record SalesColumn(string Header, string Column, SalesCellKind Kind = SalesCellKind.Number,
    string? Denominator = null, string? ShadeColumn = null, string? ToolTipColumn = null);

/// <summary>
/// Which columns the old Sales Review grid showed for a given set of "group by" choices
/// (SalesCostsReviewPage column definitions and the Page_Load DisplayVoids / DisplayByStore rules).
/// Sales mode only; the cost columns belong to the later Costs review.
/// </summary>
public static class SalesLayout
{
    private static readonly string[] DayHeaders = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static List<SalesColumn> Build(SalesDimensions dims, DataTable t, bool showWeekdays = true)
    {
        bool Has(string c) => t.Columns.Contains(c);
        bool On(SalesDimensions d) => (dims & d) != 0;
        var cols = new List<SalesColumn>();
        void Text(string header, string col) { if (Has(col)) cols.Add(new SalesColumn(header, col, SalesCellKind.Text)); }

        if (On(SalesDimensions.StoreGroup)) Text("Store Group", "StoreGroup");
        if (On(SalesDimensions.Store)) { Text("Store #", "StoreNumber"); Text("Store Name", "StoreName"); }
        if (On(SalesDimensions.ProfitCenter)) Text("Profit Center", "ProfitCenter");
        if (On(SalesDimensions.Week)) Text("Week Of", "WeekName");
        if (On(SalesDimensions.WeekDay)) Text("Week Day", "WeekDayName");
        if (On(SalesDimensions.Day)) Text("Day", "DayName");
        if (On(SalesDimensions.StoreStaff)) Text("Server", "StoreStaff");
        if (On(SalesDimensions.CheckNumber)) Text("Check Number", "CheckNumber");
        if (On(SalesDimensions.MainType)) Text("Main Type", "MainType");
        if (On(SalesDimensions.MainGroup)) Text("Main Group", "MainGroup");
        if (On(SalesDimensions.SaleType)) Text("Sale Type", "SaleType");
        if (On(SalesDimensions.MenuItem)) Text("Menu Item", "MenuItem");
        if (On(SalesDimensions.MenuItemPLU)) Text("PLU", "MenuItemPLU");
        if (On(SalesDimensions.Discount)) Text("Discount", "Discount");
        if (On(SalesDimensions.DayPart)) Text("Day Part", "DayPart");
        if (On(SalesDimensions.DayHour)) Text("Day Hour", "DayHourName");

        // The Mon..Sun columns make no sense when the rows are already days, week days or checks.
        bool days = showWeekdays && !On(SalesDimensions.Day) && !On(SalesDimensions.WeekDay) && !On(SalesDimensions.CheckNumber);
        if (days)
            for (int i = 0; i < 7; i++)
                if (Has("D" + (i + 1))) cols.Add(new SalesColumn(DayHeaders[i], "D" + (i + 1)));

        cols.Add(new SalesColumn("Sales Total", "TotalSales"));
        cols.Add(new SalesColumn("Discounts", "Discounts"));
        cols.Add(new SalesColumn("Net", "NetSales"));
        cols.Add(new SalesColumn("Quantity", "Quantity"));

        bool byStore = On(SalesDimensions.Store) || On(SalesDimensions.StoreGroup);
        if (byStore)
            for (int n = 1; n <= 4; n++)
            {
                string label = FirstText(t, $"RoyaltyColumn{n}Label");
                if (label.Length > 0 && Has($"RoyaltyColumn{n}Amount"))
                    cols.Add(new SalesColumn(label, $"RoyaltyColumn{n}Amount", SalesCellKind.Number, null,
                        Has($"RoyaltyColumn{n}DisplayAttribute") ? $"RoyaltyColumn{n}DisplayAttribute" : null,
                        Has("RoyaltyToolTip") ? "RoyaltyToolTip" : null));
            }

        // DisplayVoids: by store or store group and nothing finer.
        const SalesDimensions finer = SalesDimensions.Week | SalesDimensions.WeekDay | SalesDimensions.Day | SalesDimensions.DayPart
            | SalesDimensions.DayHour | SalesDimensions.ProfitCenter | SalesDimensions.MainType | SalesDimensions.MainGroup
            | SalesDimensions.SaleType | SalesDimensions.MenuItem | SalesDimensions.MenuItemPLU | SalesDimensions.Discount
            | SalesDimensions.Special | SalesDimensions.StoreStaff | SalesDimensions.CheckNumber;
        if (byStore && (dims & finer) == 0)
        {
            if (Has("TotalVoids"))
            {
                cols.Add(new SalesColumn("Voids", "TotalVoids"));
                cols.Add(new SalesColumn("Void %", "TotalVoids", SalesCellKind.Percent, "TotalSales"));
            }
            if (Has("TotalRefunds"))
            {
                cols.Add(new SalesColumn("Refunds", "TotalRefunds"));
                cols.Add(new SalesColumn("Refund %", "TotalRefunds", SalesCellKind.Percent, "TotalSales"));
            }
            string[] charge = ["TotalSelectedCharges1", "TotalSelectedCharges2", "TotalSelectedCharges0"];
            string[] head = ["Selected Charges 1", "Selected Charges 2", "All Other Charges"];
            for (int i = 0; i < 3; i++)
                if (Has(charge[i]) && SumOf(t, charge[i]) != 0) cols.Add(new SalesColumn(head[i], charge[i]));
        }
        return cols.Where(c => Has(c.Column) && (c.Denominator is null || Has(c.Denominator))).ToList();
    }

    /// <summary>The bold last row: column sums, with percent columns recomputed from the sums. One row per CompareIndex.</summary>
    public static List<(object? Index, Dictionary<string, decimal> Sums)> Totals(DataTable t, IEnumerable<SalesColumn> cols)
    {
        var result = new List<(object?, Dictionary<string, decimal>)>();
        var groups = t.Columns.Contains("CompareIndex")
            ? t.Rows.Cast<DataRow>().GroupBy(r => r["CompareIndex"] is DBNull ? null : r["CompareIndex"]).OrderBy(g => g.Key is null ? 0 : Convert.ToInt32(g.Key))
            : t.Rows.Cast<DataRow>().GroupBy(_ => (object?)null);
        foreach (var g in groups)
        {
            var sums = new Dictionary<string, decimal>();
            foreach (var c in cols.Where(c => c.Kind != SalesCellKind.Text))
                foreach (var name in new[] { c.Column, c.Denominator }.Where(n => n is not null).Cast<string>())
                    if (!sums.ContainsKey(name)) sums[name] = g.Sum(r => ToDecimal(r[name]));
            result.Add((g.Key, sums));
        }
        return result;
    }

    public static string FormatNumber(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
    public static string FormatPercent(decimal num, decimal den) => den == 0 ? "" : (num / den * 100m).ToString("N2", CultureInfo.InvariantCulture) + "%";

    public static string Format(DataRow row, SalesColumn c)
    {
        var v = row[c.Column];
        if (v is DBNull) return "";
        return c.Kind switch
        {
            SalesCellKind.Text => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "",
            SalesCellKind.Percent => FormatPercent(ToDecimal(v), ToDecimal(row[c.Denominator!])),
            _ => FormatNumber(ToDecimal(v)),
        };
    }

    private static decimal ToDecimal(object v) => v is DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    private static decimal SumOf(DataTable t, string col) => t.Rows.Cast<DataRow>().Sum(r => ToDecimal(r[col]));
    private static string FirstText(DataTable t, string col)
    {
        if (!t.Columns.Contains(col)) return "";
        foreach (DataRow r in t.Rows) if (r[col] is not DBNull && !string.IsNullOrWhiteSpace(Convert.ToString(r[col]))) return Convert.ToString(r[col])!.Trim();
        return "";
    }
}
