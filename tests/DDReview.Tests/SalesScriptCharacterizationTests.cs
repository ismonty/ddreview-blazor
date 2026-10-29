using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DDReview.Core.Sales.Legacy;

namespace DDReview.Tests;

/// <summary>
/// Characterization tests: the ported generator must reproduce the scripts the legacy app actually logged.
/// Set DDREVIEW_SALES_SCRIPTS to the folder of exported .sql files (skipped when unset).
/// </summary>
public class SalesScriptCharacterizationTests
{
    sealed class StubLookups(bool discountsCalculated) : ISalesLookups
    {
        public DataTable GetTable(string name, string sql)
        {
            var t = new DataTable(name);
            if (name == "DayPartTable")
            {
                t.Columns.Add("StartTime", typeof(short));
                t.Columns.Add("EndTime", typeof(short));
                t.Columns.Add("DayPartID", typeof(short));
                t.Columns.Add("DayPart", typeof(string));
                t.Rows.Add((short)360, (short)600, (short)1, "Breakfast");
                t.Rows.Add((short)600, (short)960, (short)2, "Lunch");
                t.Rows.Add((short)960, (short)1440, (short)3, "Dinner");
                t.Rows.Add((short)0, (short)360, (short)3, "Dinner");
            }
            else if (name == "tblCashAndSalesBatches")
            {
                t.Columns.Add("BatchID", typeof(int));
                if (discountsCalculated) t.Columns.Add("DiscountsCalculated", typeof(bool));
            }
            else
            {
                t.Columns.Add("SourceStoreID", typeof(int));
                t.Columns.Add("SourceProfitCenterID", typeof(int));
                t.Columns.Add("DestinationStoreID", typeof(int));
                t.Columns.Add("DestinationProfitCenterItemID", typeof(int));
            }
            return t;
        }
    }

    static readonly Regex SetLine = new(@"^\s*set @(\w+)=(.*)$", RegexOptions.Multiline);

    public static DataAccessSalesReview ParseRequest(string script, bool discountsCalculated)
    {
        var r = new DataAccessSalesReview("") { Lookups = new StubLookups(discountsCalculated) };
        int cs = 0, ce = 0;
        foreach (Match m in SetLine.Matches(script))
        {
            string name = m.Groups[1].Value, val = m.Groups[2].Value.TrimEnd('\r', ' ');
            switch (name)
            {
                case "IncludeVoids": break; // derived by the generator
                case "CompareStartDayNumber": cs = int.Parse(val, CultureInfo.InvariantCulture); break;
                case "CompareEndDayNumber": ce = int.Parse(val, CultureInfo.InvariantCulture); break;
                default:
                    var f = typeof(DataAccessSalesReview).GetField(name);
                    if (f == null) break;
                    if (f.FieldType == typeof(string)) f.SetValue(r, val.Trim('\'').Replace("''", "'"));
                    else f.SetValue(r, int.Parse(val, CultureInfo.InvariantCulture));
                    break;
            }
        }
        r.OffsetDays = unchecked(cs - r.StartDayNumber);
        return r;
    }

    public static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    [Fact]
    public void Generator_reproduces_logged_scripts()
    {
        var dir = Environment.GetEnvironmentVariable("DDREVIEW_SALES_SCRIPTS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var outDir = Environment.GetEnvironmentVariable("DDREVIEW_SALES_DIFFOUT");
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.sql").OrderBy(x => x))
        {
            var logged = File.ReadAllText(file);
            string? best = null; int bestLen = int.MaxValue;
            foreach (var dc in new[] { true })
            {
                var req = ParseRequest(logged, dc);
                var gen = req.BuildDataScript(true);
                if (Norm(gen) == Norm(logged)) { best = null; bestLen = 0; break; }
                if (best == null || Math.Abs(gen.Length - logged.Length) < bestLen) { best = gen; bestLen = Math.Abs(gen.Length - logged.Length); }
            }
            if (bestLen != 0)
            {
                failures.Add(Path.GetFileName(file) + " (len diff " + bestLen + ")");
                if (outDir != null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, Path.GetFileName(file) + ".gen"), best!); }
            }
        }
        Assert.True(failures.Count == 0, failures.Count + " mismatches:\n" + string.Join("\n", failures));
    }
}
