using System.Runtime.InteropServices;

namespace DDReview.QuickBooks.Posting;

/// <summary>Raised when QuickBooks cannot be reached; the message is fit to show the user.</summary>
public sealed class QuickBooksUnavailableException : Exception
{
    public QuickBooksUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// The real QuickBooks Desktop connection: the QBXMLRP2 request processor, used late-bound so this project needs no
/// interop assembly. QBXMLRP2 is a 32-bit COM server, so the program that uses this must run as x86 on Windows with
/// QuickBooks installed and a company file open.
/// </summary>
public sealed class Qbxmlrp2Processor : IQbRequestProcessor
{
    const int LocalQBD = 1;              // QBXMLRPConnectionType.localQBD
    const int OpenDoNotCare = 2;         // QBFileMode.qbFileOpenDoNotCare
    const int NoCompanyFileOpen = unchecked((int)0x80040416);

    object? _rp;
    string _ticket = "";

    public Qbxmlrp2Processor(string appName = "DD Admin CS", string companyFile = "")
    {
        if (!OperatingSystem.IsWindows()) throw new QuickBooksUnavailableException("QuickBooks Desktop runs only on Windows.");
        var type = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor2")
            ?? throw new QuickBooksUnavailableException("QuickBooks is not installed on this computer (QBXMLRP2 was not found). This program must run as a 32-bit program on the computer where QuickBooks is installed.");
        try
        {
            _rp = Activator.CreateInstance(type);
            dynamic rp = _rp!;
            rp.OpenConnection2("", appName, LocalQBD);
            _ticket = rp.BeginSession(companyFile, OpenDoNotCare);
        }
        catch (COMException ex) when (ex.HResult == NoCompanyFileOpen)
        {
            Dispose();
            throw new QuickBooksUnavailableException("You must have a company file open in QuickBooks to proceed.", ex);
        }
        catch (COMException ex)
        {
            Dispose();
            throw new QuickBooksUnavailableException("QuickBooks generated this error: " + ex.Message, ex);
        }
    }

    public string Send(string qbxmlRequestDocument)
    {
        if (_rp == null) throw new ObjectDisposedException(nameof(Qbxmlrp2Processor));
        try
        {
            dynamic rp = _rp;
            return (string)rp.ProcessRequest(_ticket, qbxmlRequestDocument);
        }
        catch (COMException ex)
        {
            throw new QuickBooksUnavailableException("QuickBooks generated this error: " + ex.Message, ex);
        }
    }

    public void Dispose()
    {
        if (_rp == null) return;
        try
        {
            dynamic rp = _rp;
            if (_ticket.Length > 0) rp.EndSession(_ticket);
            rp.CloseConnection();
        }
        catch (COMException) { }
        finally
        {
            if (OperatingSystem.IsWindows()) Marshal.FinalReleaseComObject(_rp);
            _rp = null;
        }
    }
}
