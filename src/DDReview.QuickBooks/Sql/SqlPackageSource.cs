using System.Data;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using Microsoft.Data.SqlClient;

namespace DDReview.QuickBooks.Sql;

/// <summary>What to post: which stores, which dates, and which kinds (the ExportScope bits: 1 GL, 2 A/R, 4 labor, 8 COGS, 16 A/R adjustments).</summary>
public sealed record PostingRequest(IReadOnlyList<int> StoreIds, DateTime Start, DateTime End, int Scope);

/// <summary>Reads the export package from the DDAdminCS back-end database and its posting settings.</summary>
public static class SqlPackageSource
{
    public static List<PackageRow> Read(string connectionString, PostingRequest request, int commandTimeoutSeconds = 150)
    {
        using var cn = new SqlConnection(connectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = commandTimeoutSeconds;
        cmd.CommandText = "select * from dbo.fn_CashAndSalesGLandARMultiStoreSelectExportPackage2(@stores, @start, @end, @scope, 0, 0)";
        cmd.Parameters.Add("@stores", SqlDbType.VarChar, 8000).Value = string.Join(",", request.StoreIds);
        cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = request.Start.Date;
        cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = request.End.Date;
        cmd.Parameters.Add("@scope", SqlDbType.Int).Value = request.Scope;
        using var rd = cmd.ExecuteReader();
        var names = Enumerable.Range(0, rd.FieldCount).ToDictionary(rd.GetName, i => i, StringComparer.OrdinalIgnoreCase);
        var rows = new List<PackageRow>();
        while (rd.Read())
            rows.Add(PackageRow.FromLookup(n =>
                names.TryGetValue(n, out var i) && !rd.IsDBNull(i)
                    ? Convert.ToString(rd.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) : null));
        return rows;
    }

    /// <summary>One value from cs_spGlobalSettingsGetNoValue, the call DDAdminCS makes (the setting name is tag + subtag).</summary>
    public static (string String, int Int) Setting(SqlConnection cn, string tag, string subTag)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "cs_spGlobalSettingsGetNoValue";
        cmd.Parameters.Add("@GlobalSettingTag", SqlDbType.VarChar, 255).Value = tag;
        cmd.Parameters.Add("@GlobalSettingSubTag", SqlDbType.VarChar, 255).Value = subTag;
        cmd.Parameters.Add("@StringNoValue", SqlDbType.VarChar, 255).Value = "";
        cmd.Parameters.Add("@IntNoValue", SqlDbType.VarChar, 255).Value = "0";
        cmd.Parameters.Add("@BigIntNoValue", SqlDbType.VarChar, 255).Value = "0";
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return ("", 0);
        string s = rd["GlobalSettingStringValue"] is string sv ? sv : "";
        int i = rd["GlobalSettingIntValue"] is DBNull ? 0 : Convert.ToInt32(rd["GlobalSettingIntValue"]);
        return (s, i);
    }

    public static PostingOptions LoadOptions(string connectionString)
    {
        using var cn = new SqlConnection(connectionString);
        cn.Open();
        const string ar = "Quickbooks.AccountsReceivable.Export", gl = "Quickbooks.GeneralLedger.Export";
        return new PostingOptions
        {
            DetailPostingActive = Setting(cn, ar, "ExportPurchaseDetails").Int != 0,
            PostDetailAsInvoiceMemo = Setting(cn, ar, "ExportPurchaseDetailsInInvoiceMemo").Int != 0,
            ItemSalesTaxCode = Setting(cn, ar, "ExportInvoiceItemSalesTaxCode").String.Trim(),
            SalesTaxItemName = Setting(cn, gl, "Canada QuickBooks Item Sales Tax").String,
            SalesTaxItemListId = Setting(cn, gl, "Canada QuickBooks Item Sales Tax ListID").String,
            SalesTaxEntityName = Setting(cn, gl, "Canada QuickBooks Sales Tax Entity").String,
            SalesTaxEntityListId = Setting(cn, gl, "Canada QuickBooks Sales Tax Entity ListID").String,
        };
    }
}
