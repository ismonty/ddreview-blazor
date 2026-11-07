using DDReview.Core.Admin;
using DDReview.Core.Security;
using DDReview.Core.Tenancy;
using Xunit;

namespace DDReview.Tests;

public class MultiDatabaseTests
{
    private static CentralAccount A(int id, string name) => new(id, name, "p", null, "Server=x;Database=y", "u", "p", 3, null, null, false, true, false, false, null);
    private static (LoginServiceTests.FakeCentral c, LoginService s) Make()
    {
        var c = new LoginServiceTests.FakeCentral(A(1, "valentinis"), A(2, "acme"), A(3, "zeta"));
        return (c, new LoginService(c, new LoginThrottle(), false));
    }

    [Fact] public async Task One_linked_login_signs_straight_in()
    {
        var (c, s) = Make(); c.Link("g", 1);
        var o = await s.GoogleOptionsAsync("g", "a@b.c", true);
        Assert.False(o.MustChoose); Assert.False(o.IsTopAdmin); Assert.Single(o.Choices);
    }

    [Fact] public async Task Two_linked_logins_give_a_list_with_friendly_names_first_by_name()
    {
        var (c, s) = Make(); c.Link("g", 1); c.Link("g", 3); c.Names[1] = "Valentini's Downtown"; c.Names[3] = "Another Place";
        var o = await s.GoogleOptionsAsync("g", null, false);
        Assert.True(o.MustChoose);
        Assert.Equal(["Another Place", "Valentini's Downtown"], o.Choices.Select(x => x.Label));
    }

    [Fact] public async Task Login_without_a_friendly_name_shows_its_user_name()
    {
        var (c, s) = Make(); c.Link("g", 2); c.Link("g", 3);
        Assert.Equal(["acme", "zeta"], (await s.GoogleOptionsAsync("g", null, false)).Choices.Select(x => x.Label));
    }

    [Fact] public async Task Unknown_google_account_has_nothing_to_choose()
    {
        var (_, s) = Make();
        var o = await s.GoogleOptionsAsync("nobody", null, false);
        Assert.Empty(o.Choices); Assert.False(o.IsTopAdmin);
    }

    [Fact] public async Task Top_admin_sees_every_login_even_with_no_links()
    {
        var (c, s) = Make(); c.TopAdmins.Add("boss");
        var o = await s.GoogleOptionsAsync("boss", "boss@x.y", true);
        Assert.True(o.IsTopAdmin); Assert.True(o.MustChoose); Assert.Equal(3, o.Choices.Count);
    }

    [Fact] public async Task Picking_a_linked_login_signs_in_and_is_audited()
    {
        var (c, s) = Make(); c.Link("g", 1); c.Link("g", 2);
        var r = await s.SignInWithGoogleChoiceAsync("g", null, false, 2, "1.1.1.1", "ua", "h");
        Assert.Equal(LoginOutcome.Success, r.Outcome); Assert.Equal(2, r.Account!.WebUserId); Assert.Contains("pick", c.Audit);
    }

    [Fact] public async Task Picking_a_login_you_are_not_linked_to_is_refused()
    {
        var (c, s) = Make(); c.Link("g", 1); c.Link("g", 2);
        var r = await s.SignInWithGoogleChoiceAsync("g", null, false, 3, null, null, null);
        Assert.Equal(LoginOutcome.InvalidCredentials, r.Outcome); Assert.Equal(0, c.Logged);
    }

    [Fact] public async Task Top_admin_may_pick_any_login_and_gets_that_logins_own_account()
    {
        var (c, s) = Make(); c.TopAdmins.Add("boss");
        var r = await s.SignInWithGoogleChoiceAsync("boss", null, false, 3, null, null, null);
        Assert.Equal(LoginOutcome.Success, r.Outcome); Assert.Equal(3, r.Account!.WebUserId); Assert.Contains("pick-top", c.Audit);
    }

    [Fact] public async Task Picking_a_login_that_does_not_exist_is_refused_even_for_a_top_admin()
    {
        var (c, s) = Make(); c.TopAdmins.Add("boss");
        Assert.Equal(LoginOutcome.InvalidCredentials, (await s.SignInWithGoogleChoiceAsync("boss", null, false, 99, null, null, null)).Outcome);
    }

    [Fact] public async Task Linking_a_second_login_keeps_the_first_link()
    {
        var (c, _) = Make(); c.Link("g", 1);
        Assert.True(await c.LinkGoogleAsync(2, "g", "me@x.y"));
        Assert.True(await c.IsLinkedAsync("g", 1)); Assert.True(await c.IsLinkedAsync("g", 2));
    }

