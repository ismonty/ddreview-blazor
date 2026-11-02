using System.Data;
using System.Text.RegularExpressions;
using DDReview.Core.Admin;

namespace DDReview.Tests;

public class SetupTableDeclarationTests
{
    // Column names of the three tables in a real client database (schema only, from the Access back end's own schema dump).
    private static readonly Dictionary<string, string> Schema = new()
    {
        ["tblAccountsClasses"] = "AccountClassID AccountClass InterClassSameClassAccount InterClassSameClassDistReference OtherAccountClassID InterClassOtherClassAccount InterClassOtherClassDistReference InterClassSameClassDebitCreditFlag InterClassOtherClassDebitCreditFlag ExportFlag InterClassSameClassDefaultQuickBooksType InterClassOtherClassDefaultQuickBooksType",
        ["tblTaxTypes"] = "TaxTypeID TaxType RoyaltyFee",
        ["tblTaxRates"] = "StoreID TaxTypeID TaxPayee TaxAcct TaxRate ItemID Marker Class AccountClassID Vendor DistReference DebitAccount DebitAccountClassID DebitDistReference RoyaltyFee DebitQuickBooksType CreditQuickBooksType RoyaltyPeriodStartDate RoyaltyPeriodEndDate RoyaltyPeriodSalesExcluded RoyaltyPeriodSalesExpected ProfitCenterAccountClassID Enabled",
    };

    // Columns that older client databases may not have yet (the old Access form fell back without them).
    private static readonly HashSet<string> MayBeMissing = ["UseTaxApplies", "UseTaxPercentage"];

    [Fact] public void Every_declared_column_exists_in_the_real_schema_or_is_known_to_be_optional()
    {
        foreach (var t in SetupTables.All)
        {
            var real = Schema[t.Table].Split(' ').ToHashSet();
            Assert.Contains(t.Key, real);
            foreach (var c in t.Columns) Assert.True(real.Contains(c.Name) || MayBeMissing.Contains(c.Name), $"{t.Id}: {c.Name} is not a column of {t.Table}");
            if (t.StoreColumn is not null) Assert.Contains(t.StoreColumn, real);
            if (t.FilterColumn is not null) Assert.Contains(t.FilterColumn, real);
        }
    }

    [Fact] public void Identifiers_are_plain_names_so_they_can_never_carry_sql()
    {
        var ok = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");
        foreach (var t in SetupTables.All)
        {
            Assert.Matches(ok, t.Table); Assert.Matches(ok, t.Key);
            foreach (var c in t.Columns) Assert.Matches(ok, c.Name);
            if (t.StoreColumn is not null) Assert.Matches(ok, t.StoreColumn);
            if (t.FilterColumn is not null) Assert.Matches(ok, t.FilterColumn);
        }
    }

    [Fact] public void Declarations_are_consistent()
    {
        Assert.Equal(SetupTables.All.Count, SetupTables.All.Select(t => t.Id).Distinct().Count());
        foreach (var t in SetupTables.All)
        {
            var names = t.Columns.Select(c => c.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.DoesNotContain(t.Key, names);                                   // the key is never an editable field
            if (t.StoreColumn is not null) Assert.DoesNotContain(t.StoreColumn, names);
            if (t.FilterColumn is not null) Assert.DoesNotContain(t.FilterColumn, names);
            foreach (var o in t.Order ?? []) Assert.Contains(o.Column, names);
            foreach (var c in t.Columns.Where(c => c.Lookup is not null)) Assert.NotNull(SetupLookups.Sql(c.Lookup!));
            Assert.Equal("/admin/setup/table/" + t.Id, t.Route);
        }
    }

    [Fact] public void Tax_and_royalty_pages_share_a_table_but_never_each_others_rows()
    {
        Assert.Equal(SetupTables.TaxRates.Table, SetupTables.RoyaltyFeeRates.Table);
        Assert.False(SetupTables.TaxRates.FilterValue);
        Assert.True(SetupTables.RoyaltyFeeRates.FilterValue);
        Assert.Equal("RoyaltyFee", SetupTables.TaxRates.FilterColumn);
    }

    [Fact] public void Built_pages_are_linked_from_the_landing_page_and_resolve()
    {
        foreach (var t in SetupTables.All)
        {
            var e = SetupCatalog.All.Single(x => x.Route == t.Route);
            Assert.Equal(t.Area, e.Area);
            Assert.Equal(t.Table, e.Table);
            Assert.Same(t, SetupTables.Find(t.Id));
        }
        Assert.Null(SetupTables.Find("nope"));
        Assert.Null(SetupTables.Find(null));
        Assert.Null(SetupTables.Find("Account-Classes"));          // ids are matched exactly
    }
}

public class SetupSqlTests
{
    [Fact] public void Select_lists_the_key_first_and_orders_by_the_declared_order_then_the_key()
    {
        var sql = SetupSql.Select(SetupTables.TaxRates, ["TaxTypeID", "TaxRate", "TaxAcct"]);
        Assert.Equal("select [ItemID], [TaxTypeID], [TaxRate], [TaxAcct] from dbo.[tblTaxRates] where [StoreID] = @store and [RoyaltyFee] = @filter order by [TaxTypeID], [TaxRate] desc, [ItemID]", sql);
    }

