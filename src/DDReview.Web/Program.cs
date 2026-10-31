using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using DDReview.Core.Gl;
using DDReview.Core.Security;
using DDReview.Core.Tenancy;
using DDReview.Web;
using DDReview.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
// Keep the login-cookie keys on disk (set DataProtection:KeysPath) so a restart or redeploy does not sign everyone out.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMemoryCache();
builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection(TenantOptions.Section));
builder.Services.AddSingleton<ICentralDirectory, CentralDirectory>();
builder.Services.AddSingleton<ITenantConnectionFactory, TenantConnectionFactory>();
builder.Services.AddSingleton<ClientAccessService>();
builder.Services.AddSingleton<GlService>();
builder.Services.AddSingleton<DDReview.Core.Gl.SubmissionReportService>();
builder.Services.AddSingleton<DDReview.Core.Updater.ClientUpdater>();
builder.Services.AddSingleton<DDReview.Core.Sales.SalesService>();
builder.Services.AddSingleton<DDReview.Core.Home.HomeService>();
builder.Services.AddSingleton<DDReview.Core.Receipts.ReceiptsService>();
builder.Services.AddSingleton<DDReview.Core.Labor.LaborService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton(sp => new LoginService(sp.GetRequiredService<ICentralDirectory>(), sp.GetRequiredService<LoginThrottle>(),
    sp.GetRequiredService<IOptions<TenantOptions>>().Value.UseCredentialTable));
builder.Services.AddScoped<UserContextService>();

var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
bool googleOn = !string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret);
builder.Services.AddSingleton(new GoogleSignInStatus(googleOn));
const string ExternalScheme = "ddreview.external";

var authBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme);
authBuilder.AddCookie(ExternalScheme, o =>
{
    o.Cookie.Name = "ddreview.external";      // short-lived: only holds the Google answer between Google and /account/google/done
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
});
if (googleOn)
    authBuilder.AddGoogle(o => { o.ClientId = googleId!; o.ClientSecret = googleSecret!; o.SignInScheme = ExternalScheme; o.CallbackPath = "/signin-google"; });
authBuilder.AddCookie(o =>
{
    o.LoginPath = "/login";
    o.Cookie.Name = "ddreview.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

static async Task<IResult> FinishSignInAsync(HttpContext ctx, LoginResult r)
{
    switch (r.Outcome)
    {
        case LoginOutcome.Success:
            var id = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, r.Account!.WebUserId.ToString()), new Claim(ClaimTypes.Name, r.Account.UserName),
                 new Claim("webLoginId", r.WebLoginId.ToString())], CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(new ClaimsPrincipal(id));
            return Results.Redirect("/");
        case LoginOutcome.Redirect when Uri.TryCreate(r.RedirectUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps:
            return Results.Redirect(u.ToString());
        case LoginOutcome.LockedOut: return Results.Redirect("/login?e=locked");
        case LoginOutcome.NoClientDatabase: return Results.Redirect("/login?e=nodb");
        default: return Results.Redirect("/login?e=bad");
    }
}

// The stored submission report of a batch, drawn as SVG (thumbnails and the full page both use it). Signed-in users only, and only for
// stores their General Ledger access covers.
app.MapGet("/gl/submission-image/{batchId:int}", async (int batchId, HttpContext http, UserContextService users, DDReview.Core.Gl.SubmissionReportService reports) =>
{
    var ctx = await users.GetAsync(http.User, http.RequestAborted);
    if (ctx is null || !(ctx.Access.Administrator || ctx.Access.CanSee(ReportKind.DebitCredit))) return Results.NotFound();
    DDReview.Core.Gl.SubmissionReport? report;
    try { report = await reports.GetAsync(ctx.ClientConnectionString, batchId, DDReview.Core.Gl.SubmissionReportService.AllowedStores(ctx.Access, ReportKind.DebitCredit), http.RequestAborted); }
    catch (Microsoft.Data.SqlClient.SqlException) { return Results.NotFound(); }
    if (report is null || report.Commands.Count == 0) return Results.NotFound();
    http.Response.Headers.CacheControl = "private, max-age=600";
    return Results.Text(DDReview.Core.Gl.SubmissionSvg.Render(report.Commands), "image/svg+xml");
}).RequireAuthorization();

app.MapPost("/account/login", async (HttpContext ctx, [FromForm] string? userName, [FromForm] string? password, LoginService login) =>
{
    var r = await login.SignInAsync(userName ?? "", password ?? "", ctx.Connection.RemoteIpAddress?.ToString(),
        ctx.Request.Headers.UserAgent.ToString(), ctx.Request.Host.Host, ctx.RequestAborted);
    return await FinishSignInAsync(ctx, r);
});  // [FromForm] endpoints validate the antiforgery token automatically

// Google: "start" is a POST (antiforgery-checked) so another site cannot start a link in your browser; "done" is where Google sends you back.
if (googleOn)
{
    app.MapPost("/account/google/start", (HttpContext ctx, [FromForm] string? mode) =>
    {
        var props = new AuthenticationProperties { RedirectUri = "/account/google/done" };
        if (mode == "link")
        {
            var me = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (me is null) return Results.Redirect("/login");
            props.Items["linkUser"] = me;            // travels inside the protected sign-in state
        }
        return Results.Challenge(props, ["Google"]);
    });

    app.MapGet("/account/google/done", async (HttpContext ctx, LoginService login, ICentralDirectory central, ILogger<Program> log) =>
    {
        var ext = await ctx.AuthenticateAsync(ExternalScheme);
        await ctx.SignOutAsync(ExternalScheme);
        var sub = ext.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ext.Succeeded || string.IsNullOrEmpty(sub))
        {
            log.LogWarning("Google sign-in did not complete: {Reason}", ext.Failure?.Message ?? "no Google account id came back (external cookie missing or expired)");
            return Results.Redirect("/login?e=google");
        }
        try
        {
            if (ext.Properties?.Items.TryGetValue("linkUser", out var linkUser) == true)
            {
                if (ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) != linkUser || !int.TryParse(linkUser, out var uid))
                    return Results.Redirect("/login?e=google");
                var ok = await central.LinkGoogleAsync(uid, sub, ext.Principal!.FindFirstValue(ClaimTypes.Email), ctx.RequestAborted);
                return Results.Redirect(ok ? "/account?g=linked" : "/account?g=taken");
            }
            var r = await login.SignInWithGoogleAsync(sub, ctx.Connection.RemoteIpAddress?.ToString(),
                ctx.Request.Headers.UserAgent.ToString(), ctx.Request.Host.Host, ctx.RequestAborted);
            return r.Outcome == LoginOutcome.InvalidCredentials ? Results.Redirect("/login?e=nogoogle") : await FinishSignInAsync(ctx, r);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex)   // e.g. sql/003 not run on the central master yet
        {
            log.LogError(ex, "Google sign-in could not read dbo.tblWebUserGoogle on the central master (has sql/003_central_google_link.sql been run?)");
            return Results.Redirect("/login?e=google");
        }
    });

    app.MapPost("/account/google/unlink", async (HttpContext ctx, ICentralDirectory central, [FromForm] string? confirm) =>
    {
        if (!int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)) return Results.Redirect("/login");
        await central.UnlinkGoogleAsync(uid, ctx.RequestAborted);
        return Results.Redirect("/account?g=unlinked");
    });
}

app.MapPost("/account/logout", async (HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    try { await af.ValidateRequestAsync(ctx); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    await ctx.SignOutAsync();
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program { }
