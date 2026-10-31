using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

public sealed record StoreInfo(int StoreId, int StoreNumber, string StoreName);

public static class StoreLookup
{
    public static async Task<IReadOnlyList<StoreInfo>> LoadAsync(string connectionString, IEnumerable<int> storeIds, CancellationToken ct = default)
    {
        var ids = storeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        // ids are ints we parsed ourselves; still built as parameters.
        var names = ids.Select((_, i) => "@s" + i).ToList();
        await using var cmd = new SqlCommand($"select StoreID, StoreNumber, isnull(StoreName, StoreGCode) as StoreName from dbo.tblStores where Active > 0 and StoreID in ({string.Join(",", names)}) order by StoreNumber", cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add(names[i], System.Data.SqlDbType.Int).Value = ids[i];
        var list = new List<StoreInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new StoreInfo((int)r["StoreID"], (int)r["StoreNumber"], (string)r["StoreName"]));
        return list;
    }

    /// <summary>Only active stores (tblStores.Active &gt; 0) are listed, as in the old pages.
    /// The stores this user may use for a report: every store when unrestricted, otherwise the authorized ones.</summary>
    public static async Task<IReadOnlyList<StoreInfo>> LoadAuthorizedAsync(string connectionString, DDReview.Core.Tenancy.UserAccess access, DDReview.Core.Tenancy.ReportKind kind, CancellationToken ct = default)
    {
        if (!access.AllStores.Contains(kind)) return await LoadAsync(connectionString, access.Stores[kind], ct);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("select StoreID, StoreNumber, isnull(StoreName, StoreGCode) as StoreName from dbo.tblStores where Active > 0 order by StoreNumber", cn);
        var list = new List<StoreInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new StoreInfo((int)r["StoreID"], (int)r["StoreNumber"], (string)r["StoreName"]));
        return list;
    }
}
