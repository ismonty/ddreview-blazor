using System.Globalization;
using System.Text;

namespace DDReview.Payroll.Adp;

/// <summary>
/// Port of the Access class clsEPIP: collects ADP "EPI" import records and writes the CSV files.
/// The column set is decided once for all records (a column exists if ANY record used it), the same as the old class.
/// </summary>
public sealed class EpipFile
{
    public enum Group { Hours3, Hours4, Hours5, Earnings3, Earnings4, Earnings5, Memo, Deduction }
    const int SlotsPerGroup = 4;
    static readonly string[] GroupTitle = ["Hours 3", "Hours 4", "Hours 5", "Earnings 3", "Earnings 4", "Earnings 5", "Memo", "Adjust DED"];
    // Field lengths of the old in-memory recordset. The old code would fail on a longer value; we say which one.
    static readonly Dictionary<string, int> MaxLen = new() { ["SortingData"] = 50, ["FileNameCode"] = 2, ["Co Code"] = 3, ["Batch Description"] = 20, ["Temp Dept"] = 6, ["Rate Code"] = 1, ["Code"] = 3 };

    internal sealed class Slot { public string? Code; public decimal? Amount; }

    public sealed class Record
    {
        internal string? SortingData, FileNameCode, TempDept, RateCode, BatchDescription;
        internal string CoCode = "";
        internal int BatchId;
        internal long FileNumber;
        internal decimal? TempRate, RegHours, RegEarnings, OtHours;
        internal readonly Slot[][] Slots = Enumerable.Range(0, 8).Select(_ => Enumerable.Range(0, SlotsPerGroup).Select(_ => new Slot()).ToArray()).ToArray();
    }

    readonly string globalCoCode;
    readonly string? batchDescription;
    readonly bool ssnAsPayrollId;
    readonly List<Record> records = [];
    readonly int[] used = new int[8];
    bool anyTempDept, anyTempRate, anyRateCode, anyRegHours, anyRegEarnings, anyOtHours;
    Record cur = null!;

    public int BatchId { get; private set; }

    public EpipFile(string coCode, int batchId, string? batchDescription, bool ssnAsPayrollId)
    {
        globalCoCode = coCode; BatchId = batchId; this.ssnAsPayrollId = ssnAsPayrollId;
        this.batchDescription = batchDescription is { Length: > 0 } ? batchDescription : null;
    }

    static string Check(string field, string? v)
    {
        v ??= "";
        if (MaxLen.TryGetValue(field, out var n) && v.Length > n)
            throw new InvalidOperationException($"ADP export: the value \"{v}\" is too long for {field} (maximum {n} characters).");
        return v;
    }

    public Record AddNew()
    {
        cur = new Record { CoCode = Check("Co Code", globalCoCode), BatchId = BatchId, BatchDescription = batchDescription is null ? "" : Check("Batch Description", batchDescription) };
        records.Add(cur);
        return cur;
    }

    public string SortingData { set => cur.SortingData = Check("SortingData", value); }
    public string FileNameCode { set => cur.FileNameCode = Check("FileNameCode", value); }
    public string CoCode { set => cur.CoCode = Check("Co Code", value); }
    public long FileNumber { set => cur.FileNumber = value; }
    public string TempDept { set { cur.TempDept = Check("Temp Dept", value); anyTempDept = true; } }
    public decimal TempRate { set { cur.TempRate = value; anyTempRate = true; } }
    public string RateCode { set { cur.RateCode = Check("Rate Code", value); anyRateCode = true; } }
    public decimal RegHours { set { cur.RegHours = value; anyRegHours = true; } }
    public decimal RegEarnings { set { cur.RegEarnings = value; anyRegEarnings = true; } }
    public decimal OtHours { set { cur.OtHours = value; anyOtHours = true; } }

    /// <summary>
    /// Puts a code/amount pair into the first free slot of the group on the current record. When all four slots are full a
    /// follow-on record is started for the same employee (same company, file number, department and rate), as the old class did.
    /// Hours and memo/deduction groups skip a zero amount; earnings groups do not.
    /// </summary>
    public void Put(Group g, string code, decimal amount)
    {
        var gi = (int)g;
        var skipZero = g is Group.Hours3 or Group.Hours4 or Group.Hours5 or Group.Memo or Group.Deduction;
        if (skipZero && amount == 0) return;
        code = Check("Code", code);
        for (var i = 0; i < SlotsPerGroup; i++)
        {
            if (cur.Slots[gi][i].Code is null)
            {
                cur.Slots[gi][i].Code = code; cur.Slots[gi][i].Amount = amount;
                used[gi] = Math.Max(used[gi], i + 1);
                return;
            }
        }
        var prev = cur;
        AddNew();
        cur.CoCode = prev.CoCode; cur.FileNumber = prev.FileNumber;
        // Deliberate difference from the Access class: it left these two empty on the follow-on record, which made the
        // record sort away from its employee and into the wrong file. The follow-on record now stays with its primary.
        cur.FileNameCode = prev.FileNameCode; cur.SortingData = prev.SortingData;
        cur.TempDept = prev.TempDept; cur.TempRate = prev.TempRate;
        cur.Slots[gi][0].Code = code; cur.Slots[gi][0].Amount = amount;
        used[gi] = Math.Max(used[gi], 1);
    }

    sealed record Field(string Header, Func<Record, object?> Get, string Kind);

