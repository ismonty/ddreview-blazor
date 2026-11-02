using System.Security.Claims;
using DDReview.Core.Admin;
using DDReview.Core.Updater;

namespace DDReview.Tests;

public class SetupPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<SetupArea> NoGrants = new HashSet<SetupArea>();

    private static SetupActor Actor(bool admin = false, bool google = true, int? minutesAgo = 5, string? ip = "10.0.0.1") =>
        new(1, 2, "pat", admin, google, minutesAgo is { } m ? Now.AddMinutes(-m) : null, "pat@example.com", ip);

    [Fact] public void Administrator_with_a_fresh_google_session_may_use_every_area_and_manage_access()
    {
        var d = SetupPolicy.Decide(Actor(admin: true), NoGrants, Now);
        Assert.True(d.Allowed);
        foreach (var a in SetupAreas.All) Assert.True(d.Allows(a));
        Assert.True(d.CanManageAccess);
    }

    [Fact] public void Administrator_signed_in_with_a_password_is_asked_for_google()
    {
        var d = SetupPolicy.Decide(Actor(admin: true, google: false), NoGrants, Now);
        Assert.Equal(SetupDenial.NotGoogle, d.Denial);
        Assert.False(d.Allows(SetupArea.GlSetup));
        Assert.False(d.CanManageAccess);
    }

    [Fact] public void A_google_session_that_google_has_not_confirmed_lately_must_verify_again()
    {
        Assert.Equal(SetupDenial.NeedsVerification, SetupPolicy.Decide(Actor(admin: true, minutesAgo: 31), NoGrants, Now).Denial);
        Assert.Equal(SetupDenial.NeedsVerification, SetupPolicy.Decide(Actor(admin: true, minutesAgo: null), NoGrants, Now).Denial);
    }

    [Fact] public void Verification_lasts_exactly_thirty_minutes()
    {
        Assert.True(SetupPolicy.Decide(Actor(admin: true, minutesAgo: 30), NoGrants, Now).Allowed);
        Assert.False(SetupPolicy.Decide(Actor(admin: true, minutesAgo: 31), NoGrants, Now).Allowed);
    }

    [Fact] public void A_verification_time_in_the_future_is_not_trusted()
    {
        Assert.True(SetupPolicy.IsFresh(Now.AddMinutes(1), Now));      // a minute or two of clock difference is fine
        Assert.False(SetupPolicy.IsFresh(Now.AddMinutes(10), Now));
    }

    [Fact] public void Someone_with_no_grant_who_is_not_an_administrator_gets_nothing_even_with_google()
    {
        var d = SetupPolicy.Decide(Actor(), NoGrants, Now);
        Assert.Equal(SetupDenial.NoRights, d.Denial);
        Assert.False(d.Allowed);
    }

    [Fact] public void No_rights_is_reported_before_the_google_requirement()
    {
        Assert.Equal(SetupDenial.NoRights, SetupPolicy.Decide(Actor(google: false), NoGrants, Now).Denial);
    }

    [Fact] public void A_granted_user_gets_only_the_granted_area_and_never_access_management()
    {
        var d = SetupPolicy.Decide(Actor(), new HashSet<SetupArea> { SetupArea.GlSetup }, Now);
        Assert.True(d.Allows(SetupArea.GlSetup));
        Assert.False(d.Allows(SetupArea.ArSetup));
        Assert.False(d.Allows(SetupArea.StoreAndGlobal));
        Assert.False(d.CanManageAccess);
    }

    [Fact] public void A_granted_user_still_needs_google()
    {
        var grants = new HashSet<SetupArea> { SetupArea.ArSetup };
        Assert.Equal(SetupDenial.NotGoogle, SetupPolicy.Decide(Actor(google: false), grants, Now).Denial);
        Assert.Equal(SetupDenial.NeedsVerification, SetupPolicy.Decide(Actor(minutesAgo: 90), grants, Now).Denial);
    }

    [Fact] public void Only_a_verified_google_administrator_can_manage_access()
    {
        Assert.True(SetupPolicy.CanManageAccess(Actor(admin: true), Now));
        Assert.False(SetupPolicy.CanManageAccess(Actor(admin: false), Now));
        Assert.False(SetupPolicy.CanManageAccess(Actor(admin: true, google: false), Now));
        Assert.False(SetupPolicy.CanManageAccess(Actor(admin: true, minutesAgo: 45), Now));
    }

    [Theory]
    [InlineData(false, true, 5)]     // not an administrator
    [InlineData(true, false, 5)]     // password session
    [InlineData(true, true, 45)]     // verification ran out
    public async Task Changing_access_is_refused_before_any_database_is_touched(bool admin, bool google, int minutesAgo)
    {
        var store = new SetupAccessStore();
        var actor = Actor(admin, google, minutesAgo);
        // "Server=." is never reached: the check runs first, so these throw UnauthorizedAccessException rather than a connection error
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.GrantAsync("Server=.", actor, Now, 7, SetupArea.GlSetup));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.RevokeAsync("Server=.", actor, Now, 7, SetupArea.GlSetup));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ListUsersAsync("Server=.", actor, Now));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.RecentAuditAsync("Server=.", actor, Now));
    }
}

public class SetupClaimsTests
{
    private static ClaimsPrincipal Principal(params Claim[] c) => new(new ClaimsIdentity(c, "test"));

