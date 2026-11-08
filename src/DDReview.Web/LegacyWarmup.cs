using DDReview.Core.Legacy;
using Microsoft.Extensions.Options;

namespace DDReview.Web;

/// <summary>
/// Keeps the old site (IIS application "/classic") warm: one quiet GET of its login page shortly after this site starts and then every
/// <see cref="LegacyProxyOptions.WarmupMinutes"/> minutes, so a person opening Classic does not wait for the old site's cold start (about 45 seconds).
/// It talks to the loopback upstream directly, sends no cookies and changes nothing on the old site.
/// </summary>
public sealed class LegacyWarmup(IOptions<LegacyProxyOptions> options, ILogger<LegacyWarmup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var o = options.Value;
        if (!o.Enabled || o.WarmupMinutes <= 0) return;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { return; }
        bool lastOk = true;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(o.WarmupMinutes));
        do
        {
            var (ok, took, note) = await LegacyWarmer.WarmOnceAsync(http, o.Upstream, o.WarmupPath, stop);
            if (ok) log.LogInformation("Classic warm-up: {Note} in {Ms} ms", note, (int)took.TotalMilliseconds);
            else if (lastOk) log.LogWarning("Classic warm-up failed after {Ms} ms: {Note} (further failures are not logged until it works again)", (int)took.TotalMilliseconds, note);
            lastOk = ok;
        }
        while (await NextAsync(timer, stop));
    }

    static async Task<bool> NextAsync(PeriodicTimer t, CancellationToken stop)
    {
        try { return await t.WaitForNextTickAsync(stop); } catch (OperationCanceledException) { return false; }
    }
}
