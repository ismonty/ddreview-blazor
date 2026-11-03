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
