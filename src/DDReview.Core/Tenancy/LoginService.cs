using DDReview.Core.Security;

namespace DDReview.Core.Tenancy;

public enum LoginOutcome { Success, InvalidCredentials, LockedOut, Redirect, NoClientDatabase }

public sealed record LoginResult(LoginOutcome Outcome, CentralAccount? Account = null, int WebLoginId = 0, string? RedirectUrl = null);

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
