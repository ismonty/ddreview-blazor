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
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddMemoryCache();
builder.Services.Configure<TenantOptions>(builder.Configuration.GetSection(TenantOptions.Section));
builder.Services.AddSingleton<ICentralDirectory, CentralDirectory>();
builder.Services.AddSingleton<ITenantConnectionFactory, TenantConnectionFactory>();
builder.Services.AddSingleton<ClientAccessService>();
builder.Services.AddSingleton<GlService>();
builder.Services.AddSingleton<DDReview.Core.Sales.SalesService>();
builder.Services.AddSingleton<DDReview.Core.Receipts.ReceiptsService>();
builder.Services.AddSingleton<DDReview.Core.Labor.LaborService>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton(sp => new LoginService(sp.GetRequiredService<ICentralDirectory>(), sp.GetRequiredService<LoginThrottle>(),
    sp.GetRequiredService<IOptions<TenantOptions>>().Value.UseCredentialTable));
builder.Services.AddScoped<UserContextService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
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

app.MapPost("/account/login", async (HttpContext ctx, [FromForm] string? userName, [FromForm] string? password, LoginService login) =>
{
    var r = await login.SignInAsync(userName ?? "", password ?? "", ctx.Connection.RemoteIpAddress?.ToString(),
        ctx.Request.Headers.UserAgent.ToString(), ctx.Request.Host.Host, ctx.RequestAborted);
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
});  // [FromForm] endpoints validate the antiforgery token automatically

app.MapPost("/account/logout", async (HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    try { await af.ValidateRequestAsync(ctx); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.BadRequest(); }
    await ctx.SignOutAsync();
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
