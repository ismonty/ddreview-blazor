using DDReview.Core.Admin;

namespace DDReview.Core.Tenancy;

/// <summary>Who is asking to manage friendly names and top-level administrators. Built on the server from the signed-in session.</summary>
public sealed record CentralActor(string? GoogleSubject, string? GoogleEmail, bool GoogleSession, DateTimeOffset? VerifiedAt, string? ClientIp);

public enum CentralDenial { None, NotGoogle, NotTopAdmin, NeedsVerification }

/// <summary>
/// Friendly names and the list of top-level administrators. Every call checks again, against the database, that the caller is a top-level
/// administrator with a Google session confirmed in the last 30 minutes; hiding a link is never the check.
/// </summary>
public sealed class CentralAdminService(ICentralDirectory central, TimeProvider clock)
{
    public async Task<CentralDenial> DecideAsync(CentralActor a, CancellationToken ct = default)
    {
        if (!a.GoogleSession || string.IsNullOrEmpty(a.GoogleSubject)) return CentralDenial.NotGoogle;
        bool top;
        try { top = await central.IsTopAdminAsync(a.GoogleSubject, null, false, ct); }
        catch (Microsoft.Data.SqlClient.SqlException) { top = false; }
        if (!top) return CentralDenial.NotTopAdmin;
        return SetupPolicy.IsFresh(a.VerifiedAt, clock.GetUtcNow()) ? CentralDenial.None : CentralDenial.NeedsVerification;
    }

    private async Task Require(CentralActor a, CancellationToken ct)
    {
        if (await DecideAsync(a, ct) != CentralDenial.None) throw new UnauthorizedAccessException("Top-level administrator access is needed.");
    }

    public async Task<IReadOnlyList<LoginChoice>> ListLoginsAsync(CentralActor a, string? filter, int top, CancellationToken ct = default)
    { await Require(a, ct); return await central.ListAllLoginsAsync(filter, top, true, ct); }

    public async Task<IReadOnlyList<DatabaseRow>> ListDatabasesAsync(CentralActor a, CancellationToken ct = default)
    { await Require(a, ct); return await central.ListDatabasesAsync(ct); }

    public async Task<bool> SetFriendlyNameAsync(CentralActor a, int webUserId, string? name, CancellationToken ct = default)
    {
        await Require(a, ct);
        var ok = await central.SetFriendlyNameAsync(webUserId, name, a.GoogleEmail, ct);
        if (ok) await central.AuditAsync("rename", a.GoogleSubject, a.GoogleEmail, webUserId, string.IsNullOrWhiteSpace(name) ? "(cleared)" : name.Trim(), a.ClientIp, ct);
        return ok;
    }

    public async Task SetInactiveAsync(CentralActor a, int webUserId, bool inactive, CancellationToken ct = default)
    {
        await Require(a, ct);
        await central.SetInactiveAsync(webUserId, inactive, a.GoogleEmail, ct);
        await central.AuditAsync(inactive ? "inactive" : "active", a.GoogleSubject, a.GoogleEmail, webUserId, null, a.ClientIp, ct);
    }

    public async Task<IReadOnlyList<TopAdminRow>> ListTopAdminsAsync(CentralActor a, CancellationToken ct = default)
    { await Require(a, ct); return await central.ListTopAdminsAsync(ct); }

    public async Task<bool> AddTopAdminAsync(CentralActor a, string email, CancellationToken ct = default)
    {
        await Require(a, ct);
        var ok = await central.AddTopAdminAsync(email, a.GoogleEmail, ct);
        if (ok) await central.AuditAsync("topadmin-add", a.GoogleSubject, a.GoogleEmail, null, email.Trim(), a.ClientIp, ct);
        return ok;
    }

    public async Task<bool> RemoveTopAdminAsync(CentralActor a, TopAdminRow row, CancellationToken ct = default)
    {
        await Require(a, ct);
        var ok = await central.RemoveTopAdminAsync(row.TopAdminId, ct);
        if (ok) await central.AuditAsync("topadmin-remove", a.GoogleSubject, a.GoogleEmail, null, row.GoogleEmail, a.ClientIp, ct);
        return ok;
    }

    /// <summary>Unlinks the Google account from a login (for a login whose person has left, or a wrong link).</summary>
    public async Task UnlinkLoginAsync(CentralActor a, int webUserId, CancellationToken ct = default)
    {
        await Require(a, ct);
        await central.UnlinkGoogleAsync(webUserId, ct);
        await central.AuditAsync("unlink-admin", a.GoogleSubject, a.GoogleEmail, webUserId, null, a.ClientIp, ct);
    }
}

/// <summary>Claims that remember which Google account a session came from.</summary>
public static class GoogleClaims
{
    public const string Subject = "google_sub";
    /// <summary>"1" when this Google account can open more than one database, so the top bar offers Switch database.</summary>
    public const string CanSwitch = "can_switch";
    public const string EmailVerified = "email_verified";

    public static bool IsTrue(string? v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
}