    List<Field> Columns()
    {
        var f = new List<Field>
        {
            new("Co Code", r => r.CoCode, "text0"), new("Batch ID", r => r.BatchId, "long"), new("File #", r => r.FileNumber, "file"),
        };
        if (batchDescription is not null) f.Add(new("Batch Description", r => r.BatchDescription, "string"));
        if (anyTempDept) f.Add(new("Temp Dept", r => r.TempDept, "dept"));
        if (anyTempRate) f.Add(new("Temp Rate", r => r.TempRate, "rate"));
        if (anyRateCode && !anyTempRate) f.Add(new("Rate Code", r => r.RateCode, "string"));
        if (anyRegHours) f.Add(new("Reg Hours", r => r.RegHours, "money"));
        if (anyRegEarnings) f.Add(new("Reg Earnings", r => r.RegEarnings, "money"));
        if (anyOtHours) f.Add(new("O/T Hours", r => r.OtHours, "money"));
        for (var gi = 0; gi < 8; gi++)
            for (var s = 0; s < used[gi]; s++)
            {
                var g = gi; var si = s;
                var isMemo = g == (int)Group.Memo;
                f.Add(new($"{GroupTitle[g]} Code", r => r.Slots[g][si].Code, isMemo ? "memocode" : "string"));
                f.Add(new($"{GroupTitle[g]} Amount", r => r.Slots[g][si].Amount, "money"));
            }
        return f;
    }

    // VBA IsNumeric for a string, close enough for codes and descriptions.
    static bool IsNumeric(string s) => decimal.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    static decimal Num(string s) => decimal.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    static string Money(decimal v, bool integer) =>
        integer ? decimal.Round(v, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
                : decimal.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    static int Compare(Record a, Record b)
    {
        int Str(string? x, string? y) => x is null ? (y is null ? 0 : -1) : y is null ? 1 : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        int Dec(decimal? x, decimal? y) => x is null ? (y is null ? 0 : -1) : y is null ? 1 : x.Value.CompareTo(y.Value);
        int c;
        if ((c = Str(a.CoCode, b.CoCode)) != 0) return c;
        if ((c = Str(a.FileNameCode, b.FileNameCode)) != 0) return c;
        if ((c = a.BatchId.CompareTo(b.BatchId)) != 0) return c;
        if ((c = Str(a.SortingData, b.SortingData)) != 0) return c;
        if ((c = a.FileNumber.CompareTo(b.FileNumber)) != 0) return c;
        if ((c = Str(a.TempDept, b.TempDept)) != 0) return c;
        if ((c = Dec(b.RegHours, a.RegHours)) != 0) return c;   // descending
        return Dec(b.OtHours, a.OtHours);                         // descending
    }

    string Line(Record r, List<Field> cols)
    {
        var sb = new StringBuilder(r.CoCode);
        var memoIntegerNext = false;
        for (var i = 1; i < cols.Count; i++)
        {
            var c = cols[i]; var v = c.Get(r);
            switch (c.Kind)
            {
                case "dept": sb.Append(v is null ? "," : $",\"{v}\""); break;
                case "rate": sb.Append(v is null ? "," : "," + ((decimal)v).ToString("0.0000", CultureInfo.InvariantCulture)); break;
                case "file" when ssnAsPayrollId: sb.Append(',').Append(r.FileNumber.ToString(CultureInfo.InvariantCulture).PadLeft(9, '0')[^9..]); break;
                case "long" or "file": var n = Convert.ToInt64(v); sb.Append(n > 0 ? "," + n : ","); break;
                case "money":
                    if (v is decimal d && d != 0) { sb.Append(',').Append(Money(d, memoIntegerNext)); memoIntegerNext = false; }
                    else sb.Append(',');
                    break;
                default: // string and memo code
                    if (v is null) { sb.Append(','); break; }
                    var s = (string)v;
                    if (IsNumeric(s))
                    {
                        if (c.Kind == "memocode" && (s == "2" || s == "4")) { sb.Append(",\"").Append(s).Append('"'); memoIntegerNext = true; }
                        else sb.Append(Num(s) > 0 ? "," + s : ",");
                    }
                    else sb.Append(",\"").Append(s).Append('"');
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Writes one file per company and file-name code, named EPI{Co Code}{FileNameCode}.csv, and returns the batch id to store afterwards.</summary>
    public (IReadOnlyList<AdpFile> Files, int NextBatchId) Export()
    {
        var cols = Columns();
        var header = string.Join(",", cols.Select(c => c.Header));
        var sorted = records.Select((r, i) => (r, i)).OrderBy(x => x.r, Comparer<Record>.Create(Compare)).ThenBy(x => x.i).Select(x => x.r).ToList();
        var files = new List<AdpFile>();
        StringBuilder? sb = null; string? name = null; var lines = 0; string? co = null, code = null;
        var batch = BatchId;
        foreach (var r in sorted)
        {
            if (co != r.CoCode || code != (r.FileNameCode ?? ""))
            {
                if (sb is not null) { files.Add(new AdpFile(name!, sb.ToString(), lines)); batch++; }   // old class: batch id +1 per file switch
                co = r.CoCode; code = r.FileNameCode ?? "";
                name = $"EPI{co}{code}.csv"; sb = new StringBuilder(); lines = 0;
                sb.Append(header).Append("\r\n");
            }
            sb!.Append(Line(r, cols)).Append("\r\n"); lines++;
        }
        if (sb is not null) files.Add(new AdpFile(name!, sb.ToString(), lines));
        var next = batch + 1;                                                                           // and +1 more on close
        BatchId = next;
        return (files, next);
    }
}