    [Fact] public async Task Inactive_logins_are_left_out_of_the_list_and_cannot_be_picked()
    {
        var (c, s) = Make(); c.Link("g", 1); c.Link("g", 2); c.Inactive.Add(2);
        Assert.Equal(["valentinis"], (await s.GoogleOptionsAsync("g", null, false)).Choices.Select(x => x.Label));
        Assert.Equal(LoginOutcome.InvalidCredentials, (await s.SignInWithGoogleChoiceAsync("g", null, false, 2, null, null, null)).Outcome);
        Assert.Equal(LoginOutcome.Success, (await s.SignInWithGoogleChoiceAsync("g", null, false, 1, null, null, null)).Outcome);
    }

    [Fact] public async Task Top_admin_can_show_and_pick_inactive_logins()
    {
        var (c, s) = Make(); c.TopAdmins.Add("boss"); c.Inactive.Add(3);
        Assert.Equal(2, (await s.GoogleOptionsAsync("boss", null, true)).Choices.Count);
        var all = (await s.GoogleOptionsAsync("boss", null, true, null, includeInactive: true)).Choices;
        Assert.Equal(3, all.Count); Assert.True(all.Single(x => x.WebUserId == 3).Inactive);
        Assert.Equal(LoginOutcome.Success, (await s.SignInWithGoogleChoiceAsync("boss", null, true, 3, null, null, null)).Outcome);
    }

    [Fact] public async Task Only_a_fresh_top_admin_can_change_the_active_flag()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss");
        var svc = new CentralAdminService(c, new Clock());
        await svc.SetInactiveAsync(Actor(), 2, true);
        Assert.Contains(2, c.Inactive); Assert.Contains("inactive", c.Audit);
        await svc.SetInactiveAsync(Actor(), 2, false);
        Assert.DoesNotContain(2, c.Inactive);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SetInactiveAsync(Actor("someone"), 2, true));
    }

    // ---- friendly names and top-level administrators ----
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private static CentralActor Actor(string? sub = "boss", bool google = true, int minutesAgo = 5) =>
        new(sub, "boss@x.y", google, Now.AddMinutes(-minutesAgo), "1.1.1.1");

    [Fact] public async Task Admin_pages_need_top_admin_google_and_a_recent_check()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss");
        var svc = new CentralAdminService(c, new Clock());
        Assert.Equal(CentralDenial.None, await svc.DecideAsync(Actor()));
        Assert.Equal(CentralDenial.NotGoogle, await svc.DecideAsync(Actor(google: false)));
        Assert.Equal(CentralDenial.NotGoogle, await svc.DecideAsync(Actor(sub: null)));
        Assert.Equal(CentralDenial.NotTopAdmin, await svc.DecideAsync(Actor(sub: "someone")));
        Assert.Equal(CentralDenial.NeedsVerification, await svc.DecideAsync(Actor(minutesAgo: 31)));
    }

    [Fact] public async Task Top_admin_can_rename_and_the_change_is_audited()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss");
        var svc = new CentralAdminService(c, new Clock());
        Assert.True(await svc.SetFriendlyNameAsync(Actor(), 2, "  Acme Pizza "));
        Assert.Equal("Acme Pizza", c.Names[2]); Assert.Contains("rename", c.Audit);
        Assert.True(await svc.SetFriendlyNameAsync(Actor(), 2, ""));
        Assert.False(c.Names.ContainsKey(2));
        Assert.False(await svc.SetFriendlyNameAsync(Actor(), 99, "x"));
    }

    [Fact] public async Task Everything_refuses_a_caller_who_is_not_a_fresh_top_admin()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss");
        var svc = new CentralAdminService(c, new Clock());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.SetFriendlyNameAsync(Actor("someone"), 2, "x"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListLoginsAsync(Actor(minutesAgo: 40), null, 10));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.AddTopAdminAsync(Actor(google: false), "x@y.z"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.UnlinkLoginAsync(Actor("someone"), 1));
        Assert.Empty(c.Names);
    }

    [Fact] public async Task The_last_top_admin_cannot_be_removed()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss");
        var svc = new CentralAdminService(c, new Clock());
        await svc.AddTopAdminAsync(Actor(), "one@x.y"); await svc.AddTopAdminAsync(Actor(), "two@x.y");
        var rows = await svc.ListTopAdminsAsync(Actor());
        Assert.True(await svc.RemoveTopAdminAsync(Actor(), rows[0]));
        Assert.False(await svc.RemoveTopAdminAsync(Actor(), (await svc.ListTopAdminsAsync(Actor()))[0]));
    }

    [Fact] public async Task Admin_unlink_removes_the_login_link()
    {
        var (c, _) = Make(); c.TopAdmins.Add("boss"); c.Link("g", 1);
        await new CentralAdminService(c, new Clock()).UnlinkLoginAsync(Actor(), 1);
        Assert.False(await c.IsLinkedAsync("g", 1)); Assert.Contains("unlink-admin", c.Audit);
    }

    [Theory] [InlineData("true", true)] [InlineData("True", true)] [InlineData("false", false)] [InlineData(null, false)] [InlineData("1", false)]
    public void Email_verified_claim_parsing(string? v, bool expected) => Assert.Equal(expected, GoogleClaims.IsTrue(v));
}

