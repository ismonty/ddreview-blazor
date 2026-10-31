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
    /// <summary>Links a Google account to a login. False when that Google account is already linked to a different login.</summary>
    Task<bool> LinkGoogleAsync(int webUserId, string googleSubject, string? email, CancellationToken ct = default);
    /// <summary>The email of the Google account linked to this login, or null when none.</summary>
    Task<string?> GetGoogleEmailAsync(int webUserId, CancellationToken ct = default);
    Task UnlinkGoogleAsync(int webUserId, CancellationToken ct = default);
}

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
        await using var cmd = new SqlCommand(@"
if exists (select 1 from dbo.tblWebUserGoogle where GoogleSubject = @s and WebUserID <> @id) select 0
else begin
    delete dbo.tblWebUserGoogle where WebUserID = @id and GoogleSubject <> @s;
    if not exists (select 1 from dbo.tblWebUserGoogle where GoogleSubject = @s)
        insert dbo.tblWebUserGoogle (GoogleSubject, WebUserID, GoogleEmail) values (@s, @id, @e)
    else update dbo.tblWebUserGoogle set GoogleEmail = @e where GoogleSubject = @s;
    select 1
end", cn);
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

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
