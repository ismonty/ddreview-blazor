using System.Security.Claims;
using DDReview.Core.Admin;
using DDReview.Core.Tenancy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace DDReview.Web;

/// <summary>Who is asking and what Setup lets them do right now. Rebuilt on every call from the session, so a verification that has run out is noticed at once.</summary>
public sealed record SetupSituation(SetupActor Actor, SetupDecision Decision, string ClientConnectionString, bool TablesMissing, bool GoogleEnabled)
{
    public DateTimeOffset? VerifiedUntil => Actor.VerifiedAt is { } v ? v + SetupClaims.VerifiedFor : null;
}

/// <summary>
/// Puts the Setup rules (<see cref="SetupPolicy"/>) together with the signed-in session: client Administrator flag and grants from the client
/// database, Google claims from the cookie. Every Setup page asks this; nothing else decides who may use Setup.
/// </summary>
public sealed class SetupAccessService(
    UserContextService users, AuthenticationStateProvider auth, SetupAccessStore store, ICentralDirectory central,
    IMemoryCache cache, IHttpContextAccessor http, GoogleSignInStatus google, TimeProvider clock)
{
    /// <param name="fresh">Read the grants from the database instead of the 30-second cache. Everything that saves something passes true.</param>
    public async Task<SetupSituation?> GetAsync(bool fresh = false, CancellationToken ct = default)
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        UserContext? ctx;
        try { ctx = await users.GetAsync(user, ct); }
        catch (Microsoft.Data.SqlClient.SqlException) { return null; }
        if (ctx is null) return null;

        var key = GrantsKey(ctx.ClientConnectionString, ctx.Access.ClientWebUserId);
        if (fresh) cache.Remove(key);
        var grants = await cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await store.LoadGrantsAsync(ctx.ClientConnectionString, ctx.Access.ClientWebUserId, ct);
        }) ?? new SetupGrants(new HashSet<SetupArea>(), true);

        var isGoogle = SetupClaims.IsGoogleSession(user);
        string? email = null;
        if (isGoogle)
            email = await cache.GetOrCreateAsync($"setupemail:{ctx.WebUserId}", async e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                try { return await central.GetGoogleEmailAsync(ctx.WebUserId, ct); } catch (Microsoft.Data.SqlClient.SqlException) { return null; }
            });

        var actor = new SetupActor(ctx.WebUserId, ctx.Access.ClientWebUserId, ctx.UserName, ctx.Access.Administrator,
            isGoogle, SetupClaims.VerifiedAt(user), email, http.HttpContext?.Connection.RemoteIpAddress?.ToString());
        var decision = SetupPolicy.Decide(actor, grants.Areas, clock.GetUtcNow());
        return new SetupSituation(actor, decision, ctx.ClientConnectionString, grants.TablesMissing, google.Enabled);
    }

    /// <summary>The link in the top bar: shown to anyone Setup could be for (an administrator or someone with a grant), whatever their session; the page explains what is missing.</summary>
    public async Task<bool> ShowLinkAsync(CancellationToken ct = default)
    {
        var s = await GetAsync(ct: ct);
        return s is not null && (s.Actor.IsAdministrator || s.Decision.Allowed || s.Decision.Denial is SetupDenial.NotGoogle or SetupDenial.NeedsVerification);
    }

    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Drops the cached grants of one client login (after an administrator changes them).</summary>
    public void Forget(string clientConnectionString, int clientWebUserId) => cache.Remove(GrantsKey(clientConnectionString, clientWebUserId));

    // The connection string holds a password, so it is hashed, never used as the key itself.
    private static string GrantsKey(string clientConnectionString, int clientWebUserId) =>
        "setupgrants:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clientConnectionString)))[..16] + ":" + clientWebUserId;
}
