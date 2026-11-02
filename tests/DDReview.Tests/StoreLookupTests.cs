using DDReview.Core.Gl;
using Xunit;

namespace DDReview.Tests;

public class StoreLookupTests
{
    [Fact]
    public void Store_names_prefer_the_report_name_like_the_old_pages()
    {
        var sql = StoreLookup.ListSql(true, "");
        Assert.Contains("isnull(StoreReportName, isnull(StoreName, StoreGCode)) as StoreName", sql);
        Assert.Contains("order by StoreNumber", sql);
    }

    [Fact]
    public void A_database_without_the_report_name_column_uses_the_store_name()
    {
        var sql = StoreLookup.ListSql(false, " and StoreID in (@s0)");
        Assert.DoesNotContain("StoreReportName", sql);
        Assert.Contains("isnull(StoreName, StoreGCode) as StoreName", sql);
        Assert.Contains("Active > 0 and StoreID in (@s0)", sql);
    }
}
