using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DDReview.Core.Support;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Reports;

/// <summary>One entry of the client database's own fn_CustomReports_Index() (the Reports tab's Excel/Other export buttons).</summary>
public sealed record CustomReportInfo(int Id, string Name, string FunctionName, string FileName, bool Runnable, string? Problem);

public sealed record CustomReportResult(IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

/// <summary>
/// The custom reports of the old Reports tab (T&amp;A Export, Tips Export, ...). Each client database lists its own in dbo.fn_CustomReports_Index(): a name,
/// the name of a table function, and a download file name. The function takes (@StoreIDList varchar, @StartingDate datetime, @EndingDate datetime) and returns
/// the rows. Because the function name comes from the database, only a name that is in that index, is a plain dbo name, and has exactly those parameters is ever run.
/// </summary>
public sealed class CustomReportService
{
    static readonly Regex PlainName = new(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);

    /// <summary>"dbo.fn_X", "[dbo].[fn_X]" or "fn_X" -> "fn_X"; null when it is anything else (another schema, a bracket trick, a call with arguments).</summary>
    public static string? PlainFunctionName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace("[", "").Replace("]", "");
        var parts = s.Split('.');
        if (parts.Length == 2 && parts[0].Equals("dbo", StringComparison.OrdinalIgnoreCase)) s = parts[1];
        else if (parts.Length != 1) return null;
        return PlainName.IsMatch(s) ? s : null;
    }

    /// <summary>The reports this database offers. A database with no index function simply has none.</summary>
    public async Task<IReadOnlyList<CustomReportInfo>> ListAsync(string connectionString, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var list = new List<CustomReportInfo>();
        await using (var exists = new SqlCommand("select case when object_id(N'dbo.fn_CustomReports_Index') is null then 0 else 1 end", cn))
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 0) return list;

        var raw = new List<(int Id, string Name, string Fn, string File)>();
        await using (var cmd = new SqlCommand("select CustomReportID, CustomReportName, CustomReportSQLFunctionName, CustomReportExcelFileName from dbo.fn_CustomReports_Index() order by CustomReportID", cn) { CommandTimeout = 60 })
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
            while (await rd.ReadAsync(ct))
                raw.Add((Convert.ToInt32(rd[0]), Convert.ToString(rd[1]) ?? "", Convert.ToString(rd[2]) ?? "", Convert.ToString(rd[3]) ?? ""));

        foreach (var (id, name, fn, file) in raw)
        {
            var plain = PlainFunctionName(fn);
            string? problem = null;
            if (plain is null) problem = "The function name is not a plain dbo name.";
            else
            {
                // exactly the three parameters, in order (the multi-store daily summary style reports take others and are not offered here)
                await using var chk = new SqlCommand(@"
select p.name from sys.parameters p where p.object_id = object_id(@f) order by p.parameter_id", cn) { CommandTimeout = 60 };
                chk.Parameters.Add("@f", SqlDbType.NVarChar, 300).Value = "dbo." + plain;
                var names = new List<string>();
                await using (var rd = await chk.ExecuteReaderAsync(ct)) while (await rd.ReadAsync(ct)) names.Add(Convert.ToString(rd[0]) ?? "");
                if (names.Count == 0) problem = "The function was not found.";
                else if (!names.SequenceEqual(["@StoreIDList", "@StartingDate", "@EndingDate"], StringComparer.OrdinalIgnoreCase))
                    problem = "The function takes different parameters than a store list and two dates.";
            }
            list.Add(new CustomReportInfo(id, name, plain ?? fn, file, problem is null, problem));
        }
        return list;
    }

    /// <summary>Runs one report from the index. Stores are limited to the ones the user is authorized for; none selected means all of those.</summary>
    public async Task<CustomReportResult> RunAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int reportId,
        IReadOnlyCollection<int> selectedStoreIds, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (end < start) throw new ArgumentException("The end date is before the start date.");
        var report = (await ListAsync(connectionString, ct)).FirstOrDefault(r => r.Id == reportId) ?? throw new ArgumentException("That report is not in this database's list.");
        if (!report.Runnable) throw new ArgumentException(report.Problem ?? "That report cannot be run here.");
        var stores = (selectedStoreIds.Count == 0 ? authorizedStoreIds : selectedStoreIds.Where(authorizedStoreIds.Contains)).OrderBy(x => x).ToList();
        if (stores.Count == 0) throw new UnauthorizedAccessException("You are not authorized for any store.");

        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        // report.FunctionName passed PlainFunctionName in ListAsync and was confirmed to exist with the three parameters, so it is quoted and used as the name only.
        await using var cmd = new SqlCommand($"select * from dbo.{QuoteName(report.FunctionName)}(@s, @d1, @d2)", cn) { CommandTimeout = 600 };
        cmd.Parameters.Add("@s", SqlDbType.VarChar, -1).Value = string.Join(",", stores);
        cmd.Parameters.Add("@d1", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@d2", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        var cols = Enumerable.Range(0, rd.FieldCount).Select(i => (Index: i, Name: rd.GetName(i))).ToList();
        cols = DropRowCounter(cols);
        var headers = cols.Select(c => c.Name).ToList();
        var rows = new List<object?[]>();
        while (await rd.ReadAsync(ct))
            rows.Add(cols.Select(c => rd.IsDBNull(c.Index) ? null : rd.GetValue(c.Index)).ToArray());
        return new CustomReportResult(headers, rows);
    }

    static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>The report functions carry an identity column called ItemID so the rows keep their order; it is not part of the export.</summary>
    public static List<(int Index, string Name)> DropRowCounter(List<(int Index, string Name)> cols) =>
        cols.Count > 0 && cols[0].Name.Equals("ItemID", StringComparison.OrdinalIgnoreCase) ? cols.Skip(1).ToList() : cols;

    /// <summary>The download name: the index's file name, but .xlsx (the old .xls name is only a label), or .csv.</summary>
    public static string DownloadName(string indexFileName, bool csv)
    {
        var baseName = Path.GetFileNameWithoutExtension((indexFileName ?? "").Trim());
        foreach (var bad in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(bad, '_');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Export";
        return baseName + (csv ? ".csv" : ".xlsx");
    }

    public static byte[] ToXlsx(string sheetName, CustomReportResult r) =>
        XlsxWriter.Write(sheetName, null, r.Headers, r.Rows, plainIntegers: true);

    /// <summary>RFC 4180 CSV with a header row: nothing is quoted unless it has a comma, quote or line break; numbers use a period; empty cells stay empty.</summary>
    public static string ToCsv(CustomReportResult r)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", r.Headers.Select(Cell))).Append("\r\n");
        foreach (var row in r.Rows) sb.Append(string.Join(",", row.Select(Cell))).Append("\r\n");
        return sb.ToString();
    }

    static string Cell(object? v)
    {
        var s = v switch
        {
            null or DBNull => "",
            decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
        };
        return s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
