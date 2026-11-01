using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

public sealed record StoreInfo(int StoreId, int StoreNumber, string StoreName);

public static class StoreLookup
{
    /// <summary>
    /// The store name the old pages show everywhere: the report name when one is set, otherwise the store name
    /// (<c>isnull(StoreReportName, StoreName)</c>), and the store code as a last resort.
    /// </summary>
    public static string NameSql(bool hasReportName, string alias = "")
    {
        var a = alias.Length == 0 ? "" : alias + ".";
        return hasReportName ? $"isnull({a}StoreReportName, isnull({a}StoreName, {a}StoreGCode))" : $"isnull({a}StoreName, {a}StoreGCode)";
    }

    /// <summary>Whether this client database has tblStores.StoreReportName (the old site adds it on first use; very old databases may lack it).</summary>
    public static async Task<bool> HasReportNameAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand("select count(*) from INFORMATION_SCHEMA.COLUMNS where TABLE_NAME = 'tblStores' and COLUMN_NAME = 'StoreReportName'", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public static string ListSql(bool hasReportName, string where) =>
        $"select StoreID, StoreNumber, {NameSql(hasReportName)} as StoreName from dbo.tblStores where Active > 0{where} order by StoreNumber";

    static async Task<IReadOnlyList<StoreInfo>> ReadAsync(SqlConnection cn, string where, IReadOnlyList<int> ids, CancellationToken ct)
    {
        try { return await ReadCoreAsync(cn, true, where, ids, ct); }
        catch (SqlException ex) when (ex.Number == 207)   // invalid column name: a database that never got StoreReportName
        {
            return await ReadCoreAsync(cn, false, where, ids, ct);
        }
    }

    static async Task<IReadOnlyList<StoreInfo>> ReadCoreAsync(SqlConnection cn, bool hasReportName, string where, IReadOnlyList<int> ids, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(ListSql(hasReportName, where), cn);
        for (var i = 0; i < ids.Count; i++) cmd.Parameters.Add("@s" + i, System.Data.SqlDbType.Int).Value = ids[i];
        var list = new List<StoreInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(new StoreInfo((int)r["StoreID"], (int)r["StoreNumber"], ((string)r["StoreName"]).Trim()));
        return list;
    }

    public static async Task<IReadOnlyList<StoreInfo>> LoadAsync(string connectionString, IEnumerable<int> storeIds, CancellationToken ct = default)
    {
        var ids = storeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        // ids are ints we parsed ourselves; still built as parameters.
        return await ReadAsync(cn, $" and StoreID in ({string.Join(",", ids.Select((_, i) => "@s" + i))})", ids, ct);
    }

    /// <summary>Only active stores (tblStores.Active &gt; 0) are listed, as in the old pages.
    /// The stores this user may use for a report: every store when unrestricted, otherwise the authorized ones.</summary>
    public static async Task<IReadOnlyList<StoreInfo>> LoadAuthorizedAsync(string connectionString, DDReview.Core.Tenancy.UserAccess access, DDReview.Core.Tenancy.ReportKind kind, CancellationToken ct = default)
    {
        if (!access.AllStores.Contains(kind)) return await LoadAsync(connectionString, access.Stores[kind], ct);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        return await ReadAsync(cn, "", [], ct);
    }
}
