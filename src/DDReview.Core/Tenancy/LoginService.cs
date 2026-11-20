using DDReview.Core.Security;

namespace DDReview.Core.Tenancy;

public enum LoginOutcome { Success, InvalidCredentials, LockedOut, Redirect, NoClientDatabase }

public sealed record LoginResult(LoginOutcome Outcome, CentralAccount? Account = null, int WebLoginId = 0, string? RedirectUrl = null);

/// <summary>The databases (logins) a Google account can open. <see cref="MustChoose"/> is true when the person gets a list instead of being signed straight in.</summary>
public sealed record GoogleOptions(IReadOnlyList<LoginChoice> Choices, bool IsTopAdmin, bool MustChoose, IReadOnlyList<DatabaseRow>? Databases = null);

public sealed class LoginService(ICentralDirectory central, LoginThrottle throttle, bool upgradeLegacyPasswords)
{
    public async Task<LoginResult> SignInAsync(string userOrEmail, string password, string? ip, string? userAgent, string? hostName, CancellationToken ct = default)
    {
        userOrEmail = (userOrEmail ?? "").Trim();
        password ??= "";
        if (userOrEmail.Length is 0 or > 250 || password.Length is 0 or > 200)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        var key = $"{userOrEmail.ToUpperInvariant()}|{ip}";
        if (throttle.IsLockedOut(key)) return new LoginResult(LoginOutcome.LockedOut);

        CentralAccount? match = null;
        foreach (var a in await central.FindAccountsAsync(userOrEmail, ct))
        {
            var ok = a.PasswordHash is not null
                ? PasswordHasher.Verify(password, a.PasswordHash)
                : PasswordHasher.VerifyLegacy(password, a.LegacyPassword);
            if (!ok) continue;
            match = a;
            if (a.PasswordHash is null && upgradeLegacyPasswords)
                await central.StoreHashAsync(a.WebUserId, PasswordHasher.Hash(password), ct);
            break;
        }

        if (match is null) { throttle.RecordFailure(key); return new LoginResult(LoginOutcome.InvalidCredentials); }
        throttle.Reset(key);

        return await CompleteAsync(match, ip, userAgent, hostName, ct);
    }

    /// <summary>Google sign-in: the Google account id must already be linked to a login (see CentralDirectory.LinkGoogleAsync).</summary>
    public async Task<LoginResult> SignInWithGoogleAsync(string googleSubject, string? ip, string? userAgent, string? hostName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(googleSubject) || googleSubject.Length > 100) return new LoginResult(LoginOutcome.InvalidCredentials);
        var match = await central.FindByGoogleAsync(googleSubject, ct);
        return match is null ? new LoginResult(LoginOutcome.InvalidCredentials) : await CompleteAsync(match, ip, userAgent, hostName, ct);
    }

    /// <summary>What a Google account may open: the logins it is linked to, or (top-level administrators) every login.</summary>
    public async Task<GoogleOptions> GoogleOptionsAsync(string googleSubject, string? email, bool emailVerified, string? filter = null, bool includeInactive = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(googleSubject) || googleSubject.Length > 100) return new GoogleOptions([], false, false);
        var linked = await central.ListLinkedAsync(googleSubject, ct);
        bool top;
        try { top = await central.IsTopAdminAsync(googleSubject, email, emailVerified, ct); }
        catch (Microsoft.Data.SqlClient.SqlException) { top = false; }   // sql/008 not run yet: behave as before (linked logins only)
        if (!top) return new GoogleOptions(linked, false, linked.Count > 1);
        var all = await central.ListAllLoginsAsync(filter, 200, includeInactive, ct);
        var dbs = await central.ListDatabasesAsync(ct);
        return new GoogleOptions(all, true, true, dbs.Count > 0 ? dbs : null);
    }

    /// <summary>
    /// Signs in as the login the person chose from their list. The choice is checked again here: it must be one of the logins this Google account
    /// is linked to, or the person must be a top-level administrator. A top-level administrator gets that login's own access, nothing more.
    /// </summary>
    public async Task<LoginResult> SignInWithGoogleChoiceAsync(string googleSubject, string? email, bool emailVerified, int webUserId, string? ip, string? userAgent, string? hostName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(googleSubject) || googleSubject.Length > 100) return new LoginResult(LoginOutcome.InvalidCredentials);
        var linked = await central.IsLinkedAsync(googleSubject, webUserId, ct);
        var viaTop = false;
        try { viaTop = await central.IsTopAdminAsync(googleSubject, email, emailVerified, ct); }
        catch (Microsoft.Data.SqlClient.SqlException) { viaTop = false; }
        if (!linked && !viaTop) return new LoginResult(LoginOutcome.InvalidCredentials);
        if (!viaTop && await central.IsInactiveAsync(webUserId, ct)) return new LoginResult(LoginOutcome.InvalidCredentials);   // inactive logins are only for top-level administrators
        viaTop &= !linked;
        var match = await central.GetByIdAsync(webUserId, ct);
        if (match is null) return new LoginResult(LoginOutcome.InvalidCredentials);
        await central.AuditAsync(viaTop ? "pick-top" : "pick", googleSubject, email, webUserId, null, ip, ct);
        return await CompleteAsync(match, ip, userAgent, hostName, ct);
    }

    private async Task<LoginResult> CompleteAsync(CentralAccount match, string? ip, string? userAgent, string? hostName, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(match.RedirectUrl))
            return new LoginResult(LoginOutcome.Redirect, match, RedirectUrl: match.RedirectUrl);
        if (string.IsNullOrWhiteSpace(match.ClientDsn))
            return new LoginResult(LoginOutcome.NoClientDatabase, match);

        int loginId;
        try { loginId = await central.LogLoginAsync(match.WebUserId, userAgent, ip, hostName, ct); }
        catch (Microsoft.Data.SqlClient.SqlException) { loginId = 0; } // the audit-log write must never block a valid sign-in
        return new LoginResult(LoginOutcome.Success, match, loginId);
    }
}
