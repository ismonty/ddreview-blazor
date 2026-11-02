using DDReview.QuickBooks.Launch;

namespace DDReview.Web;

/// <summary>
/// The "Post to QuickBooks" button's side of the one-click launch (see docs/QUICKBOOKS.md). Off unless <c>PosterLink:SigningKeyPkcs8</c> and <c>PosterLink:KeyId</c> are set.
/// The private signing key lives only in the server's secret settings, never in git. <c>PosterLink:LifetimeMinutes</c> (default 10, at most 30) is how long a link stays valid.
/// </summary>
public static class PosterLinks
{
    public const string Section = "PosterLink";

    public static void AddPosterLinks(this WebApplicationBuilder builder)
    {
        var c = builder.Configuration.GetSection(Section);
        builder.Services.AddSingleton(new PosterLinkService(c["SigningKeyPkcs8"], c["KeyId"], c.GetValue("LifetimeMinutes", 10)));
    }
}
