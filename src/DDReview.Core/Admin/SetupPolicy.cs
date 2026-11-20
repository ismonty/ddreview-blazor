using System.Globalization;
using System.Security.Claims;

namespace DDReview.Core.Admin;

/// <summary>The parts of Setup that can be allowed separately. Stored by name in dbo.tblDDReviewAdminAccess.Area.</summary>
public enum SetupArea { GlSetup, ArSetup, StoreAndGlobal }

public static class SetupAreas
{
    public static IReadOnlyList<SetupArea> All { get; } = Enum.GetValues<SetupArea>();

    public static string Title(SetupArea a) => a switch
    {
        SetupArea.GlSetup => "G/L Setup",
        SetupArea.ArSetup => "A/R Setup",
        SetupArea.StoreAndGlobal => "Store Setup and Global Settings",
        _ => a.ToString(),
    };

    /// <summary>Exact names only (Enum.TryParse would also take "7" or "GlSetup,ArSetup").</summary>
    public static bool TryParse(string? name, out SetupArea area)
    {
        foreach (var a in All)
            if (string.Equals(name, a.ToString(), StringComparison.Ordinal)) { area = a; return true; }
        area = default;
        return false;
    }
}

/// <summary>Claims that tell a password session from a Google one, and when Google last vouched for the person.</summary>
public static class SetupClaims
{
    public const string AuthMethod = "auth_method";
    public const string Google = "google";
    public const string VerifiedUtc = "admin_verified_utc";   // unix seconds

    /// <summary>How long a Google check stays good for the Setup pages.</summary>
    public static readonly TimeSpan VerifiedFor = TimeSpan.FromMinutes(30);

    /// <summary>Sets (replacing any earlier copy) the two claims that say "Google vouched for this person just now".</summary>
    public static void StampGoogle(ClaimsIdentity id, DateTimeOffset now)
    {
        foreach (var c in id.FindAll(AuthMethod).ToList()) id.RemoveClaim(c);
        foreach (var c in id.FindAll(VerifiedUtc).ToList()) id.RemoveClaim(c);
        id.AddClaim(new Claim(AuthMethod, Google));
        id.AddClaim(new Claim(VerifiedUtc, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
    }

    public static bool IsGoogleSession(ClaimsPrincipal user) => user.HasClaim(AuthMethod, Google);

    /// <summary>When Google last vouched for this person in this session, or null (password session, or never).</summary>
    public static DateTimeOffset? VerifiedAt(ClaimsPrincipal user) =>
        long.TryParse(user.FindFirst(VerifiedUtc)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s) : null;
}

/// <summary>Who is asking, as the Setup rules need to know it. Built on the server from the signed-in session; never from the browser.</summary>
public sealed record SetupActor(
    int CentralWebUserId, int ClientWebUserId, string UserName, bool IsAdministrator,
    bool GoogleSession, DateTimeOffset? VerifiedAt, string? GoogleEmail, string? ClientIp);

public enum SetupDenial
{
    None,
    /// <summary>Nothing has been granted to this login (and it is not an administrator).</summary>
    NoRights,
    /// <summary>Signed in with a password; Setup needs a Google session.</summary>
    NotGoogle,
    /// <summary>Google session, but Google has not vouched for the person in the last 30 minutes.</summary>
    NeedsVerification,
}

public sealed record SetupDecision(SetupDenial Denial, IReadOnlySet<SetupArea> Areas, bool IsAdministrator)
{
    public bool Allowed => Denial == SetupDenial.None;
    public bool Allows(SetupArea area) => Allowed && Areas.Contains(area);
    /// <summary>Only administrators change who may use Setup, and a grant is never enough for that.</summary>
    public bool CanManageAccess => Allowed && IsAdministrator;
}

/// <summary>
/// The three Setup conditions in one place: (1) an administrator, or someone an administrator has granted an area to;
/// (2) a Google session; (3) Google has vouched for the person within <see cref="SetupClaims.VerifiedFor"/>.
/// Everything that changes data calls this again; hiding a menu link is never the check.
/// </summary>
public static class SetupPolicy
{
    private static readonly IReadOnlySet<SetupArea> None = new HashSet<SetupArea>();

    public static SetupDecision Decide(SetupActor actor, IReadOnlySet<SetupArea> grants, DateTimeOffset now)
    {
        IReadOnlySet<SetupArea> areas = actor.IsAdministrator ? new HashSet<SetupArea>(SetupAreas.All) : grants;
        if (areas.Count == 0) return new SetupDecision(SetupDenial.NoRights, None, actor.IsAdministrator);
        if (!actor.GoogleSession) return new SetupDecision(SetupDenial.NotGoogle, None, actor.IsAdministrator);
        if (!IsFresh(actor.VerifiedAt, now)) return new SetupDecision(SetupDenial.NeedsVerification, None, actor.IsAdministrator);
        return new SetupDecision(SetupDenial.None, areas, actor.IsAdministrator);
    }

    /// <summary>Verified within the window, and not "in the future" beyond a couple of minutes of clock difference.</summary>
    public static bool IsFresh(DateTimeOffset? verifiedAt, DateTimeOffset now) =>
        verifiedAt is { } v && v <= now.AddMinutes(2) && now - v <= SetupClaims.VerifiedFor;

    /// <summary>The check the grants page and the grant/revoke calls use: administrator + Google + fresh.</summary>
    public static bool CanManageAccess(SetupActor actor, DateTimeOffset now) =>
        actor.IsAdministrator && actor.GoogleSession && IsFresh(actor.VerifiedAt, now);
}