    [Fact] public void Select_skips_order_columns_this_database_does_not_have()
    {
        Assert.EndsWith("order by [AccountClass], [AccountClassID]", SetupSql.Select(SetupTables.AccountClasses, ["AccountClass", "ExportFlag"]));
        Assert.EndsWith("order by [AccountClassID]", SetupSql.Select(SetupTables.AccountClasses, ["ExportFlag"]));
    }

    [Fact] public void A_table_without_store_or_filter_has_no_where()
    {
        Assert.DoesNotContain("where", SetupSql.Select(SetupTables.TaxTypes, ["TaxType"]));
    }

    [Fact] public void Insert_uses_only_parameters_and_returns_the_new_key()
    {
        var sql = SetupSql.Insert(SetupTables.TaxRates, ["TaxPayee", "StoreID", "RoyaltyFee"]);
        Assert.Equal("insert dbo.[tblTaxRates] ([TaxPayee], [StoreID], [RoyaltyFee]) output inserted.[ItemID] values (@v_TaxPayee, @v_StoreID, @v_RoyaltyFee)", sql);
    }

    [Fact] public void Insert_with_nothing_typed_lets_every_default_apply()
    {
        Assert.Equal("insert dbo.[tblTaxTypes] output inserted.[TaxTypeID] default values", SetupSql.Insert(SetupTables.TaxTypes, []));
    }

    [Fact] public void Update_changes_only_the_named_columns_and_requires_each_to_still_hold_its_old_value()
    {
        var sql = SetupSql.Update(SetupTables.TaxRates, ["TaxRate", "Vendor"]);
        Assert.Equal("update dbo.[tblTaxRates] set [TaxRate] = @v_TaxRate, [Vendor] = @v_Vendor where [ItemID] = @key and [StoreID] = @store and [RoyaltyFee] = @filter"
            + " and ([TaxRate] = @o_TaxRate or ([TaxRate] is null and @o_TaxRate is null))"
            + " and ([Vendor] = @o_Vendor or ([Vendor] is null and @o_Vendor is null))", sql);
    }

    [Fact] public void Update_and_delete_cannot_reach_a_row_of_another_store_or_the_other_rate_kind()
    {
        foreach (var sql in new[] { SetupSql.Update(SetupTables.RoyaltyFeeRates, ["Enabled"]), SetupSql.Delete(SetupTables.RoyaltyFeeRates) })
        {
            Assert.Contains("[StoreID] = @store", sql);
            Assert.Contains("[RoyaltyFee] = @filter", sql);
            Assert.Contains("[ItemID] = @key", sql);
        }
    }

    [Fact] public void Brackets_in_a_name_are_escaped()
    {
        Assert.Equal("[a]]b]", SetupSql.Q("a]b"));
    }

    [Fact] public void No_value_is_ever_written_into_the_statement_text()
    {
        foreach (var t in SetupTables.All)
        {
            var cols = t.Columns.Select(c => c.Name).ToList();
            foreach (var sql in new[] { SetupSql.Insert(t, cols), SetupSql.Update(t, cols), SetupSql.Delete(t), SetupSql.Select(t, cols) })
                Assert.DoesNotContain("'", sql);
        }
    }
}

public class SetupValueTests
{
    private static SetupDbColumn Db(string type, int len = 0, int precision = 0, int scale = 0, bool nullable = true) => new("c", type, nullable, false, false, len, precision, scale);
    private static SetupColumn Col(SetupKind k) => new("c", "Thing", k);

    private static object? Parse(SetupColumn c, SetupDbColumn d, string text, out string? error)
    {
        var ok = SetupValue.TryParse(c, d, text, out var v, out error);
        Assert.Equal(error is null, ok);
        return v;
    }

    [Fact] public void Text_is_trimmed_and_length_checked()
    {
        Assert.Equal("abc", Parse(Col(SetupKind.Text), Db("varchar", 5), "  abc ", out var e)); Assert.Null(e);
        Parse(Col(SetupKind.Text), Db("varchar", 5), "abcdef", out e); Assert.Contains("at most 5", e);
    }

