using System.Text;

namespace DDReview.QuickBooks.Package;

/// <summary>Reads an export package saved as CSV (header row, NULL for nulls, optional BOM, quoted fields).</summary>
public static class CsvPackageReader
{
    public static List<PackageRow> Parse(string csv)
    {
        var records = Records(csv.TrimStart('﻿'));
        var rows = new List<PackageRow>();
        if (records.Count == 0) return rows;
        var header = records[0];
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++) index[header[i].Trim()] = i;
        for (int r = 1; r < records.Count; r++)
        {
            var rec = records[r];
            if (rec.Count == 1 && rec[0].Length == 0) continue;
            rows.Add(PackageRow.FromLookup(name => index.TryGetValue(name, out var i) && i < rec.Count ? rec[i] : null));
        }
        return rows;
    }

    public static List<PackageRow> ReadFile(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    static List<List<string>> Records(string text)
    {
        var all = new List<List<string>>();
        var cur = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cur.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { cur.Add(sb.ToString()); sb.Clear(); all.Add(cur); cur = new List<string>(); }
            else sb.Append(c);
        }
        if (sb.Length > 0 || cur.Count > 0) { cur.Add(sb.ToString()); all.Add(cur); }
        return all;
    }
}
