using DDReview.Core.Tenancy;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Updater;

public enum UpdateStatus
{
    /// <summary>In place and usable by the web login.</summary>
    Ok,
    /// <summary>Not there yet.</summary>
    Missing,
    /// <summary>There, but the web login may not use it (a grant is missing).</summary>
    NotUsable,
    /// <summary>Was missing and has just been added.</summary>
    Applied,
    /// <summary>Needs the login to be allowed to create objects (db_ddladmin) and it is not.</summary>
    NeedsPermission,
    /// <summary>The script ran but the login still cannot use the result; a grant has to be run by someone with the rights.</summary>
    NeedsGrant,
    Failed,
}

public sealed record UpdateItem(string Id, string Title, UpdateStatus Status, string? Detail = null);

public sealed record UpdateReport(string Database, string DbUser, bool CanCreateObjects, IReadOnlyList<UpdateItem> Items)
{
    public bool AllOk => Items.All(i => i.Status is UpdateStatus.Ok or UpdateStatus.Applied);
    public bool NeedsWork => Items.Any(i => i.Status is UpdateStatus.Missing or UpdateStatus.NotUsable);

    /// <summary>What to run once as someone with rights, to let this login create objects (works on SQL Server 2012 and later).</summary>
    public string DdlAdminHint => $"exec sp_addrolemember N'db_ddladmin', N'{DbUser.Replace("'", "''")}';";
}

/// <summary>
/// Checks a client database for the objects DD Review adds, and adds the missing ones. It never grants itself anything:
/// if the login lacks the right to create objects, it says so and shows the one line an administrator runs.
/// </summary>
public sealed class ClientUpdater
{
    public async Task<UpdateReport> CheckAsync(string clientConnectionString, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        return await CheckAsync(cn, ct);
    }

    /// <summary>Applies every missing script. Only a client-database administrator may run it (checked here, not only in the page).</summary>
    public async Task<UpdateReport> ApplyAsync(string clientConnectionString, UserAccess access, CancellationToken ct = default)
    {
        if (!access.Administrator) throw new UnauthorizedAccessException("Only an administrator may update the database.");
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);

        var before = await CheckAsync(cn, ct);
        var results = new List<UpdateItem>();
        foreach (var script in ClientScripts.All)
        {
            var item = before.Items.First(i => i.Id == script.Id);
            if (item.Status is UpdateStatus.Ok) { results.Add(item); continue; }

            // Present but not usable: running the script changes nothing, so go straight to the grant hint.
            if (item.Status is UpdateStatus.Missing)
            {
                if (!before.CanCreateObjects)
                {
                    results.Add(item with { Status = UpdateStatus.NeedsPermission });
                    continue;
                }
                try { await RunAsync(cn, script, ct); }
                catch (SqlException ex) { results.Add(item with { Status = UpdateStatus.Failed, Detail = ex.Message }); continue; }
            }

            var (present, usable) = await ProbeAsync(cn, script, ct);
            if (present && usable) results.Add(item with { Status = item.Status is UpdateStatus.Missing ? UpdateStatus.Applied : UpdateStatus.Ok, Detail = null });
            else results.Add(item with { Status = UpdateStatus.NeedsGrant, Detail = Hint(script, before.DbUser) });
        }
        return before with { Items = results };
    }

    private async Task<UpdateReport> CheckAsync(SqlConnection cn, CancellationToken ct)
    {
        string db, user; bool canCreate;
        await using (var cmd = new SqlCommand(@"
select db_name(), user_name(),
       cast(case when is_member('db_owner') = 1 or is_member('db_ddladmin') = 1
                   or has_perms_by_name(db_name(), 'DATABASE', 'ALTER') = 1 then 1 else 0 end as bit)", cn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            await r.ReadAsync(ct);
            db = r.GetString(0); user = r.GetString(1); canCreate = r.GetBoolean(2);
        }

        var items = new List<UpdateItem>();
        foreach (var s in ClientScripts.All)
        {
            var (present, usable) = await ProbeAsync(cn, s, ct);
            var status = !present ? UpdateStatus.Missing : usable ? UpdateStatus.Ok : UpdateStatus.NotUsable;
            items.Add(new UpdateItem(s.Id, s.Title, status, status is UpdateStatus.NotUsable ? Hint(s, user) : null));
        }
        return new UpdateReport(db, user, canCreate, items);
    }

    private static string? Hint(ClientScript s, string user) => s.GrantHint?.Replace("{user}", user.Replace("]", "]]"));

    private static async Task<(bool Present, bool Usable)> ProbeAsync(SqlConnection cn, ClientScript s, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(s.CheckSql, cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return (r.GetInt32(0) == 1, r.GetInt32(1) == 1);
    }

    /// <summary>One transaction per script, so a failure part-way leaves the database as it was.</summary>
    private static async Task RunAsync(SqlConnection cn, ClientScript s, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            foreach (var batch in SqlBatches.Split(s.ReadSql()))
            {
                await using var cmd = new SqlCommand(batch, cn, tx) { CommandTimeout = 120 };
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }
    }
}
