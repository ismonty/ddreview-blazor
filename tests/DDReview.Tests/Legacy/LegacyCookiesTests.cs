using DDReview.Core.Legacy;

namespace DDReview.Tests.Legacy;

public class LegacyCookiesTests
{
    [Fact]
    public void Removes_new_site_cookies_and_keeps_the_old_sites()
        => Assert.Equal("ASP.NET_SessionId=abc; .ASPXFORMSAUTH=xyz",
            LegacyCookies.Strip("ddreview.auth=SECRET; ASP.NET_SessionId=abc; .AspNetCore.Antiforgery.q=1; .ASPXFORMSAUTH=xyz; ddreview.external=g",
                "ddreview.", ".AspNetCore."));

    [Fact]
    public void Returns_null_when_nothing_is_left()
    {
        Assert.Null(LegacyCookies.Strip("ddreview.auth=SECRET", "ddreview."));
        Assert.Null(LegacyCookies.Strip("", "ddreview."));
        Assert.Null(LegacyCookies.Strip(null, "ddreview."));
    }

    [Fact]
    public void Matches_on_name_not_value()
        => Assert.Equal("a=ddreview.x", LegacyCookies.Strip("a=ddreview.x", "ddreview."));
}
