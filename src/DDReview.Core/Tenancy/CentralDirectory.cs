using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DDReview.Core.Tenancy;

public interface ICentralDirectory
{
    /// <summary>Accounts whose UserName or ControllingEmailAddress matches (case-insensitive). Never filters by password.</summary>
    Task<IReadOnlyList<CentralAccount>> FindAccountsAsync(string userOrEmail, CancellationToken ct = default);
    Task<CentralAccount?> GetByIdAsync(int webUserId, CancellationToken ct = default);
    Task StoreHashAsync(int webUserId, string hash, CancellationToken ct = default);
    Task<int> LogLoginAsync(int webUserId, string? userAgent, string? hostAddress, string? hostName, CancellationToken ct = default);
    /// <summary>The login linked to this Google account id (the "sub" claim), or null.</summary>
    Task<CentralAccount?> FindByGoogleAsync(string googleSubject, CancellationToken ct = default);
    /// <summary>Links a Google account to a login (a Google account may be linked to several logins; a login has one Google account, so a new one replaces the old).</summary>
    Task<bool> LinkGoogleAsync(int webUserId, string googleSubject, string? email, CancellationToken ct = default);
    /// <summary>The email of the Google account linked to this login, or null when none.</summary>
    Task<string?> GetGoogleEmailAsync(int webUserId, CancellationToken ct = default);
    Task UnlinkGoogleAsync(int webUserId, CancellationToken ct = default);

    // ---- one Google account, several logins (sql/008) ----
    /// <summary>Every login this Google account is linked to, with friendly names, best name first.</summary>
    Task<IReadOnlyList<LoginChoice>> ListLinkedAsync(string googleSubject, CancellationToken ct = default);
    Task<bool> IsLinkedAsync(string googleSubject, int webUserId, CancellationToken ct = default);
    /// <summary>True for a top-level administrator. Matched by Google account id; a row added by email is tied to the id the first time that email signs in with Google saying the email is verified.</summary>
    Task<bool> IsTopAdminAsync(string googleSubject, string? email, bool emailVerified, CancellationToken ct = default);
    /// <summary>Every login (for top-level administrators), optionally filtered on user name or friendly name.</summary>
    Task<IReadOnlyList<LoginChoice>> ListAllLoginsAsync(string? filter, int top, CancellationToken ct = default);
    /// <summary>Sets or (blank) clears a login's friendly name. False when the login does not exist.</summary>
    Task<bool> SetFriendlyNameAsync(int webUserId, string? friendlyName, string? by, CancellationToken ct = default);
    Task<IReadOnlyList<TopAdminRow>> ListTopAdminsAsync(CancellationToken ct = default);
    Task<bool> AddTopAdminAsync(string email, string? by, CancellationToken ct = default);
    /// <summary>False when the row is missing or is the last top-level administrator.</summary>
    Task<bool> RemoveTopAdminAsync(int topAdminId, CancellationToken ct = default);
    /// <summary>Best effort: a missing audit table never blocks a sign-in or a save.</summary>
    Task AuditAsync(string action, string? googleSubject, string? email, int? webUserId, string? detail, string? clientIp, CancellationToken ct = default);
}

/// <summary>A login as the "choose a database" list shows it.</summary>
public sealed record LoginChoice(int WebUserId, string UserName, string? FriendlyName, string? GoogleEmail)
{
    public string Label => string.IsNullOrWhiteSpace(FriendlyName) ? UserName : FriendlyName!;
}

public sealed record TopAdminRow(int TopAdminId, string GoogleEmail, bool Bound, DateTime AddedUtc, string? AddedBy);

public sealed class CentralDirectory(IOptions<TenantOptions> options) : ICentralDirectory
{
    private readonly TenantOptions _o = options.Value;

    private string Select => $@"
select top (10) wus.WebUserID, wus.UserName, wus.[Password], {(_o.UseCredentialTable ? "cred.PasswordHash" : "cast(null as varchar(200))")} as PasswordHash,
       wus.SQLConnectionString, wus.DDReviewUser, wus.DDReviewPassword, wus.MajorVersionNumber,
       wus.LicensedStartDate, wus.LicensedEndDate, wus.VoidsActivated, wus.DDReviewSubscriber,
       wus.MontySoftConsultingCustomer, wus.PayPalEnabledCustomer, wus.RedirectURL
from dbo.tblWebUserSecurity wus
{(_o.UseCredentialTable ? "left join dbo.tblWebUserCredential cred on cred.WebUserID = wus.WebUserID" : "")}";

