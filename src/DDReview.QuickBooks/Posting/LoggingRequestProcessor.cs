using System.Text;

namespace DDReview.QuickBooks.Posting;

/// <summary>
/// Wraps a processor and writes every request and response to a log file in the layout DDAdminCS used
/// (QBLogFile.txt: &lt;DDQLog&gt; with &lt;Request&gt; and &lt;Response&gt; blocks), for support.
/// </summary>
public sealed class LoggingRequestProcessor : IQbRequestProcessor
{
    readonly IQbRequestProcessor _inner;
    readonly StreamWriter _log;

    public LoggingRequestProcessor(IQbRequestProcessor inner, string logPath)
    {
        _inner = inner;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        _log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        _log.WriteLine("<DDQLog>");
    }

    public string Send(string request)
    {
        string response;
        try { response = _inner.Send(request); }
        catch (Exception ex)
        {
            _log.WriteLine();
            _log.WriteLine("<Request>"); _log.WriteLine(request); _log.WriteLine("</Request>");
            _log.WriteLine("<SendError>" + System.Security.SecurityElement.Escape(ex.Message) + "</SendError>");
            throw;
        }
        _log.WriteLine();
        _log.WriteLine("<Request>"); _log.WriteLine(request); _log.WriteLine("</Request>");
        _log.WriteLine("<Response>"); _log.WriteLine(); _log.WriteLine(response); _log.WriteLine(); _log.WriteLine("</Response>");
        return response;
    }

    public void Dispose()
    {
        _log.WriteLine("</DDQLog>");
        _log.Dispose();
        _inner.Dispose();
    }
}
