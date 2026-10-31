using System.Globalization;
using System.Text.RegularExpressions;
using DDReview.Core.Receipts.Legacy;

namespace DDReview.Tests;

/// <summary>Receipts Review generator vs. scripts captured from the live app. Set DDREVIEW_RECEIPTS_SCRIPTS to a folder of .sql files.</summary>
public class ReceiptsScriptCharacterizationTests
{
    static readonly Regex SetLine = new(@"^\s*set @(\w+)\s*=\s*(.*)$", RegexOptions.Multiline);
    static readonly (string, string)[] Calc =
    [
        ("TotalTipsVsNetPercentageAmount", "case when isnull(TotalPayrollSalesAmount,0)<>0 then TotalTipsAmount/TotalPayrollSalesAmount else null end"),
        ("TotalDiscountPercentage", "case when isnull(TotalSalesAmount,0)<>0 then TotalDiscountAmount/TotalSalesAmount else null end"),
        ("AverageNetSalesPerTable", "case when isnull(TotalCheckCount,0)<>0 then TotalNetReceiptAmount/TotalCheckCount else null end"),
        ("AverageNetSalesPerGuest", "case when isnull(TotalDeclaredCoverCount,0)<>0 then TotalNetReceiptAmount/TotalDeclaredCoverCount else null end"),
        ("AverageTipPerTable", "case when isnull(TotalCheckCount,0)<>0 then TotalTipsAmount/TotalCheckCount else null end"),
        ("AverageTipGuest", "case when isnull(TotalDeclaredCoverCount,0)<>0 then TotalTipsAmount/TotalDeclaredCoverCount else null end"),
    ];

    public static DataAccessReceiptsReview ParseRequest(string script)
    {
        var r = new DataAccessReceiptsReview("");
        foreach (var (n, e) in Calc) r.AddCalculatedField(n, e);
        foreach (Match m in SetLine.Matches(script))
        {
            string name = m.Groups[1].Value, val = m.Groups[2].Value.TrimEnd('\r', ' ');
            var f = typeof(DataAccessReceiptsReview).GetField(name);
            if (f == null) continue;
            if (f.FieldType == typeof(string)) f.SetValue(r, val.Trim('\'').Replace("''", "'"));
            else f.SetValue(r, int.Parse(val, CultureInfo.InvariantCulture));
        }
        return r;
    }

    static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    [Fact]
    public void Generator_reproduces_captured_scripts()
    {
        var dir = Environment.GetEnvironmentVariable("DDREVIEW_RECEIPTS_SCRIPTS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        var outDir = Environment.GetEnvironmentVariable("DDREVIEW_RECEIPTS_DIFFOUT");
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.sql").OrderBy(x => x))
        {
            var logged = Norm(File.ReadAllText(file));
            bool ok = false; string? last = null;
            foreach (var pc in new[] { true, false })
            foreach (var deposits in new[] { false, true })
            {
                var gen = ParseRequest(logged.Length == 0 ? "" : File.ReadAllText(file)).BuildDataScript(pc, deposits, 0);
                last = gen;
                if (Norm(gen) == logged) { ok = true; break; }
            }
            if (!ok)
            {
                failures.Add(Path.GetFileName(file));
                if (outDir != null) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, Path.GetFileName(file) + ".gen"), last!); }
            }
        }
        Assert.True(failures.Count == 0, "Mismatches: " + string.Join(", ", failures));
    }
}

public class ReceiptsLayoutTests
{
    static System.Data.DataTable LoadTsv(string path)
    {
        var lines = File.ReadAllLines(path);
        var heads = lines[0].Split('\t');
        var vals = lines[1].Split('\t');
        var t = new System.Data.DataTable();
        var seen = new HashSet<string>();
        for (int i = 0; i < heads.Length; i++) t.Columns.Add(seen.Add(heads[i]) ? heads[i] : heads[i] + "_dup" + i, typeof(string));
        var row = t.NewRow();
        for (int i = 0; i < vals.Length; i++) row[i] = vals[i];
        t.Rows.Add(row);
        // re-type numerics so Format() sees numbers
        var nt = new System.Data.DataTable();
        foreach (System.Data.DataColumn c in t.Columns) nt.Columns.Add(c.ColumnName, typeof(object));
        var nr = nt.NewRow();
        for (int i = 0; i < vals.Length; i++)
            nr[i] = decimal.TryParse(vals[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : DBNull.Value;
        nt.Rows.Add(nr);
        return nt;
    }

    [Fact]
    public void Stacked_columns_reproduce_the_live_screen()
    {
        var p = Environment.GetEnvironmentVariable("DDREVIEW_RECEIPTS_RESULT");
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) return;
        var row = LoadTsv(p).Rows[0];
        string[][] expected =
        [
            ["35,618.81","0.00","35,618.81","23,178.97","0.00","23,178.97"],
            ["14,618.70","6,056.18","2,110.11","393.98","0.00"],
            ["23,178.97","0.00","0.00"],
            ["35,618.81","448.49","11,991.35","23,178.97","31,302.31","0.00"],
            ["39,414.40","-3,439.59","-356.00","35,618.81","356.00","35,974.81"],
            ["0.00","-2,783.15","0.00","-305.00","0.00","-1,584.35"],
            ["32,223.22","28,958.06","2,743.61","18.00","-417.36","31,302.31"],
            ["2.86%","3,820.59","3,439.59","381.00","31,284.31","12.21%"],
            ["0","648","636","0.00","14,786.82","23.26%"],
            ["0.00","48.31","49.22","0.00","5.90","6.01"],
        ];
        var cols = DDReview.Core.Receipts.ReceiptsLayout.Columns;
        Assert.Equal(expected.Length, cols.Count);
        for (int c = 0; c < cols.Count; c++)
            Assert.Equal(expected[c], cols[c].Lines.Select(l => DDReview.Core.Receipts.ReceiptsLayout.Format(row, l)).ToArray());
    }
}
