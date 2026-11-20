using DDReview.Core.Legacy;
using Xunit;

namespace DDReview.Tests;

public class LegacyWarmupTests
{
    sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<Uri> Seen { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Seen.Add(r.RequestUri!); return Task.FromResult(f(r)); }
    }

    [Fact]
    public async Task Warm_up_asks_the_upstream_for_the_login_page_with_no_cookies_and_counts_any_answer_as_warm()
    {
        var h = new Stub(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var (ok, _, note) = await LegacyWarmer.WarmOnceAsync(new HttpClient(h), "http://127.0.0.1:8081", "/classic/LoginPage.aspx");
        Assert.True(ok);
        Assert.Equal("HTTP 200", note);
        Assert.Equal("http://127.0.0.1:8081/classic/LoginPage.aspx", h.Seen.Single().ToString());
        var (ok2, _, note2) = await LegacyWarmer.WarmOnceAsync(new HttpClient(new Stub(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Found))), "http://127.0.0.1:8081/", "classic/LoginPage.aspx");
        Assert.True(ok2); Assert.Equal("HTTP 302", note2);
    }

    [Fact]
    public async Task A_connection_failure_is_reported_not_thrown()
    {
        var h = new Stub(_ => throw new HttpRequestException("refused"));
        var (ok, _, note) = await LegacyWarmer.WarmOnceAsync(new HttpClient(h), "http://127.0.0.1:8081", "/classic/LoginPage.aspx");
        Assert.False(ok);
        Assert.Contains("refused", note);
    }

    [Fact]
    public void Defaults_warm_every_four_minutes_below_the_twenty_minute_idle_time_out()
    {
        var o = new LegacyProxyOptions();
        Assert.Equal(4, o.WarmupMinutes);
        Assert.Equal("/classic/LoginPage.aspx", o.WarmupPath);
        Assert.False(o.Enabled);
    }
}
