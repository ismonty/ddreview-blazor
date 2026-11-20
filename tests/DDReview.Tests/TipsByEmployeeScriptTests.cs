using System.Text.RegularExpressions;
using DDReview.Core.Updater;
using Xunit;

namespace DDReview.Tests;

public class TipsByEmployeeScriptTests
{
    static string Sql() => ClientScripts.All.Single(x => x.Id == "020").ReadSql();

    [Fact]
    public void Script_is_registered_last_and_embedded()
    {
        Assert.Equal("020", ClientScripts.All[^1].Id);
        Assert.Contains("fn_TipsByEmployee", Sql());
    }

    [Fact]
    public void Script_only_creates_or_alters_its_own_two_functions()
    {
        var sql = Sql();
        Assert.DoesNotContain("drop ", sql, StringComparison.OrdinalIgnoreCase);
        var targets = Regex.Matches(sql, @"ALTER\s+FUNCTION\s+dbo\.(\w+)", RegexOptions.IgnoreCase).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(["fn_TipsByEmployee", "fn_CustomReport_TipsExportNew"], targets);
        Assert.DoesNotMatch(new Regex(@"\b(update|delete|insert)\s+(into\s+)?dbo\.tbl", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Joins_between_tables_with_a_store_id_include_it()
    {
        // house rule: a join between two store-keyed tables always carries StoreID
        var sql = Sql();
        foreach (Match m in Regex.Matches(sql, @"JOIN\s+dbo\.(tbl\w+)\s+(\w+)\s+ON\s+([^\r\n]+)", RegexOptions.IgnoreCase))
        {
            if (m.Groups[1].Value.Equals("tblGlobal", StringComparison.OrdinalIgnoreCase)) continue;
            Assert.Contains("StoreID", m.Groups[3].Value);
        }
    }

    [Fact]
    public void Script_has_no_dependence_on_the_old_work_tables_or_store_13()
    {
        var sql = Sql();
        Assert.DoesNotContain("TempTable", sql);
        Assert.DoesNotContain("(13,", sql);
    }
}
