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
        await using var cmd = new SqlCommand($"select StoreID, StoreNumber, isnull(StoreName, StoreGCode) as StoreName from dbo.tblStores where StoreID in ({string.Join(",", names)}) order by StoreNumber", cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add(names[i], System.Data.SqlDbType.Int).Value = ids[i];
        var list = new List<StoreInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new StoreInfo((int)r["StoreID"], (int)r["StoreNumber"], (string)r["StoreName"]));
        return list;
    }
}