public class UserAccessCopyTests
{
    [Fact] public void AsAdministrator_copies_the_access_and_leaves_the_original_alone()
    {
        var a = new UserAccess { ClientWebUserId = 5, UserName = "bob" };
        a.Stores[ReportKind.Sales].Add(7); a.AllStores.Add(ReportKind.Payroll);
        var b = a.AsAdministrator();
        Assert.True(b.Administrator); Assert.False(a.Administrator);
        Assert.Equal(5, b.ClientWebUserId); Assert.Contains(7, b.Stores[ReportKind.Sales]);
        b.Stores[ReportKind.Sales].Add(9); Assert.DoesNotContain(9, a.Stores[ReportKind.Sales]);
    }
}

public class DatabaseGroupTests
{
    private static LoginChoice L(int id, string name, int? db, bool inactive = false) => new(id, name, null, null, inactive, db);

    [Fact] public void Logins_are_grouped_under_their_database_in_short_name_order_and_empty_databases_are_left_out()
    {
        var dbs = new[] { new DatabaseRow(2, "Zebra", "DDCS-Zebra"), new DatabaseRow(1, "Acme", "DDCS-Acme"), new DatabaseRow(3, "Empty", "DDCS-Empty") };
        var g = DatabaseGroups.Build(dbs, [L(10, "z1", 2), L(11, "a1", 1), L(12, "a2", 1)]);
        Assert.Equal(["Acme", "Zebra"], g.Select(x => x.Label));
        Assert.Equal(2, g[0].Logins.Count);
        Assert.Equal(["Acme", "Empty", "Zebra"], DatabaseGroups.Build(dbs, [L(10, "z1", 2), L(11, "a1", 1)], keepEmpty: true).Select(x => x.Label));
    }

    [Fact] public void Same_short_name_gets_the_id_added_and_a_missing_short_name_falls_back_to_the_database_name()
    {
        var dbs = new[] { new DatabaseRow(80, "GRC-CS", "GRC-CS"), new DatabaseRow(81, "GRC-CS", "GRC-CS"), new DatabaseRow(5, "", "DDCS-Plain") };
        var g = DatabaseGroups.Build(dbs, [L(1, "a", 80), L(2, "b", 81), L(3, "c", 5)]);
        Assert.Equal(["DDCS-Plain", "GRC-CS (ID 80)", "GRC-CS (ID 81)"], g.Select(x => x.Label));
    }

    [Fact] public void Logins_with_no_database_or_an_unknown_one_go_last_under_other()
    {
        var g = DatabaseGroups.Build([new DatabaseRow(1, "Acme", "x")], [L(1, "a", 1), L(2, "loose", null), L(3, "gone", 99)]);
        Assert.Equal(["Acme", "Other logins"], g.Select(x => x.Label));
        Assert.Equal(["loose", "gone"], g[1].Logins.Select(x => x.UserName));
    }

    [Fact] public async Task A_top_admin_gets_the_databases_with_their_options_and_others_do_not()
    {
        var c = new LoginServiceTests.FakeCentral(new CentralAccount(1, "u1", "p", null, "Server=x", "u", "p", 3, null, null, false, true, false, false, null));
        c.Databases.Add(new DatabaseRow(1, "Acme", "x")); c.DbOf[1] = 1; c.TopAdmins.Add("boss"); c.Link("g", 1);
        var s = new LoginService(c, new DDReview.Core.Security.LoginThrottle(), false);
        Assert.Single((await s.GoogleOptionsAsync("boss", null, true)).Databases!);
        Assert.Null((await s.GoogleOptionsAsync("g", null, true)).Databases);
    }
}

