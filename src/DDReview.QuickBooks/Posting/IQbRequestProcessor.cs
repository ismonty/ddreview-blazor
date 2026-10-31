namespace DDReview.QuickBooks.Posting;

/// <summary>Sends one QBXML request document to QuickBooks and returns the response document.</summary>
public interface IQbRequestProcessor : IDisposable
{
    string Send(string qbxmlRequestDocument);
}
