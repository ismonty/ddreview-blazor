using System.Xml.Linq;

namespace DDReview.QuickBooks.Posting;

/// <summary>The first response in a QBXML reply: its status and, for queries and adds, the returned objects.</summary>
public sealed class QbResponse
{
    public int StatusCode { get; init; }
    public string StatusMessage { get; init; } = "";
    public XElement? Rs { get; init; }

    public bool Ok => StatusCode == 0;
    public IEnumerable<XElement> Rets => Rs?.Elements().Where(e => e.Name.LocalName.EndsWith("Ret", StringComparison.Ordinal)) ?? Enumerable.Empty<XElement>();
    public XElement? FirstRet => Rets.FirstOrDefault();

    public static QbResponse Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return new QbResponse { StatusCode = -1, StatusMessage = "No response from QuickBooks." };
        var doc = XDocument.Parse(xml);
        var rs = doc.Root?.Element("QBXMLMsgsRs")?.Elements().FirstOrDefault();
        if (rs == null) return new QbResponse { StatusCode = -1, StatusMessage = "QuickBooks sent a reply with no response in it." };
        int.TryParse((string?)rs.Attribute("statusCode"), out var code);
        return new QbResponse { StatusCode = code, StatusMessage = (string?)rs.Attribute("statusMessage") ?? "", Rs = rs };
    }
}