public class TaxVarianceTests
{
    private static DDReview.Core.Gl.TaxVarianceLine L(string store, decimal amount, decimal variance) =>
        new(new DateTime(2026, 11, 1), store, null, null, null, null, "Burger", 7, false, 8.25m, 7m, null, null, 1m, amount, variance);

    [Fact] public void Summary_adds_up_sales_variance_and_counts_stores()
    {
        var s = DDReview.Core.Gl.TaxVariance.Summarize([L("A", 100m, 1.25m), L("A", 50m, 0.625m), L("B", 10m, 0.125m)]);
        Assert.Equal(3, s.Lines); Assert.Equal(160m, s.Sales); Assert.Equal(2.0m, s.Variance); Assert.Equal(2, s.Stores);
    }

    [Fact] public void No_variances_summarizes_to_zero()
    {
        var s = DDReview.Core.Gl.TaxVariance.Summarize([]);
        Assert.Equal(0, s.Lines); Assert.Equal(0m, s.Variance);
    }
}

public class AdjustValidationTests
{
    private static DDReview.Core.Gl.GlAdjustRow Gl(decimal? debit = 10m, decimal? credit = null, string? account = "1000 Cash", int cls = 1, int batch = 5) =>
        new(0, batch, account, debit, credit, "ref", null, null, cls, null, 0, null);
    private static DDReview.Core.Gl.SalesAdjustRow Sales(decimal? amount = -5m, string? offset = "4000 Sales", int ca = 3, int cls = 1) =>
        new(0, 5, ca, amount, offset, cls, null);

    [Fact] public void A_good_gl_line_passes() => Assert.Null(DDReview.Core.Gl.AdjustValidation.Check(Gl()));

    [Theory]
    [InlineData(null, null, "1000", "Enter a debit or a credit.")]
    [InlineData(0, 0, "1000", "Enter a debit or a credit.")]
    [InlineData(5, 5, "1000", "Enter a debit or a credit, not both.")]
    [InlineData(-5, null, "1000", "Debit and credit cannot be negative. Use the other column instead.")]
    [InlineData(5, null, " ", "Enter the account.")]
    public void Bad_gl_lines_say_why(int? debit, int? credit, string account, string expected) =>
        Assert.Equal(expected, DDReview.Core.Gl.AdjustValidation.Check(Gl(debit, credit, account)));

    [Fact] public void Gl_line_needs_a_batch_and_a_class()
    {
        Assert.Equal("Choose the batch.", DDReview.Core.Gl.AdjustValidation.Check(Gl(batch: 0)));
        Assert.Equal("Choose the class.", DDReview.Core.Gl.AdjustValidation.Check(Gl(cls: 0)));
    }

    [Fact] public void Sales_adjustment_may_be_negative_but_not_zero_and_needs_an_offset()
    {
        Assert.Null(DDReview.Core.Gl.AdjustValidation.Check(Sales()));
        Assert.Null(DDReview.Core.Gl.AdjustValidation.Check(Sales(amount: 12.5m)));
        Assert.NotNull(DDReview.Core.Gl.AdjustValidation.Check(Sales(amount: 0m)));
        Assert.NotNull(DDReview.Core.Gl.AdjustValidation.Check(Sales(amount: null)));
        Assert.Equal("Enter the offset account.", DDReview.Core.Gl.AdjustValidation.Check(Sales(offset: "")));
        Assert.Equal("Choose the sales account.", DDReview.Core.Gl.AdjustValidation.Check(Sales(ca: 0)));
    }

    [Theory]
    [InlineData("12.50", true, 12.5)] [InlineData("1,234.5", true, 1234.5)] [InlineData("-3", true, -3.0)] [InlineData("", true, null)]
    [InlineData("1.234", false, null)] [InlineData("abc", false, null)] [InlineData("99999999999", false, null)]
    public void Money_parsing(string text, bool ok, double? expected)
    {
        Assert.Equal(ok, DDReview.Core.Gl.AdjustValidation.TryMoney(text, out var v));
        Assert.Equal(expected is null ? null : (decimal?)Convert.ToDecimal(expected), v);
    }

    [Fact] public void Batch_label_names_store_date_and_batch() =>
        Assert.Equal("Main St, Sun Nov 1, 2026 (batch 7)", new DDReview.Core.Gl.AdjustBatch(7, 1, "Main St", new DateTime(2026, 11, 1)).Label);
}
