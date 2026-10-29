using System.Security.Claims;
using DDReview.Core.Tenancy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace DDReview.Web;

/// <summary>Everything the pages need about the signed-in user, resolved server-side (the connection string never goes to the browser or the cookie).</summary>
public sealed record UserContext(int WebUserId, string UserName, string ClientConnectionString, UserAccess Access, CentralAccount Account);

public sealed class UserContextService(
    AuthenticationStateProvider auth, ICentralDirectory central, ITenantConnectionFactory tenants,
    ClientAccessService clientAccess, IMemoryCache cache)
{
    public async Task<UserContext?> GetAsync(CancellationToken ct = default)
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return null;
        return await cache.GetOrCreateAsync($"ctx:{id}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var acct = await central.GetByIdAsync(id, ct);
            if (acct?.ClientUser is null || acct.ClientPassword is null) return null;
            var cs = await tenants.GetClientConnectionStringAsync(id, ct);
            var access = await clientAccess.LoadAsync(cs, acct.ClientUser, acct.ClientPassword, ct);
            return access is null ? null : new UserContext(id, acct.UserName, cs, access, acct);
        });
    }
}
