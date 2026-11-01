namespace DDReview.Core.Legacy;

/// <summary>Settings for the /classic reverse proxy to the old site. Section name <c>LegacyProxy</c>. The secret never goes in git.</summary>
public sealed class LegacyProxyOptions
{
    public const string Section = "LegacyProxy";
    /// <summary>Off by default: nothing is proxied until the old site is installed and this is set.</summary>
    public bool Enabled { get; set; }
    /// <summary>Where the old site listens, loopback only, e.g. http://127.0.0.1:8081 . It must itself be published as the application "/classic".</summary>
    public string Upstream { get; set; } = "http://127.0.0.1:8081";
    public string PathPrefix { get; set; } = "/classic";
    /// <summary>Shared HMAC secret (32+ characters) for handoff tickets. Empty = handoff off and the old login page is allowed through (double login).</summary>
    public string HandoffSecret { get; set; } = "";
    public int TicketSeconds { get; set; } = 60;
    /// <summary>Largest request body passed to the old site, in megabytes.</summary>
    public int MaxRequestMegabytes { get; set; } = 10;
    /// <summary>The old site's login page. Reachable only while handoff is off.</summary>
    public string LegacyLoginPath { get; set; } = "/classic/LoginPage.aspx";
    /// <summary>Where the old site accepts a ticket (the small file the owner adds in VS 2008).</summary>
    public string HandoffPath { get; set; } = "/classic/Handoff.aspx";
    /// <summary>Installer downloads (/classic/download/...) stay blocked unless the owner turns them on.</summary>
    public bool AllowDownloads { get; set; }

    public bool HandoffOn => HandoffSecret.Length >= LegacyTicket.MinSecretLength;
}
