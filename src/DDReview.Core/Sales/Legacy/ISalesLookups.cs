using System.Data;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Sales.Legacy;

/// <summary>The old generator ran four tiny queries while building its script. They go through this seam so the generator can be tested without a database.</summary>
public interface ISalesLookups
{
    /// <param name="name">DayPartTable, tblCashAndSalesBatches, ProfitCenterSubstitutionTable or PseudoStoreSubstitutionTable</param>
    /// <param name="sql">The query the original code ran for that table.</param>
    DataTable GetTable(string name, string sql);
}

public sealed class SqlSalesLookups(string connectionString) : ISalesLookups
{
    public DataTable GetTable(string name, string sql)
    {
        using var cn = new SqlConnection(connectionString);
        using var adapter = new SqlDataAdapter(sql, cn);
        adapter.SelectCommand.CommandTimeout = 600;
        var t = new DataTable(name);
        adapter.Fill(t);
        return t;
    }
}
