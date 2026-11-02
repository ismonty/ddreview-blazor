using Microsoft.Data.SqlClient;

namespace DDReview.Core.Admin;

/// <summary>The audit trail: every change made through the Setup pages writes one row, inside the same transaction as the change.</summary>
public static class SetupAudit
{
    public static async Task WriteAsync(SqlConnection cn, SqlTransaction? tx, SetupActor actor, SetupArea area, string table, string? key,
        string action, string? oldJson, string? newJson, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(@"
insert dbo.tblDDReviewAdminAudit (CentralWebUserID, ClientWebUserID, UserName, GoogleEmail, Area, TableName, KeyValue, Action, OldValues, NewValues, ClientIp)
values (@c, @w, @u, @e, @a, @t, @k, @x, @o, @n, @ip)", cn, tx);
        cmd.Parameters.Add("@c", System.Data.SqlDbType.Int).Value = actor.CentralWebUserId;
        cmd.Parameters.Add("@w", System.Data.SqlDbType.Int).Value = actor.ClientWebUserId;
        cmd.Parameters.Add("@u", System.Data.SqlDbType.VarChar, 50).Value = Cut(actor.UserName, 50) ?? (object)DBNull.Value;
        cmd.Parameters.Add("@e", System.Data.SqlDbType.VarChar, 320).Value = Cut(actor.GoogleEmail, 320) ?? (object)DBNull.Value;
        cmd.Parameters.Add("@a", System.Data.SqlDbType.VarChar, 30).Value = area.ToString();
        cmd.Parameters.Add("@t", System.Data.SqlDbType.VarChar, 128).Value = Cut(table, 128)!;
        cmd.Parameters.Add("@k", System.Data.SqlDbType.VarChar, 200).Value = Cut(key, 200) ?? (object)DBNull.Value;
        cmd.Parameters.Add("@x", System.Data.SqlDbType.VarChar, 10).Value = Cut(action, 10)!;
        cmd.Parameters.Add("@o", System.Data.SqlDbType.NVarChar, -1).Value = oldJson ?? (object)DBNull.Value;
        cmd.Parameters.Add("@n", System.Data.SqlDbType.NVarChar, -1).Value = newJson ?? (object)DBNull.Value;
        cmd.Parameters.Add("@ip", System.Data.SqlDbType.VarChar, 64).Value = Cut(actor.ClientIp, 64) ?? (object)DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string? Cut(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>One client login as the grants page shows it.</summary>
public sealed record SetupUserRow(int WebUserId, string UserName, bool Administrator, IReadOnlySet<SetupArea> Areas);

/// <summary>What is in dbo.tblDDReviewAdminAccess for one login. <see cref="TablesMissing"/> means sql/007 has not been run in that database, or the web login has not been granted the table.</summary>
public sealed record SetupGrants(IReadOnlySet<SetupArea> Areas, bool TablesMissing);

/// <summary>Reads and changes who may use the Setup pages, in the client database.</summary>
public sealed class SetupAccessStore
{
    private const int InvalidObjectName = 208;   // sql/007 not run here yet
    private const int PermissionDenied = 229;    // tables there, but the web login has not been granted them (usp_DDReviewGrantAccess)

    public async Task<SetupGrants> LoadGrantsAsync(string clientConnectionString, int clientWebUserId, CancellationToken ct = default)
    {
        var areas = new HashSet<SetupArea>();
        try
        {
            await using var cn = new SqlConnection(clientConnectionString);
            await cn.OpenAsync(ct);
            await using var cmd = new SqlCommand("select Area from dbo.tblDDReviewAdminAccess where WebUserID = @u and RevokedUtc is null", cn) { CommandTimeout = 30 };
            cmd.Parameters.Add("@u", System.Data.SqlDbType.Int).Value = clientWebUserId;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (SetupAreas.TryParse(r.GetString(0), out var a)) areas.Add(a);
            return new SetupGrants(areas, false);
        }
        catch (SqlException ex) when (ex.Number is InvalidObjectName or PermissionDenied)
        {
            return new SetupGrants(areas, true);
        }
    }

    /// <summary>Every client login, with the areas an administrator has granted it. Passwords are never read.</summary>
    public async Task<IReadOnlyList<SetupUserRow>> ListUsersAsync(string clientConnectionString, SetupActor actor, DateTimeOffset now, CancellationToken ct = default)
    {
        RequireManager(actor, now);
        var grants = new Dictionary<int, HashSet<SetupArea>>();
        var users = new List<(int Id, string Name, bool Admin)>();
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        await using (var cmd = new SqlCommand("select WebUserID, UserName, Administrator from dbo.tblWebUserSecurity order by UserName", cn) { CommandTimeout = 30 })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) users.Add((r.GetInt32(0), r.GetString(1).Trim(), r.GetBoolean(2)));
        await using (var cmd = new SqlCommand("select WebUserID, Area from dbo.tblDDReviewAdminAccess where RevokedUtc is null", cn) { CommandTimeout = 30 })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (SetupAreas.TryParse(r.GetString(1), out var a))
                {
                    if (!grants.TryGetValue(r.GetInt32(0), out var set)) grants[r.GetInt32(0)] = set = [];
                    set.Add(a);
                }
        return users.Select(u => new SetupUserRow(u.Id, u.Name, u.Admin, grants.TryGetValue(u.Id, out var s) ? s : new HashSet<SetupArea>())).ToList();
    }

    /// <summary>Allows <paramref name="area"/> for a non-administrator login. False when nothing changed (no such login, an administrator, or already allowed).</summary>
    public async Task<bool> GrantAsync(string clientConnectionString, SetupActor actor, DateTimeOffset now, int targetWebUserId, SetupArea area, CancellationToken ct = default)
    {
        RequireManager(actor, now);
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            string? name;
            await using (var who = new SqlCommand("select UserName from dbo.tblWebUserSecurity where WebUserID = @t and Administrator = 0", cn, tx))
            {
                who.Parameters.Add("@t", System.Data.SqlDbType.Int).Value = targetWebUserId;
                name = (await who.ExecuteScalarAsync(ct)) as string;
            }
            if (name is null) { await tx.RollbackAsync(ct); return false; }

            int added;
            await using (var ins = new SqlCommand(@"
if not exists (select 1 from dbo.tblDDReviewAdminAccess where WebUserID = @t and Area = @a and RevokedUtc is null)
    insert dbo.tblDDReviewAdminAccess (WebUserID, Area, GrantedByWebUserID) values (@t, @a, @by);", cn, tx))
            {
                ins.Parameters.Add("@t", System.Data.SqlDbType.Int).Value = targetWebUserId;
                ins.Parameters.Add("@a", System.Data.SqlDbType.VarChar, 30).Value = area.ToString();
                ins.Parameters.Add("@by", System.Data.SqlDbType.Int).Value = actor.ClientWebUserId;
                added = await ins.ExecuteNonQueryAsync(ct);
            }
            if (added > 0)
                await SetupAudit.WriteAsync(cn, tx, actor, area, "tblDDReviewAdminAccess", targetWebUserId.ToString(), "grant",
                    null, $"{{\"user\":{Json(name)},\"area\":\"{area}\"}}", ct);
            await tx.CommitAsync(ct);
            return added > 0;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    /// <summary>Takes <paramref name="area"/> away from a login (the row stays, stamped as revoked). False when there was nothing to revoke.</summary>
    public async Task<bool> RevokeAsync(string clientConnectionString, SetupActor actor, DateTimeOffset now, int targetWebUserId, SetupArea area, CancellationToken ct = default)
    {
        RequireManager(actor, now);
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            int changed;
            await using (var upd = new SqlCommand(@"
update dbo.tblDDReviewAdminAccess set RevokedUtc = sysutcdatetime(), RevokedByWebUserID = @by
where WebUserID = @t and Area = @a and RevokedUtc is null", cn, tx))
            {
                upd.Parameters.Add("@t", System.Data.SqlDbType.Int).Value = targetWebUserId;
                upd.Parameters.Add("@a", System.Data.SqlDbType.VarChar, 30).Value = area.ToString();
                upd.Parameters.Add("@by", System.Data.SqlDbType.Int).Value = actor.ClientWebUserId;
                changed = await upd.ExecuteNonQueryAsync(ct);
            }
            if (changed > 0)
                await SetupAudit.WriteAsync(cn, tx, actor, area, "tblDDReviewAdminAccess", targetWebUserId.ToString(), "revoke",
                    $"{{\"area\":\"{area}\"}}", null, ct);
            await tx.CommitAsync(ct);
            return changed > 0;
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    /// <summary>The newest audit rows, for the grants page.</summary>
    public async Task<IReadOnlyList<SetupAuditRow>> RecentAuditAsync(string clientConnectionString, SetupActor actor, DateTimeOffset now, int top = 50, CancellationToken ct = default)
    {
        RequireManager(actor, now);
        var rows = new List<SetupAuditRow>();
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
select top (@n) AtUtc, UserName, GoogleEmail, Area, TableName, KeyValue, Action, OldValues, NewValues
from dbo.tblDDReviewAdminAudit order by AuditID desc", cn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@n", System.Data.SqlDbType.Int).Value = Math.Clamp(top, 1, 500);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new SetupAuditRow(new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc)),
                r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8)));
        return rows;
    }

    private static void RequireManager(SetupActor actor, DateTimeOffset now)
    {
        if (!SetupPolicy.CanManageAccess(actor, now))
            throw new UnauthorizedAccessException("Only an administrator signed in with Google (and verified in the last 30 minutes) may change who can use Setup.");
    }

    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}

public sealed record SetupAuditRow(DateTimeOffset AtUtc, string UserName, string? GoogleEmail, string Area, string TableName, string? Key,
    string Action, string? OldValues, string? NewValues);