    [Fact] public void A_max_length_column_has_no_length_limit()
    {
        Assert.Equal(new string('x', 9000), Parse(Col(SetupKind.Text), Db("varchar", -1), new string('x', 9000), out _));
    }

    [Fact] public void Accented_text_is_stored_as_typed()
    {
        Assert.Equal("Café Receipts", Parse(Col(SetupKind.Text), Db("varchar", 50), "Café Receipts", out _));
    }

    [Theory]
    [InlineData("int", "12", true)] [InlineData("int", "-3", true)] [InlineData("int", "1.5", false)] [InlineData("int", "abc", false)] [InlineData("int", "99999999999", false)]
    [InlineData("smallint", "32767", true)] [InlineData("smallint", "32768", false)]
    [InlineData("tinyint", "255", true)] [InlineData("tinyint", "-1", false)] [InlineData("tinyint", "256", false)]
    public void Whole_numbers_are_range_checked_for_the_column_type(string type, string text, bool ok)
    {
        Parse(Col(SetupKind.Int), Db(type), text, out var e);
        Assert.Equal(ok, e is null);
    }

    [Theory]
    [InlineData("money", 0, 0, "6.5", true)] [InlineData("money", 0, 0, "$1,234.50", true)] [InlineData("money", 0, 0, "x", false)] [InlineData("money", 0, 0, "9999999999999999", false)]
    [InlineData("decimal", 18, 2, "99.99", true)] [InlineData("decimal", 5, 2, "1000", false)] [InlineData("decimal", 5, 2, "999.99", true)]
    public void Decimals_are_checked_against_the_column(string type, int precision, int scale, string text, bool ok)
    {
        Parse(Col(SetupKind.Decimal), Db(type, 0, precision, scale), text, out var e);
        Assert.Equal(ok, e is null);
    }

    [Theory] [InlineData("true", true)] [InlineData("1", true)] [InlineData("False", false)] [InlineData("0", false)]
    public void Yes_no_values(string text, bool expected) => Assert.Equal(expected, Parse(Col(SetupKind.Bool), Db("bit"), text, out _));

    [Fact] public void A_yes_no_that_is_neither_is_refused() { Parse(Col(SetupKind.Bool), Db("bit"), "maybe", out var e); Assert.NotNull(e); }

    [Theory] [InlineData("2026-11-02", true)] [InlineData("11/2/2026", true)] [InlineData("2026-13-40", false)] [InlineData("yesterday", false)] [InlineData("1600-01-01", false)]
    public void Dates(string text, bool ok) { Parse(Col(SetupKind.Date), Db("datetime"), text, out var e); Assert.Equal(ok, e is null); }

    [Fact] public void Format_is_canonical_so_equal_values_compare_equal()
    {
        var c = Col(SetupKind.Decimal);
        Assert.Equal(SetupValue.Format(c, 6.5m), SetupValue.Format(c, 6.50m));
        Assert.Equal("6.5", SetupValue.Format(c, 6.5000m));
        Assert.Equal("", SetupValue.Format(c, null));
        Assert.Equal("", SetupValue.Format(c, DBNull.Value));
        Assert.Equal("true", SetupValue.Format(c, true));
        Assert.Equal("2026-11-02", SetupValue.Format(c, new DateTime(2026, 11, 2, 13, 0, 0)));
        Assert.Equal("abc", SetupValue.Format(c, "abc   "));          // char columns come back space-padded
        Assert.Equal("7", SetupValue.Format(c, (short)7));
    }
}

public class SetupGridParameterTests
{
    private static SetupDbColumn Db(string type, int len = 0, int precision = 0, int scale = 0) => new("c", type, true, false, false, len, precision, scale);

    [Fact] public void Parameters_take_their_type_from_the_column_and_a_null_becomes_dbnull()
    {
        var p = SetupGridService.Param("@v_c", Db("varchar", 30), "abc");
        Assert.Equal(SqlDbType.VarChar, p.SqlDbType); Assert.Equal(30, p.Size); Assert.Equal("abc", p.Value);
        Assert.Equal(DBNull.Value, SetupGridService.Param("@v_c", Db("varchar", 30), null).Value);
        Assert.Equal(-1, SetupGridService.Param("@v_c", Db("varchar", -1), "x").Size);
    }

    [Fact] public void Numbers_are_converted_to_the_columns_own_type()
    {
        Assert.IsType<int>(SetupGridService.Param("@v_c", Db("int"), 5L).Value);
        Assert.IsType<short>(SetupGridService.Param("@v_c", Db("smallint"), 5L).Value);
        Assert.IsType<byte>(SetupGridService.Param("@v_c", Db("tinyint"), 5L).Value);
        Assert.Equal(SqlDbType.Money, SetupGridService.Param("@v_c", Db("money"), 1.5m).SqlDbType);
    }

