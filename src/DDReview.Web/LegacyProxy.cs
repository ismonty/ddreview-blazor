using System.Security.Claims;
using DDReview.Core.Legacy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace DDReview.Web;

/// <summary>
/// The /classic reverse proxy to the old DDReview site (see docs/LEGACY-PROXY-PLAN.md). Everything here is switched off unless
/// <c>LegacyProxy:Enabled</c> is true. The old site is published as the IIS application "/classic" on a loopback-only binding, so paths pass through unchanged.
/// </summary>
public static class LegacyProxy
{
    const string RouteId = "classic", ClusterId = "classic-old-site";

    public static LegacyProxyOptions Read(IConfiguration config)
        => config.GetSection(LegacyProxyOptions.Section).Get<LegacyProxyOptions>() ?? new LegacyProxyOptions();

    public static void AddLegacyProxy(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<LegacyProxyOptions>(builder.Configuration.GetSection(LegacyProxyOptions.Section));
        var o = Read(builder.Configuration);
        if (!o.Enabled) return;
        if (!Uri.TryCreate(o.Upstream, UriKind.Absolute, out var up) || !System.Net.IPAddress.TryParse(up.Host, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("LegacyProxy:Upstream must be a loopback address such as http://127.0.0.1:8081 (the old site must never be reachable from outside).");

        var routes = new[]
        {
            new RouteConfig
            {
                RouteId = RouteId, ClusterId = ClusterId, AuthorizationPolicy = "default",   // signed-in users only
                Match = new RouteMatch { Path = o.PathPrefix + "/{**catch-all}" },
            },
            new RouteConfig   // "/classic" with no slash: send it to the slash form (handled by an endpoint in MapLegacyProxy)
            {
                RouteId = RouteId + "-root", ClusterId = ClusterId, AuthorizationPolicy = "default",
                Match = new RouteMatch { Path = o.PathPrefix },
            },
        };
        var clusters = new[]
        {
            new ClusterConfig
            {
                ClusterId = ClusterId,
                Destinations = new Dictionary<string, DestinationConfig> { ["old"] = new() { Address = o.Upstream.TrimEnd('/') + "/" } },
                HttpRequest = new Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(6) },   // old site allows 300 s
            },
        };
        builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters).AddTransforms(ctx =>
        {
            if (ctx.Route.RouteId != RouteId) return;
            ctx.AddRequestTransform(t =>
            {
                // Never hand the new site's sign-in cookies to the old site, and never trust internal headers sent by the browser.
                string? cookie = LegacyCookies.Strip(t.HttpContext.Request.Headers.Cookie.ToString(), "ddreview.", ".AspNetCore.");
                t.ProxyRequest.Headers.Remove("Cookie");
                if (cookie is not null) t.ProxyRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
                foreach (var h in t.ProxyRequest.Headers.Where(h => h.Key.StartsWith("X-DDReview-", StringComparison.OrdinalIgnoreCase)).Select(h => h.Key).ToList())
                    t.ProxyRequest.Headers.Remove(h);
                t.ProxyRequest.Headers.TryAddWithoutValidation("X-DDReview-User", t.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
                return ValueTask.CompletedTask;
            });
            ctx.AddResponseTransform(t =>
            {
                t.HttpContext.Response.Headers.CacheControl = "no-store";   // report pages are private
                t.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
                t.HttpContext.Response.Headers.Remove("X-Powered-By");
                t.HttpContext.Response.Headers.Remove("X-AspNet-Version");
                return ValueTask.CompletedTask;
            });
        });
    }

    /// <summary>Path allow-list, size limit and request log. Must run after authentication, before the proxy endpoint.</summary>
    public static void UseLegacyGate(this WebApplication app)
    {
        var o = Read(app.Configuration);
        if (!o.Enabled) return;
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LegacyProxy");
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value;
            bool inScope = path is not null && (path.Equals(o.PathPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(o.PathPrefix + "/", StringComparison.OrdinalIgnoreCase));
            if (!inScope) { await next(); return; }
            // Judge the raw (still percent-encoded) target, so tricks like %2e%2e are refused rather than decoded.
            string raw = ctx.Features.Get<IHttpRequestFeature>()?.RawTarget ?? path!;
            string rawPath = raw.Split('?', 2)[0];
            string rawQuery = raw.Contains('?') ? raw[(raw.IndexOf('?') + 1)..] : "";
            string method = ctx.Request.Method;
            if (LegacyPathPolicy.IsSignedOutLanding(rawPath, method, rawQuery, o) && LegacyPathPolicy.IsSignedOutLanding(path, method, rawQuery, o))
            {
                // The old site's Sign out ends on its login page, which this app never shows. Say what happened instead of "Not Found".
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-store";
                await ctx.Response.WriteAsync(SignedOutPage);
                return;
            }
            if (!path!.Equals(o.PathPrefix, StringComparison.OrdinalIgnoreCase)
                && (!LegacyPathPolicy.IsAllowedRequest(rawPath, method, rawQuery, o) || !LegacyPathPolicy.IsAllowedRequest(path, method, rawQuery, o)))
            {
                log.LogWarning("Blocked {Method} {Path} for user {User} from {Ip}", ctx.Request.Method, rawPath, ctx.User.Identity?.Name ?? "-", ctx.Connection.RemoteIpAddress);
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var size = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = o.MaxRequestMegabytes * 1024L * 1024L;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { await next(); }
            finally
            {
                log.LogInformation("{Method} {Path} -> {Status} in {Ms} ms, user {User}", ctx.Request.Method, rawPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds,
                    ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "-");
            }
        });
    }

    const string SignedOutPage = """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Signed out of Classic</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#000;color:#f5efe0;font:16px/1.5 Roboto,system-ui,sans-serif}
        main{max-width:26rem;padding:2rem}h1{font-size:1.25rem}a,button{font:inherit;color:#10206a;background:#d9a916;border:0;border-radius:4px;padding:.5rem 1rem;margin:.25rem .5rem .25rem 0;cursor:pointer;text-decoration:none;display:inline-block}</style></head>
        <body><main><h1>You've signed out of Classic.</h1>
        <p>You're still signed in to DD Review.</p>
        <button type="button" onclick="window.close();document.getElementById('m').hidden=false">Close this tab</button>
        <a href="/">Back to DD Review</a>
        <p id="m" hidden>Your browser kept this tab open. You can close it yourself.</p></main></body></html>
        """;

    public static void MapLegacyProxy(this WebApplication app)
    {
        var o = Read(app.Configuration);
        if (!o.Enabled) return;
        app.MapReverseProxy();
        // /classic-enter?to=/classic/DDSalesReview.aspx : mints a ticket and sends the browser to the old site's login page with it (only when handoff is on).
        app.MapGet("/classic-enter", (HttpContext ctx, IOptions<LegacyProxyOptions> opt) =>
        {
            var lo = opt.Value;
            if (!lo.HandoffOn) return Results.Redirect(lo.PathPrefix + "/");
            if (!int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out int uid)) return Results.Redirect("/login");
            string to = ctx.Request.Query["to"].ToString();
            if (!LegacyPathPolicy.IsAllowed(to, lo)) to = lo.PathPrefix + "/Default.aspx";
            string ticket = LegacyTicket.Mint(lo.HandoffSecret, uid, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(lo.TicketSeconds));
            // The old login page signs the person in and then sends them to ReturnUrl (standard Forms authentication behaviour).
            return Results.Redirect($"{lo.HandoffPath}?t={Uri.EscapeDataString(ticket)}&ReturnUrl={Uri.EscapeDataString(to)}");
        }).RequireAuthorization();
    }
}
