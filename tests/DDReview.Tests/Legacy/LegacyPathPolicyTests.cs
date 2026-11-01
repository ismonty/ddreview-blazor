using DDReview.Core.Legacy;

namespace DDReview.Tests.Legacy;

public class LegacyPathPolicyTests
{
    static LegacyProxyOptions Opt(bool handoff = false, bool downloads = false)
        => new() { HandoffSecret = handoff ? new string('s', 40) : "", AllowDownloads = downloads };

    [Theory]
    [InlineData("/classic/")]
    [InlineData("/classic/Default.aspx")]
    [InlineData("/classic/DDSalesReview.aspx")]
    [InlineData("/classic/default.ASPX")]
    [InlineData("/classic/images/KlimtTilePrussianBlueDarker.png")]
    [InlineData("/classic/navimg/ddsubmit.png")]
    [InlineData("/classic/Styles.css")]
    [InlineData("/classic/jsDatePick.min.1.3.js")]
    [InlineData("/classic/ChartAxd.axd")]
    [InlineData("/classic/ScriptResource.axd")]
    [InlineData("/classic/WebResource.axd")]
    [InlineData("/classic/LoginPage.aspx")]
    public void Allows_normal_old_site_paths(string path) => Assert.True(LegacyPathPolicy.IsAllowed(path, Opt()));

    [Theory]
    [InlineData("/classic/web.config")]
    [InlineData("/classic/Web.Config")]
    [InlineData("/classic/trace.axd")]
    [InlineData("/classic/elmah.axd")]
    [InlineData("/classic/SomethingElse.axd")]
    [InlineData("/classic/bin/DDReviewAppCode.dll")]
    [InlineData("/classic/App_Code/LoginPageClass.cs")]
    [InlineData("/classic/TempFiles/x.png")]
    [InlineData("/classic/Default.aspx.cs")]
    [InlineData("/classic/DDReview.master")]
    [InlineData("/classic/download/DDReviewClient.msi")]
    [InlineData("/classic/../Default.aspx")]
    [InlineData("/classic/images/../web.config")]
    [InlineData("/classic/images/./a.png")]
    [InlineData("/classic/%2e%2e/web.config")]
    [InlineData("/classic/images%2fa.png")]
    [InlineData("/classic\\web.config")]
    [InlineData("/classic//Default.aspx")]
    [InlineData("/classic/(S(abc))/Default.aspx")]
    [InlineData("/classic/Default.aspx;x=1")]
    [InlineData("/classic/Default.aspx/extra.txt")]
    [InlineData("/classic/web.config.")]
    [InlineData("/classic/web.config ")]
    [InlineData("/classic/default.aspx::$DATA")]
    [InlineData("/classicother/Default.aspx")]
    [InlineData("/classic")]
    [InlineData("/Default.aspx")]
    [InlineData("")]
    [InlineData(null)]
    public void Blocks_everything_else(string? path) => Assert.False(LegacyPathPolicy.IsAllowed(path, Opt()));

    [Fact]
    public void Old_login_page_is_blocked_once_handoff_is_on()
    {
        Assert.True(LegacyPathPolicy.IsAllowed("/classic/LoginPage.aspx", Opt(handoff: false)));
        Assert.False(LegacyPathPolicy.IsAllowed("/classic/LoginPage.aspx", Opt(handoff: true)));
        Assert.True(LegacyPathPolicy.IsAllowed("/classic/Handoff.aspx", Opt(handoff: true)));
    }

    [Fact]
    public void Downloads_only_when_switched_on()
    {
        Assert.False(LegacyPathPolicy.IsAllowed("/classic/download/DDReviewClient.msi", Opt(downloads: true)));   // .msi is not an allowed type anyway
        Assert.False(LegacyPathPolicy.IsAllowed("/classic/download/page.aspx", Opt()));
        Assert.True(LegacyPathPolicy.IsAllowed("/classic/download/page.aspx", Opt(downloads: true)));
    }

    [Fact]
    public void Very_long_paths_are_refused() => Assert.False(LegacyPathPolicy.IsAllowed("/classic/" + new string('a', 500) + ".aspx", Opt()));
}
