using System.Data;
using DDReview.Core.Drill;
using DDReview.Core.Sales;

namespace DDReview.Tests;

public class PairSetTests
{
    [Fact]
    public void A_group_is_every_store_with_every_item()
    {
        using var scope = PairScope.Begin();
        var sql = scope.Output("1,2,3:8901,8902 1,2:8903 3:8904", "css.StoreID", "css.PLU")!;
        var pairs = scope.ToTable().Rows.Cast<DataRow>().Select(r => ((int)r["StoreID"], (long)r["ItemID"])).ToHashSet();
        var expected = new HashSet<(int, long)>
        {
            (1, 8901), (1, 8902), (2, 8901), (2, 8902), (3, 8901), (3, 8902), (1, 8903), (2, 8903), (3, 8904),
        };
        Assert.True(expected.SetEquals(pairs));
        Assert.Contains("exists (select 1 from @DrillPairs dp1 where dp1.Dim = 1 and dp1.StoreID = css.StoreID and dp1.ItemID = css.PLU)", sql);
        Assert.DoesNotContain("8901", sql);        // no list text reaches the SQL
    }

    [Fact]
    public void Same_pairs_for_the_same_columns_share_a_dimension_and_different_ones_do_not()
    {
        using var scope = PairScope.Begin();
        var a = scope.Output("1:5", "x.StoreID", "x.ID");
        var b = scope.Output("1:5", "x.StoreID", "x.ID");
        var c = scope.Output("1:6", "x.StoreID", "x.ID");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Contains("Dim = 2", c);
    }

    [Fact]
    public void Plain_lists_stay_plain_and_are_checked()
    {
        using var scope = PairScope.Begin();
        Assert.Equal("css.PLU in (5,6)", scope.Output("5,6", "css.StoreID", "css.PLU"));
        Assert.False(scope.HasPairs);
        Assert.Equal("", scope.Output("", "a", "b"));
        Assert.Throws<ArgumentException>(() => scope.Output("1:5); drop table x;--", "a", "b"));
    }

    [Fact]
    public void Scopes_nest_and_end()
    {
        Assert.Null(PairScope.Current);
        using (PairScope.Begin()) { Assert.NotNull(PairScope.Current); }
        Assert.Null(PairScope.Current);
    }

    static SalesRequest Req() => new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), SalesDimensions.MenuItem, [1, 2, 3],
        Filters: new Dictionary<SalesDimensions, string> { [SalesDimensions.MenuItem] = "1,2,3:8901,8902 1,2:8903" });

    [Fact]
    public void Without_a_scope_the_script_is_the_text_chain_as_before()
    {
        var s = new SalesService().BuildScript("", Req(), new SalesScriptCharacterizationTests_Lookups());
        Assert.Contains("css.StoreID in (1,2,3) and css.PLU in (8901,8902)", s);
        Assert.DoesNotContain("@DrillPairs", s);
    }

    [Fact]
    public void With_a_scope_the_script_looks_pairs_up_instead()
    {
        using var scope = PairScope.Begin();
        var s = new SalesService().BuildScript("", Req(), new SalesScriptCharacterizationTests_Lookups());
        Assert.Contains("from @DrillPairs", s);
        Assert.DoesNotContain("css.PLU in (8901", s);
        Assert.True(scope.HasPairs);
        Assert.Equal(8, scope.ToTable().Rows.Count);
    }
}