    [Fact] public void A_decimal_parameter_keeps_the_columns_precision_and_scale()
    {
        var p = SetupGridService.Param("@v_c", Db("decimal", 0, 18, 6), 1.234567m);
        Assert.Equal(SqlDbType.Decimal, p.SqlDbType); Assert.Equal(18, p.Precision); Assert.Equal(6, p.Scale);
    }

    [Fact] public void A_column_type_the_grid_cannot_write_is_not_offered()
    {
        Assert.False(Db("uniqueidentifier").Supported);
        Assert.False(Db("varbinary").Supported);
        Assert.True(Db("varchar").Supported);
    }

    [Fact] public void Required_means_not_null_without_a_default_and_not_an_identity()
    {
        Assert.True(new SetupDbColumn("c", "varchar", false, false, false, 10, 0, 0).Required);
        Assert.False(new SetupDbColumn("c", "bit", false, false, true, 0, 0, 0).Required);
        Assert.False(new SetupDbColumn("c", "varchar", true, false, false, 10, 0, 0).Required);
        Assert.False(new SetupDbColumn("c", "int", false, true, false, 0, 0, 0).Required);
    }
}

public class SetupGridAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
    private static SetupActor Actor(bool admin = true, bool google = true, int? minutesAgo = 5) =>
        new(1, 2, "pat", admin, google, minutesAgo is { } m ? Now.AddMinutes(-m) : null, null, null);
    private static SetupDecision Decide(SetupActor a, params SetupArea[] grants) => SetupPolicy.Decide(a, grants.ToHashSet(), Now);

    private static readonly SetupGridService Svc = new();
    private const string Unreachable = "Server=.";     // never reached: every check below runs first

    private static async Task AssertRefusedEverywhere(SetupActor actor, SetupDecision decision, SetupTable table)
    {
        var row = new SetupRow(1, new Dictionary<string, object?>());
        var edits = new Dictionary<string, string?>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.LoadAsync(Unreachable, actor, decision, Now, table, 1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.StoresAsync(Unreachable, actor, decision, Now, table));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.InsertAsync(Unreachable, actor, decision, Now, table, 1, edits));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.UpdateAsync(Unreachable, actor, decision, Now, table, 1, row, edits));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.DeleteAsync(Unreachable, actor, decision, Now, table, 1, 1));
    }

    [Fact] public async Task A_password_session_may_not_read_or_change_anything()
    {
        var a = Actor(google: false);
        await AssertRefusedEverywhere(a, Decide(a), SetupTables.TaxRates);
    }

    [Fact] public async Task A_verification_that_has_run_out_may_not_read_or_change_anything()
    {
        var a = Actor(minutesAgo: 31);
        await AssertRefusedEverywhere(a, Decide(a), SetupTables.AccountClasses);
    }

    [Fact] public async Task A_login_with_no_grant_may_not_read_or_change_anything()
    {
        var a = Actor(admin: false);
        await AssertRefusedEverywhere(a, Decide(a), SetupTables.TaxTypes);
    }

    [Fact] public async Task A_grant_for_another_area_does_not_open_this_one()
    {
        var a = Actor(admin: false);
        await AssertRefusedEverywhere(a, Decide(a, SetupArea.ArSetup), SetupTables.TaxRates);
    }

    [Fact] public async Task A_decision_made_earlier_does_not_outlive_the_verification()
    {
        var a = Actor(minutesAgo: 25);
        var decision = Decide(a);                      // allowed when made...
        Assert.True(decision.Allowed);
        var later = Now.AddMinutes(10);                // ...but the service looks at the clock itself
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Svc.LoadAsync(Unreachable, a, decision, later, SetupTables.TaxRates, 1));
    }

    [Fact] public void A_granted_area_passes_the_check_and_other_areas_do_not()
    {
        var a = Actor(admin: false);
        var d = Decide(a, SetupArea.GlSetup);
        SetupGridService.Require(a, d, Now, SetupArea.GlSetup);
        Assert.Throws<UnauthorizedAccessException>(() => SetupGridService.Require(a, d, Now, SetupArea.ArSetup));
    }

    [Fact] public void An_administrator_with_a_fresh_google_session_passes_for_every_area()
    {
        var a = Actor();
        foreach (var area in SetupAreas.All) SetupGridService.Require(a, Decide(a), Now, area);
    }

    [Fact] public async Task A_table_that_cannot_be_deleted_from_refuses_before_touching_the_database()
    {
        var t = SetupTables.TaxTypes with { AllowDelete = false };
        var a = Actor();
        var r = await Svc.DeleteAsync(Unreachable, a, Decide(a), Now, t, null, 1);
        Assert.Equal(SetupOutcome.Refused, r.Outcome);
    }
}
