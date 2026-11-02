using DDReview.QuickBooks.Sql;

namespace DDReview.QuickBooks.Launch;

public enum PosterLinkProblem
{
    NotALink, Malformed, UnsupportedVersion, BadDates, BadScope, BadStores, UnknownKey, BadSignature, Expired, ClockOff,
}

/// <summary>Why a link was refused, in words a person at the poster (or on the site) can act on.</summary>
public sealed record PosterLinkError(PosterLinkProblem Problem, string Message);

/// <summary>A link whose signature, expiry and every parameter checked out.</summary>
public sealed record VerifiedLaunch(PostingRequest Request, int WebUserId, string KeyId, DateTimeOffset Expires);

/// <summary>
/// The limits the website and the poster both enforce on a "Post to QuickBooks" link. The site refuses to make a link outside them and the poster
/// refuses to accept one outside them, so a signed link that somehow breaks a rule is still turned away.
/// </summary>
public static class PosterLinkRules
{
    public const string Scheme = "ddposter";
    public const string Prefix = "ddposter://run";
    public const int Version = 1;

    /// <summary>Day 0 of the DD databases (the first day any restaurant's data was posted). Nothing earlier is accepted. A database may define a later day 0; see <see cref="EffectiveEarliest"/>.</summary>
    public static readonly DateTime EarliestDate = new(2003, 9, 29);
    /// <summary>Most days in one run, counting both end dates.</summary>
    public const int MaxSpanDays = 366;
    /// <summary>How far past today the end date may be.</summary>
    public const int MaxDaysAhead = 366;
    public const int MaxStores = 200;
    public const int MaxStoreId = 9_999_999;
    public const int MaxLength = 2000;
    public const int MaxKeyIdLength = 24;

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(30);
    /// <summary>The poster runs on another computer than the site, so their clocks can differ.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>1 general ledger, 2 accounts receivable, 3 both. A/R adjustments (16) are not offered from links yet.</summary>
    public static bool ScopeAllowed(int scope) => scope is 1 or 2 or 3;

    public static bool IsValidKeyId(string? kid)
    {
        if (string.IsNullOrEmpty(kid) || kid.Length > MaxKeyIdLength) return false;
        foreach (char c in kid) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')) return false;
        return true;
    }

    /// <summary>The earliest date a run may start: the database's own day 0 when it has a later one, never before <see cref="EarliestDate"/>.</summary>
    public static DateTime EffectiveEarliest(DateTime? databaseDayZero) =>
        databaseDayZero is DateTime z && z.Date > EarliestDate ? z.Date : EarliestDate;

    /// <summary>Checks dates, scope and stores. Returns null when the request is acceptable.</summary>
    public static PosterLinkError? Check(PostingRequest r, DateTimeOffset now)
    {
        var start = r.Start.Date; var end = r.End.Date;
        if (start < EarliestDate)
            return new(PosterLinkProblem.BadDates, $"Dates before {EarliestDate:yyyy-MM-dd} (day 0 of the DD database) are not accepted.");
        if (end < start)
            return new(PosterLinkProblem.BadDates, "The end date is before the start date.");
        if ((end - start).Days + 1 > MaxSpanDays)
            return new(PosterLinkProblem.BadDates, $"At most {MaxSpanDays} days can be posted at a time.");
        if (end > now.UtcDateTime.Date.AddDays(MaxDaysAhead))
            return new(PosterLinkProblem.BadDates, "The end date is too far in the future.");

        if (!ScopeAllowed(r.Scope))
            return new(PosterLinkProblem.BadScope, "Only general ledger, accounts receivable, or both can be opened from a link.");

        var ids = r.StoreIds;
        if (ids.Count == 0)
            return new(PosterLinkProblem.BadStores, "Pick at least one store.");
        if (ids.Count > MaxStores)
            return new(PosterLinkProblem.BadStores, $"A link can carry at most {MaxStores} stores; pick fewer.");
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] < 1 || ids[i] > MaxStoreId)
                return new(PosterLinkProblem.BadStores, "A store number in the link is out of range.");
            if (i > 0 && ids[i] <= ids[i - 1])
                return new(PosterLinkProblem.BadStores, "Store numbers in the link must be distinct and in ascending order.");
        }
        return null;
    }
}
