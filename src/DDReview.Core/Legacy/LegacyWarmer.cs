namespace DDReview.Core.Legacy;

/// <summary>The one request of the Classic warm-up (the loop that repeats it lives in DDReview.Web).</summary>
public static class LegacyWarmer
{
    /// <summary>One warm-up request. Any HTTP answer counts as warm (the old site is awake); only a connection failure or time-out does not.</summary>
    public static async Task<(bool Ok, TimeSpan Took, string Note)> WarmOnceAsync(HttpClient http, string upstream, string path, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var uri = new Uri(new Uri(upstream.TrimEnd('/') + "/"), path.TrimStart('/'));
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return (true, sw.Elapsed, "HTTP " + (int)res.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return (false, sw.Elapsed, "stopped"); }
        catch (Exception ex) { return (false, sw.Elapsed, ex.GetType().Name + ": " + ex.Message); }
    }
}
