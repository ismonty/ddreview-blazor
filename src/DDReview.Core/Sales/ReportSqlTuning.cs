using System.Text;
using System.Text.RegularExpressions;

namespace DDReview.Core.Sales;

/// <summary>
/// Three mechanical speed-ups applied to the generated Sales / Receipts Review script just before it is sent (the legacy generator is untouched, so
/// <c>BuildScript</c> still returns exactly what it always did):
/// <list type="number">
/// <item>every <c>declare @x table (...)</c> becomes a temp table <c>#x</c> (table variables are planned as one row at this server's compatibility level);
/// character columns get <c>collate database_default</c> so comparisons with real tables behave as before even if tempdb's collation differs</item>
/// <item><c>fn_CalcDayNumberDate(n)</c>, a scalar function that runs once per row, becomes <c>dateadd(d, n, @DDR_DayZero)</c> with the base date read once</item>
/// <item><c>fn_CreateKeyTableValueFromCSVString(@list)</c>, a T-SQL loop, becomes an inline XML split (same <c>KeyValue</c> column, blanks ignored)</item>
/// </list>
/// Set the environment variable DDREVIEW_NO_REPORT_TUNING=1 to switch all three off.
/// </summary>
public static class ReportSqlTuning
{
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("DDREVIEW_NO_REPORT_TUNING") != "1";

    public static string Apply(string sql)
    {
        if (!Enabled || string.IsNullOrEmpty(sql)) return sql;
        sql = TempTables(sql);
        sql = DayNumberDates(sql);
        sql = CsvSplits(sql);
        return sql;
    }

    // ---- 1) table variables -> temp tables --------------------------------------------------------------------------------------------------

    static readonly Regex DeclareTable = new(@"declare\s+@(?<n>\w+)\s+table\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex CharColumn = new(@"\b(?<t>(?:n?var)?char)\s*\(\s*(?<len>\d+|max)\s*\)(?!\s+collate)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string TempTables(string sql)
    {
        // only names whose declaration has a closing parenthesis we can find, and that do not already exist as a #temp table
        var names = DeclareTable.Matches(sql).Cast<Match>()
            .Where(m => MatchingParen(sql, m.Index + m.Length - 1) >= 0)
            .Select(m => m.Groups["n"].Value).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => !Regex.IsMatch(sql, @"#" + Regex.Escape(n) + @"\b", RegexOptions.IgnoreCase)).ToList();
        if (names.Count == 0) return sql;

        foreach (var n in names)
            sql = Regex.Replace(sql, @"(?<![@\w])@" + Regex.Escape(n) + @"\b", "#" + n, RegexOptions.IgnoreCase);

        // declaration -> drop-if-exists + create table, working from the end so earlier offsets stay valid
        var decl = new Regex(@"declare\s+#(?<n>" + string.Join("|", names.Select(Regex.Escape)) + @")\s+table\s*\(", RegexOptions.IgnoreCase);
        foreach (var m in decl.Matches(sql).Cast<Match>().Reverse())
        {
            int open = m.Index + m.Length - 1;
            int close = MatchingParen(sql, open);
            if (close < 0) continue;
            var body = sql.Substring(open + 1, close - open - 1);
            body = CharColumn.Replace(body, "${t}(${len}) collate database_default");
            var n = m.Groups["n"].Value;
            var repl = "if object_id('tempdb..#" + n + "') is not null drop table #" + n + "\ncreate table #" + n + " (" + body + ")";
            sql = sql.Substring(0, m.Index) + repl + sql.Substring(close + 1);
        }
        return sql;
    }

    // ---- 2) fn_CalcDayNumberDate -> dateadd ---------------------------------------------------------------------------------------------------

    static readonly Regex DayFn = new(@"(?:\[?dbo\]?\.)?\[?fn_CalcDayNumberDate\]?\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string DayNumberDates(string sql)
    {
        var ms = DayFn.Matches(sql).Cast<Match>().Reverse().ToList();
        if (ms.Count == 0) return sql;
        int done = 0;
        foreach (var m in ms)
        {
            int open = m.Index + m.Length - 1;
            int close = MatchingParen(sql, open);
            if (close < 0) continue;
            var arg = sql.Substring(open + 1, close - open - 1).Trim();
            if (arg.Length == 0) continue;
            sql = sql.Substring(0, m.Index) + "dateadd(d, (" + arg + "), @DDR_DayZero)" + sql.Substring(close + 1);
            done++;
        }
        if (done == 0) return sql;
        return "declare @DDR_DayZero datetime = dbo.fn_CalcWeekNumberBaseDate();\n" + sql;
    }

    // ---- 3) CSV splitter -> inline XML split --------------------------------------------------------------------------------------------------

    static readonly Regex CsvFn = new(@"(?:\[?dbo\]?\.)?\[?fn_CreateKeyTableValueFromCSVString\]?\s*\(\s*(?<arg>@\w+)\s*\)(?<alias>\s+(?:as\s+)?(?:csv|kv|rcl)\b)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string CsvSplits(string sql)
    {
        int n = 0;
        return CsvFn.Replace(sql, m =>
        {
            var a = m.Groups["arg"].Value;
            n++;
            var alias = m.Groups["alias"].Success ? m.Groups["alias"].Value : " as DDR_split" + n;
            return "(select try_convert(int, nullif(x.i.value('.', 'varchar(20)'), '')) as KeyValue "
                 + "from (select cast('<i>' + replace(replace(replace(" + a + ", ' ', ''), ';', ','), ',', '</i><i>') + '</i>' as xml) as doc) s "
                 + "cross apply s.doc.nodes('/i') x(i) "
                 + "where try_convert(int, nullif(x.i.value('.', 'varchar(20)'), '')) is not null)" + alias;
        });
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Index of the ')' that closes the '(' at <paramref name="open"/> (single-quoted strings are skipped), or -1.</summary>
    static int MatchingParen(string s, int open)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr) { if (c == '\'') inStr = false; continue; }
            if (c == '\'') { inStr = true; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { while (i < s.Length && s[i] != '\n') i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); if (e < 0) return -1; i = e + 1; continue; }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }
}
