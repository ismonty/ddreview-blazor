using System.IO.Compression;
using System.Security;
using System.Text;
using System.Globalization;

namespace DDReview.Core.Support;

/// <summary>A minimal .xlsx writer (one sheet, bold header, optional bold totals row, numbers kept as numbers) so no extra package is needed.</summary>
public static class XlsxWriter
{
    public static byte[] Write(string sheetName, string? title, IReadOnlyList<string> headers, IEnumerable<object?[]> rows, object?[]? totals = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Add(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(zip, "xl/workbook.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{Esc(SafeName(sheetName))}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(zip, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            // style 0 normal, 1 bold, 2 number #,##0.00, 3 bold number
            Add(zip, "xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="#,##0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="164" fontId="1" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyFont="1"/></cellXfs></styleSheet>""");
            var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            int r = 1;
            if (!string.IsNullOrEmpty(title)) Row(sb, r++, [title], bold: true);
            Row(sb, r++, headers.Cast<object?>().ToArray(), bold: true);
            foreach (var row in rows) Row(sb, r++, row, false);
            if (totals is not null) Row(sb, r++, totals, bold: true);
            sb.Append("</sheetData></worksheet>");
            Add(zip, "xl/worksheets/sheet1.xml", sb.ToString());
        }
        return ms.ToArray();
    }

    static void Row(StringBuilder sb, int r, object?[] cells, bool bold)
    {
        sb.Append($"<row r=\"{r}\">");
        for (int c = 0; c < cells.Length; c++)
        {
            var addr = Col(c) + r.ToString(CultureInfo.InvariantCulture);
            var v = cells[c];
            switch (v)
            {
                case null or DBNull: break;
                case decimal or double or float or int or long or short:
                    sb.Append($"<c r=\"{addr}\" s=\"{(bold ? 3 : 2)}\"><v>{Convert.ToString(v, CultureInfo.InvariantCulture)}</v></c>"); break;
                case DateTime d:
                    sb.Append($"<c r=\"{addr}\" t=\"inlineStr\" s=\"{(bold ? 1 : 0)}\"><is><t>{Esc(d.ToString("g", CultureInfo.InvariantCulture))}</t></is></c>"); break;
                default:
                    sb.Append($"<c r=\"{addr}\" t=\"inlineStr\" s=\"{(bold ? 1 : 0)}\"><is><t xml:space=\"preserve\">{Esc(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")}</t></is></c>"); break;
            }
        }
        sb.Append("</row>");
    }

    static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
    static string Esc(string s) => SecurityElement.Escape(s) ?? "";
    static string SafeName(string n) { foreach (var ch in "[]:*?/\\") n = n.Replace(ch, ' '); return n.Length > 31 ? n[..31] : n.Length == 0 ? "Sheet1" : n; }
    static void Add(ZipArchive z, string name, string xml)
    {
        var e = z.CreateEntry(name, CompressionLevel.Fastest);
        using var s = new StreamWriter(e.Open(), new UTF8Encoding(false));
        s.Write(xml);
    }
}