    public async Task<IReadOnlyList<CentralAccount>> FindAccountsAsync(string userOrEmail, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(Select + @"
where upper(wus.UserName) = upper(@u) or upper(wus.ControllingEmailAddress) = upper(@u)", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@u", System.Data.SqlDbType.VarChar, 250).Value = userOrEmail;
        return await ReadAsync(cmd, ct);
    }

    public async Task<CentralAccount?> GetByIdAsync(int webUserId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(Select + " where wus.WebUserID = @id", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        return (await ReadAsync(cmd, ct)).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<CentralAccount>> ReadAsync(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<CentralAccount>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string? S(string c) => r[c] is DBNull ? null : (string)r[c];
            DateTime? D(string c) => r[c] is DBNull ? null : (DateTime)r[c];
            list.Add(new CentralAccount(
                (int)r["WebUserID"], (string)r["UserName"], S("Password"), S("PasswordHash"),
                S("SQLConnectionString"), S("DDReviewUser"), S("DDReviewPassword"), (int)r["MajorVersionNumber"],
                D("LicensedStartDate"), D("LicensedEndDate"), (bool)r["VoidsActivated"], (bool)r["DDReviewSubscriber"],
                (bool)r["MontySoftConsultingCustomer"], (bool)r["PayPalEnabledCustomer"], S("RedirectURL")));
        }
        return list;
    }

    public async Task StoreHashAsync(int webUserId, string hash, CancellationToken ct = default)
    {
        if (!_o.UseCredentialTable) return;
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
merge dbo.tblWebUserCredential as t using (select @id as WebUserID) as s on t.WebUserID = s.WebUserID
when matched then update set PasswordHash = @h, UpdatedUtc = sysutcdatetime()
when not matched then insert (WebUserID, PasswordHash, UpdatedUtc) values (@id, @h, sysutcdatetime());", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        cmd.Parameters.Add("@h", System.Data.SqlDbType.VarChar, 200).Value = hash;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Uses the existing logging procedure (same as the old app) so reports on logins keep working.</summary>
    public async Task<int> LogLoginAsync(int webUserId, string? userAgent, string? hostAddress, string? hostName, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("dbo.cs_spLoginLogAddWithRequestDetails2", cn) { CommandType = System.Data.CommandType.StoredProcedure };
        cmd.Parameters.Add("@WebUserID", System.Data.SqlDbType.Int).Value = webUserId;
        cmd.Parameters.Add("@BrowserString", System.Data.SqlDbType.VarChar, 250).Value = (object?)Trunc(userAgent, 250) ?? "";
        cmd.Parameters.Add("@BrowserHostAddress", System.Data.SqlDbType.VarChar, 50).Value = (object?)Trunc(hostAddress, 50) ?? "";
        cmd.Parameters.Add("@BrowserHostName", System.Data.SqlDbType.VarChar, 50).Value = (object?)Trunc(hostName, 50) ?? "";
        var p = cmd.Parameters.Add("@WebLoginID", System.Data.SqlDbType.Int); p.Direction = System.Data.ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync(ct);
        return p.Value is int i ? i : 0;
    }

    public async Task<CentralAccount?> FindByGoogleAsync(string googleSubject, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(Select + " inner join dbo.tblWebUserGoogle g on g.WebUserID = wus.WebUserID where g.GoogleSubject = @s", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = googleSubject;
        return (await ReadAsync(cmd, ct)).FirstOrDefault();
    }

    public async Task<bool> LinkGoogleAsync(int webUserId, string googleSubject, string? email, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        // Additive since sql/008: one Google account may be linked to several logins. A login still has only one Google account,
        // so linking a different one replaces that login's old link (the unique index on WebUserID enforces it).
        await using var cmd = new SqlCommand(@"
delete dbo.tblWebUserGoogle where WebUserID = @id and GoogleSubject <> @s;
if not exists (select 1 from dbo.tblWebUserGoogle where GoogleSubject = @s and WebUserID = @id)
    insert dbo.tblWebUserGoogle (GoogleSubject, WebUserID, GoogleEmail) values (@s, @id, @e)
else update dbo.tblWebUserGoogle set GoogleEmail = @e where GoogleSubject = @s and WebUserID = @id;
select 1", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        cmd.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = googleSubject;
        cmd.Parameters.Add("@e", System.Data.SqlDbType.VarChar, 320).Value = (object?)Trunc(email, 320) ?? DBNull.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<string?> GetGoogleEmailAsync(int webUserId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("select GoogleEmail from dbo.tblWebUserGoogle where WebUserID = @id", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        var o = await cmd.ExecuteScalarAsync(ct);
        return o is null ? null : o is DBNull ? "(linked)" : (string)o;
    }

    public async Task UnlinkGoogleAsync(int webUserId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("delete dbo.tblWebUserGoogle where WebUserID = @id", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<LoginChoice>> ListLinkedAsync(string googleSubject, CancellationToken ct = default) =>
        await ChoicesAsync(@"
select wus.WebUserID, wus.UserName, f.FriendlyName, g.GoogleEmail
from dbo.tblWebUserGoogle g
join dbo.tblWebUserSecurity wus on wus.WebUserID = g.WebUserID
left join dbo.tblWebUserFriendlyName f on f.WebUserID = wus.WebUserID
where g.GoogleSubject = @s
order by coalesce(f.FriendlyName, wus.UserName), wus.WebUserID", c => c.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = googleSubject, ct);

    public async Task<bool> IsLinkedAsync(string googleSubject, int webUserId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("select count(*) from dbo.tblWebUserGoogle where GoogleSubject = @s and WebUserID = @id", cn);
        cmd.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = googleSubject;
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<bool> IsTopAdminAsync(string googleSubject, string? email, bool emailVerified, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
if exists (select 1 from dbo.tblDDReviewTopAdmin where GoogleSubject = @s) select 1
else if @v = 1 and @e is not null and exists (select 1 from dbo.tblDDReviewTopAdmin where upper(GoogleEmail) = upper(@e) and GoogleSubject is null)
begin
    update dbo.tblDDReviewTopAdmin set GoogleSubject = @s where upper(GoogleEmail) = upper(@e) and GoogleSubject is null;
    select 1
end
else select 0", cn);
        cmd.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = googleSubject;
        cmd.Parameters.Add("@e", System.Data.SqlDbType.VarChar, 320).Value = (object?)Trunc(email, 320) ?? DBNull.Value;
        cmd.Parameters.Add("@v", System.Data.SqlDbType.Bit).Value = emailVerified;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<IReadOnlyList<LoginChoice>> ListAllLoginsAsync(string? filter, int top, CancellationToken ct = default)
    {
        var like = string.IsNullOrWhiteSpace(filter) ? null : "%" + filter.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
        return await ChoicesAsync($@"
select top ({Math.Clamp(top, 1, 500)}) wus.WebUserID, wus.UserName, f.FriendlyName, g.GoogleEmail
from dbo.tblWebUserSecurity wus
left join dbo.tblWebUserFriendlyName f on f.WebUserID = wus.WebUserID
left join dbo.tblWebUserGoogle g on g.WebUserID = wus.WebUserID
where (@f is null or wus.UserName like @f escape '\' or f.FriendlyName like @f escape '\')
order by coalesce(f.FriendlyName, wus.UserName), wus.WebUserID",
            c => c.Parameters.Add("@f", System.Data.SqlDbType.VarChar, 250).Value = (object?)like ?? DBNull.Value, ct);
    }

    private async Task<IReadOnlyList<LoginChoice>> ChoicesAsync(string sql, Action<SqlCommand> bind, CancellationToken ct)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        bind(cmd);
        var list = new List<LoginChoice>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new LoginChoice((int)r["WebUserID"], (string)r["UserName"],
                r["FriendlyName"] is DBNull ? null : (string)r["FriendlyName"], r["GoogleEmail"] is DBNull ? null : (string)r["GoogleEmail"]));
        return list;
    }

    public async Task<bool> SetFriendlyNameAsync(int webUserId, string? friendlyName, string? by, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName.Trim();
        if (name is { Length: > 100 }) throw new ArgumentException("The friendly name is limited to 100 characters.", nameof(friendlyName));
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
if not exists (select 1 from dbo.tblWebUserSecurity where WebUserID = @id) select 0
else begin
    if @n is null delete dbo.tblWebUserFriendlyName where WebUserID = @id
    else if exists (select 1 from dbo.tblWebUserFriendlyName where WebUserID = @id)
        update dbo.tblWebUserFriendlyName set FriendlyName = @n, UpdatedUtc = sysutcdatetime(), UpdatedBy = @by where WebUserID = @id
    else insert dbo.tblWebUserFriendlyName (WebUserID, FriendlyName, UpdatedBy) values (@id, @n, @by);
    select 1
end", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = webUserId;
        cmd.Parameters.Add("@n", System.Data.SqlDbType.VarChar, 100).Value = (object?)name ?? DBNull.Value;
        cmd.Parameters.Add("@by", System.Data.SqlDbType.VarChar, 320).Value = (object?)Trunc(by, 320) ?? DBNull.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<IReadOnlyList<TopAdminRow>> ListTopAdminsAsync(CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("select TopAdminID, GoogleEmail, GoogleSubject, AddedUtc, AddedBy from dbo.tblDDReviewTopAdmin order by GoogleEmail", cn);
        var list = new List<TopAdminRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new TopAdminRow((int)r["TopAdminID"], (string)r["GoogleEmail"], r["GoogleSubject"] is not DBNull, (DateTime)r["AddedUtc"], r["AddedBy"] is DBNull ? null : (string)r["AddedBy"]));
        return list;
    }

    public async Task<bool> AddTopAdminAsync(string email, string? by, CancellationToken ct = default)
    {
        email = (email ?? "").Trim();
        if (email.Length is 0 or > 320 || !email.Contains('@')) return false;
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
if exists (select 1 from dbo.tblDDReviewTopAdmin where upper(GoogleEmail) = upper(@e)) select 0
else begin insert dbo.tblDDReviewTopAdmin (GoogleEmail, AddedBy) values (@e, @by); select 1 end", cn);
        cmd.Parameters.Add("@e", System.Data.SqlDbType.VarChar, 320).Value = email;
        cmd.Parameters.Add("@by", System.Data.SqlDbType.VarChar, 320).Value = (object?)Trunc(by, 320) ?? DBNull.Value;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<bool> RemoveTopAdminAsync(int topAdminId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(_o.CentralConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("delete dbo.tblDDReviewTopAdmin where TopAdminID = @id and (select count(*) from dbo.tblDDReviewTopAdmin) > 1", cn);
        cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = topAdminId;
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AuditAsync(string action, string? googleSubject, string? email, int? webUserId, string? detail, string? clientIp, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(_o.CentralConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand(@"insert dbo.tblDDReviewGoogleAudit (GoogleSubject, GoogleEmail, Action, WebUserID, Detail, ClientIp) values (@s, @e, @a, @id, @d, @ip)", cn);
            cmd.Parameters.Add("@s", System.Data.SqlDbType.VarChar, 100).Value = (object?)Trunc(googleSubject, 100) ?? DBNull.Value;
            cmd.Parameters.Add("@e", System.Data.SqlDbType.VarChar, 320).Value = (object?)Trunc(email, 320) ?? DBNull.Value;
            cmd.Parameters.Add("@a", System.Data.SqlDbType.VarChar, 30).Value = Trunc(action, 30)!;
            cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = (object?)webUserId ?? DBNull.Value;
            cmd.Parameters.Add("@d", System.Data.SqlDbType.VarChar, 500).Value = (object?)Trunc(detail, 500) ?? DBNull.Value;
            cmd.Parameters.Add("@ip", System.Data.SqlDbType.VarChar, 50).Value = (object?)Trunc(clientIp, 50) ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException) { /* the audit table is new (sql/008); a missing table or permission must not block the action */ }
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
