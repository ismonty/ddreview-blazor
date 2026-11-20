using System.Data;
using DDReview.Core.Tenancy;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Features;

/// <summary>Optional DD Review features an administrator switches on per user. Off for everyone else until then.</summary>
public enum WebFeature { Payroll, ConsolidatedPayroll }

public sealed record FeatureUser(int WebUserId, string UserName, bool Administrator, IReadOnlySet<WebFeature> Enabled);

/// <summary>
/// Administrators always have every feature. Anyone else has a feature only if an administrator turned it on for them
/// (a row in dbo.tblWebUserFeatures; script 019). If that table is not there yet, non-administrators have nothing.
/// </summary>
public sealed class FeatureService
{
    public static string Key(WebFeature f) => f.ToString();

    public async Task<IReadOnlySet<WebFeature>> EnabledForAsync(string clientConnectionString, UserAccess access, CancellationToken ct = default)
    {
        if (access.Administrator) return Enum.GetValues<WebFeature>().ToHashSet();
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        return await ReadAsync(cn, access.ClientWebUserId, ct);
    }

    public async Task<bool> IsEnabledAsync(string clientConnectionString, UserAccess access, WebFeature feature, CancellationToken ct = default)
        => access.Administrator || (await EnabledForAsync(clientConnectionString, access, ct)).Contains(feature);

    /// <summary>Throws when the user may not use the feature. Pages call this on every action, not only when they open.</summary>
    public async Task RequireAsync(string clientConnectionString, UserAccess access, WebFeature feature, CancellationToken ct = default)
    {
        if (!await IsEnabledAsync(clientConnectionString, access, feature, ct))
            throw new UnauthorizedAccessException("This feature has not been turned on for your login. Ask an administrator.");
    }

    public async Task<IReadOnlyList<FeatureUser>> ListUsersAsync(string clientConnectionString, UserAccess admin, CancellationToken ct = default)
    {
        RequireAdmin(admin);
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        var users = new List<(int Id, string Name, bool Admin)>();
        await using (var cmd = new SqlCommand("select WebUserID, UserName, Administrator from dbo.tblWebUserSecurity order by UserName", cn) { CommandTimeout = 60 })
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) users.Add((Convert.ToInt32(r["WebUserID"]), Convert.ToString(r["UserName"])!.Trim(), r["Administrator"] is not DBNull && Convert.ToBoolean(r["Administrator"])));

        var on = new Dictionary<int, HashSet<WebFeature>>();
        if (await TableExistsAsync(cn, ct))
            await using (var cmd = new SqlCommand("select WebUserID, Feature from dbo.tblWebUserFeatures", cn) { CommandTimeout = 60 })
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    if (Enum.TryParse<WebFeature>(Convert.ToString(r["Feature"])!.Trim(), out var f))
                    {
                        var id = Convert.ToInt32(r["WebUserID"]);
                        if (!on.TryGetValue(id, out var set)) on[id] = set = [];
                        set.Add(f);
                    }
        return users.Select(u => new FeatureUser(u.Id, u.Name, u.Admin, u.Admin ? Enum.GetValues<WebFeature>().ToHashSet() : on.GetValueOrDefault(u.Id) ?? [])).ToList();
    }

    public async Task SetAsync(string clientConnectionString, UserAccess admin, int webUserId, WebFeature feature, bool enabled, CancellationToken ct = default)
    {
        RequireAdmin(admin);
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        if (!await TableExistsAsync(cn, ct)) throw new InvalidOperationException("The feature table is not in this database yet. Run the Database updater first.");
        await using var cmd = new SqlCommand(enabled
            ? @"if not exists (select 1 from dbo.tblWebUserFeatures where WebUserID = @u and Feature = @f)
                    insert dbo.tblWebUserFeatures (WebUserID, Feature, EnabledBy) values (@u, @f, @by)"
            : "delete dbo.tblWebUserFeatures where WebUserID = @u and Feature = @f", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = webUserId;
        cmd.Parameters.Add("@f", SqlDbType.VarChar, 40).Value = Key(feature);
        if (enabled) cmd.Parameters.Add("@by", SqlDbType.Int).Value = admin.ClientWebUserId;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    static void RequireAdmin(UserAccess a) { if (!a.Administrator) throw new UnauthorizedAccessException("Only an administrator may change who can use a feature."); }

    static async Task<bool> TableExistsAsync(SqlConnection cn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select case when object_id(N'dbo.tblWebUserFeatures', N'U') is null then 0 else 1 end", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    static async Task<HashSet<WebFeature>> ReadAsync(SqlConnection cn, int webUserId, CancellationToken ct)
    {
        var set = new HashSet<WebFeature>();
        if (!await TableExistsAsync(cn, ct)) return set;
        await using var cmd = new SqlCommand("select Feature from dbo.tblWebUserFeatures where WebUserID = @u", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@u", SqlDbType.Int).Value = webUserId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (Enum.TryParse<WebFeature>(Convert.ToString(r[0])!.Trim(), out var f)) set.Add(f);
        return set;
    }
}