    [Fact] public void A_password_session_has_neither_claim()
    {
        var p = Principal(new Claim(ClaimTypes.Name, "pat"));
        Assert.False(SetupClaims.IsGoogleSession(p));
        Assert.Null(SetupClaims.VerifiedAt(p));
    }

    [Fact] public void Stamping_sets_google_and_the_time()
    {
        var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, "pat")], "test");
        var t = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);
        SetupClaims.StampGoogle(id, t);
        var p = new ClaimsPrincipal(id);
        Assert.True(SetupClaims.IsGoogleSession(p));
        Assert.Equal(t, SetupClaims.VerifiedAt(p));
    }

    [Fact] public void Stamping_again_replaces_the_old_time_instead_of_adding_a_second()
    {
        var id = new ClaimsIdentity([], "test");
        SetupClaims.StampGoogle(id, new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero));
        SetupClaims.StampGoogle(id, new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero));
        Assert.Single(id.FindAll(SetupClaims.VerifiedUtc));
        Assert.Single(id.FindAll(SetupClaims.AuthMethod));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero), SetupClaims.VerifiedAt(new ClaimsPrincipal(id)));
    }

    [Theory] [InlineData("")] [InlineData("abc")] [InlineData("-5")] [InlineData("1.5")]
    public void A_garbled_time_counts_as_never_verified(string value)
    {
        Assert.Null(SetupClaims.VerifiedAt(Principal(new Claim(SetupClaims.VerifiedUtc, value))));
    }

    [Fact] public void Another_auth_method_value_is_not_google()
    {
        Assert.False(SetupClaims.IsGoogleSession(Principal(new Claim(SetupClaims.AuthMethod, "password"))));
    }
}

public class SetupMiscTests
{
    [Theory] [InlineData("GlSetup", true)] [InlineData("ArSetup", true)] [InlineData("StoreAndGlobal", true)]
    [InlineData("glsetup", false)] [InlineData("7", false)] [InlineData("", false)] [InlineData(null, false)] [InlineData("GlSetup,ArSetup", false)]
    public void Area_names_parse_strictly(string? name, bool ok) => Assert.Equal(ok, SetupAreas.TryParse(name, out _));

    [Theory]
    [InlineData("/admin/setup", "/admin/setup")]
    [InlineData("/admin/setup/access", "/admin/setup/access")]
    [InlineData("/admin/setup/access?v=failed", "/admin/setup/access")]
    [InlineData(null, "/admin/setup")]
    [InlineData("", "/admin/setup")]
    [InlineData("https://evil.example/admin/x", "/admin/setup")]
    [InlineData("//evil.example/admin/x", "/admin/setup")]
    [InlineData("/admin//evil.example", "/admin/setup")]
    [InlineData("/account", "/admin/setup")]
    [InlineData("/admin/..\\x", "/admin/setup")]
    [InlineData("/admin/a:b", "/admin/setup")]
    public void The_return_path_after_verification_is_always_a_local_admin_page(string? given, string expected) =>
        Assert.Equal(expected, SetupReturn.SafePath(given));

    [Fact] public void Catalog_covers_every_area_and_names_real_looking_tables()
    {
        foreach (var a in SetupAreas.All) Assert.Contains(SetupCatalog.All, e => e.Area == a);
        Assert.All(SetupCatalog.All, e => Assert.StartsWith("tbl", e.Table));
        Assert.Equal(SetupCatalog.All.Count, SetupCatalog.All.Select(e => e.Title).Distinct().Count());
    }

    [Fact] public void Access_script_creates_both_tables_with_a_live_grant_index_and_is_registered_last()
    {
        Assert.Equal(["002", "004", "007"], ClientScripts.All.Select(s => s.Id));
        var sql = ClientScripts.All.Single(s => s.Id == "007").ReadSql();
        Assert.Contains("create table dbo.tblDDReviewAdminAccess", sql);
        Assert.Contains("create table dbo.tblDDReviewAdminAudit", sql);
        Assert.Contains("where RevokedUtc is null", sql);
        Assert.Equal(2, SqlBatches.Split(sql).Count);
    }

    [Fact] public void Grant_procedure_gives_the_web_login_no_delete_on_the_audit_trail()
    {
        var sql = ClientScripts.All.Single(s => s.Id == "004").ReadSql();
        Assert.Contains("grant select, insert, update on dbo.tblDDReviewAdminAccess", sql);
        Assert.Contains("grant select, insert on dbo.tblDDReviewAdminAudit", sql);
        Assert.DoesNotContain("delete on dbo.tblDDReviewAdminAudit", sql);
        Assert.DoesNotContain("update on dbo.tblDDReviewAdminAudit", sql);
    }

    [Fact] public void An_old_grant_procedure_is_noticed_so_the_updater_recreates_it()
    {
        var check = ClientScripts.All.Single(s => s.Id == "004").CheckSql;
        Assert.Contains("tblDDReviewAdminAccess", check);
        // the procedure tests for the two tables when it runs, so the order the scripts were applied in does not matter
        Assert.Contains("object_id(N'dbo.tblDDReviewAdminAccess', N'U') is not null", ClientScripts.All.Single(s => s.Id == "004").ReadSql());
    }
}
