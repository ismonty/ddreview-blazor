using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using DDReview.QuickBooks.Launch;
using DDReview.QuickBooks.Sql;

namespace DDReview.Tests.QuickBooks;

public class PosterLinkTests : IDisposable
{
    static readonly DateTimeOffset Now = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);
    static readonly long ExpOk = Now.AddMinutes(10).ToUnixTimeSeconds();

    readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    readonly List<IDisposable> _extra = [];
    public void Dispose() { _key.Dispose(); foreach (var d in _extra) d.Dispose(); }

    PosterLinkSigner Signer(string kid = "test1") => new(_key, kid);
    IReadOnlyList<TrustedKey> Trusted(string kid = "test1") => [TrustedKey.FromPublicBase64(kid, TrustedKey.ExportPublicBase64(_key))];

    static PostingRequest Req(int scope = 3, params int[] stores) =>
        new(stores.Length == 0 ? [4, 7, 12] : stores, new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), scope);

    string Make(PostingRequest? r = null, int uid = 12, DateTimeOffset? at = null, TimeSpan? life = null)
    {
        Assert.True(Signer().TryCreate(r ?? Req(), uid, at ?? Now, life ?? TimeSpan.FromMinutes(10), out var link, out var err), err);
        return link;
    }

    static string Body(string v = "1", string start = "2026-10-01", string end = "2026-10-31", string scope = "3", string stores = "4,7,12",
        long? exp = null, string kid = "test1", string uid = "12") =>
        $"v={v}&start={start}&end={end}&scope={scope}&stores={stores}&exp={exp ?? ExpOk}&kid={kid}&uid={uid}";

    /// <summary>A link signed by the trusted key over any body at all, so the rules (not the signature) are what is being tested.</summary>
    string HandSigned(string body, ECDsa? key = null)
    {
        var sig = (key ?? _key).SignData(Encoding.UTF8.GetBytes(PosterLink.SigningText(body)), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return "ddposter://run?" + body + "&sig=" + Base64Url.EncodeToString(sig);
    }

    bool Parse(string? raw, out VerifiedLaunch? l, out PosterLinkError? e, DateTimeOffset? at = null, IReadOnlyList<TrustedKey>? keys = null) =>
        PosterLink.TryParse(raw, keys ?? Trusted(), at ?? Now, out l, out e);

    PosterLinkProblem Refused(string? raw, DateTimeOffset? at = null, IReadOnlyList<TrustedKey>? keys = null)
    {
        Assert.False(Parse(raw, out var l, out var e, at, keys), "the link should have been refused");
        Assert.Null(l);
        return e!.Problem;
    }

    // ---- the good path ----

    [Fact]
    public void A_link_made_by_the_site_is_accepted_by_the_poster_with_everything_intact()
    {
        var link = Make(Req(3, 12, 4, 7, 4), uid: 55);   // unsorted and a duplicate on purpose
        Assert.True(Parse(link, out var l, out var e), e?.Message);
        Assert.Equal([4, 7, 12], l!.Request.StoreIds);
        Assert.Equal(new DateTime(2026, 10, 1), l.Request.Start);
        Assert.Equal(new DateTime(2026, 10, 31), l.Request.End);
        Assert.Equal(3, l.Request.Scope);
        Assert.Equal(55, l.WebUserId);
        Assert.Equal("test1", l.KeyId);
        Assert.Equal(Now.AddMinutes(10).ToUnixTimeSeconds(), l.Expires.ToUnixTimeSeconds());
    }

    [Fact]
    public void The_link_carries_parameters_only_in_one_fixed_order()
    {
        var link = Make();
        Assert.StartsWith("ddposter://run?", link);
        var names = link["ddposter://run?".Length..].Split('&').Select(p => p.Split('=')[0]).ToArray();
        Assert.Equal(["v", "start", "end", "scope", "stores", "exp", "kid", "uid", "sig"], names);
        Assert.True(link.Length < PosterLinkRules.MaxLength);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Scopes_1_2_and_3_are_accepted(int scope)
    {
        Assert.True(Parse(Make(Req(scope)), out var l, out _));
        Assert.Equal(scope, l!.Request.Scope);
    }

    [Fact]
    public void The_scheme_is_not_case_sensitive_and_a_slash_after_run_and_outer_spaces_are_tolerated()
    {
        var link = Make();
        Assert.True(Parse(link.Replace("ddposter://run", "DDPOSTER://RUN"), out _, out _));
        Assert.True(Parse(link.Replace("ddposter://run?", "ddposter://run/?"), out _, out _));
        Assert.True(Parse("  " + link + "\r\n", out _, out _));
        Assert.True(Parse(link + "/", out _, out _));                       // a trailing slash some launchers add
        Assert.False(Parse(link + "//", out _, out _));                     // but only that one
        Assert.False(Parse(link[..link.IndexOf("&sig=", StringComparison.Ordinal)] + "/", out _, out _));   // and never in place of a signature
    }

    [Fact]
    public void Two_hundred_stores_with_the_longest_ids_still_fit_in_a_link()
    {
        var ids = Enumerable.Range(PosterLinkRules.MaxStoreId - 199, 200).ToArray();
        var link = Make(Req(3, ids));
        Assert.True(link.Length <= PosterLinkRules.MaxLength);
        Assert.True(Parse(link, out var l, out var e), e?.Message);
        Assert.Equal(ids, l!.Request.StoreIds);
    }

    // ---- changing a signed link ----

    [Theory]
    [InlineData("stores=4,7,12", "stores=4,7,13")]
    [InlineData("stores=4,7,12", "stores=4,7,12,99")]
    [InlineData("start=2026-10-01", "start=2026-09-01")]
    [InlineData("end=2026-10-31", "end=2026-11-30")]
    [InlineData("scope=3", "scope=1")]
    [InlineData("uid=12", "uid=13")]
    public void Changing_any_signed_value_breaks_the_signature(string from, string to)
    {
        var link = Make();
        Assert.Contains(from, link);
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(link.Replace(from, to)));
    }

    [Fact]
    public void A_later_expiry_breaks_the_signature_so_a_link_cannot_be_kept_alive()
    {
        var link = Make();
        var forged = link.Replace("exp=" + ExpOk, "exp=" + (ExpOk + 86400));
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(forged));
    }

    [Fact]
    public void A_damaged_signature_is_refused()
    {
        var link = Make();
        int at = link.LastIndexOf("&sig=", StringComparison.Ordinal) + 5;
        char flip = link[at] == 'A' ? 'B' : 'A';
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(link[..at] + flip + link[(at + 1)..]));
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(link[..(link.Length - 2)]));   // cut short
        Assert.Equal(PosterLinkProblem.Malformed, Refused(link[..(at - 5)]));                  // no signature at all
    }

    [Fact]
    public void A_link_signed_by_someone_elses_key_is_refused_even_under_a_trusted_key_id()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(HandSigned(Body(), other)));
    }

    [Fact]
    public void An_unknown_key_id_and_an_empty_trust_list_are_both_refused_with_a_reason()
    {
        Assert.Equal(PosterLinkProblem.UnknownKey, Refused(Make().Replace("kid=test1", "kid=test2")));
        Assert.False(Parse(Make(), out _, out var e, keys: []));
        Assert.Equal(PosterLinkProblem.UnknownKey, e!.Problem);
        Assert.Contains(TrustedKeyFile.FileName, e.Message);
    }

    // ---- expiry and clocks ----

    [Fact]
    public void A_link_lasts_its_lifetime_plus_the_clock_allowance_and_not_a_second_more()
    {
        var link = Make();
        var expires = Now.AddMinutes(10);
        Assert.True(Parse(link, out _, out _, expires + PosterLinkRules.ClockSkew));
        Assert.Equal(PosterLinkProblem.Expired, Refused(link, expires + PosterLinkRules.ClockSkew + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_poster_clock_far_behind_the_site_is_reported_as_a_clock_problem_not_accepted()
    {
        var link = Make();
        Assert.True(Parse(link, out _, out _, Now.AddMinutes(-24)));
        Assert.Equal(PosterLinkProblem.ClockOff, Refused(link, Now.AddMinutes(-40)));
    }

    [Fact]
    public void Expired_is_only_said_about_genuine_links()
    {
        var forgedOld = Make().Replace("uid=12", "uid=13");
        Assert.Equal(PosterLinkProblem.BadSignature, Refused(forgedOld, Now.AddDays(3)));
    }

    // ---- the rules hold even for a validly signed link ----

    public static TheoryData<string, string, PosterLinkProblem> BrokenRules() => new()
    {
        { "day before day 0", Body(start: "2003-09-28", end: "2003-10-05"), PosterLinkProblem.BadDates },
        { "end before start", Body(start: "2026-10-31", end: "2026-10-01"), PosterLinkProblem.BadDates },
        { "367 days", Body(start: "2025-01-01", end: "2026-01-02"), PosterLinkProblem.BadDates },
        { "far future", Body(start: "2027-12-01", end: "2027-12-31"), PosterLinkProblem.BadDates },
        { "not a date", Body(start: "2026-13-01"), PosterLinkProblem.BadDates },
        { "date with a time", Body(start: "2026-10-01T00"), PosterLinkProblem.BadDates },
        { "short date", Body(start: "2026-1-1"), PosterLinkProblem.BadDates },
        { "scope 0", Body(scope: "0"), PosterLinkProblem.BadScope },
        { "scope 4 (labor)", Body(scope: "4"), PosterLinkProblem.BadScope },
        { "scope 16 (A/R adjustments)", Body(scope: "16"), PosterLinkProblem.BadScope },
        { "scope 7", Body(scope: "7"), PosterLinkProblem.BadScope },
        { "scope text", Body(scope: "gl"), PosterLinkProblem.BadScope },
        { "no stores", Body(stores: ""), PosterLinkProblem.BadStores },
        { "stores out of order", Body(stores: "7,4"), PosterLinkProblem.BadStores },
        { "duplicate store", Body(stores: "4,4"), PosterLinkProblem.BadStores },
        { "store 0", Body(stores: "0,4"), PosterLinkProblem.BadStores },
        { "eight-digit store", Body(stores: "10000000"), PosterLinkProblem.BadStores },
        { "store with a sign", Body(stores: "4,-7"), PosterLinkProblem.BadStores },
        { "201 stores", Body(stores: string.Join(',', Enumerable.Range(1, 201))), PosterLinkProblem.BadStores },
        { "future version", Body(v: "2"), PosterLinkProblem.UnsupportedVersion },
        { "odd version", Body(v: "x"), PosterLinkProblem.Malformed },
        { "user 0", Body(uid: "0"), PosterLinkProblem.Malformed },
        { "user with a sign", Body(uid: "-1"), PosterLinkProblem.Malformed },
        { "upper-case key id", Body(kid: "Test1"), PosterLinkProblem.Malformed },
        { "long key id", Body(kid: new string('a', 25)), PosterLinkProblem.Malformed },
        { "expiry is not a number", Body(exp: null).Replace($"exp={ExpOk}", "exp=soon"), PosterLinkProblem.Malformed },
        { "leading zero scope", Body(scope: "03"), PosterLinkProblem.Malformed },
        { "leading zero store", Body(stores: "04,7,12"), PosterLinkProblem.Malformed },
        { "extra parameter", Body() + "&extra=1", PosterLinkProblem.Malformed },
        { "swapped order", $"v=1&end=2026-10-31&start=2026-10-01&scope=3&stores=4,7,12&exp={ExpOk}&kid=test1&uid=12", PosterLinkProblem.Malformed },
        { "connection string smuggled in", Body() + "&cs=Server=x", PosterLinkProblem.Malformed },
    };

    [Theory]
    [MemberData(nameof(BrokenRules))]
    public void A_validly_signed_link_that_breaks_a_rule_is_still_refused(string why, string body, PosterLinkProblem expected)
    {
        Assert.Equal(expected, Refused(HandSigned(body)));
        _ = why;
    }

    [Fact]
    public void Day_zero_itself_is_accepted()
    {
        Assert.True(Parse(HandSigned(Body(start: "2003-09-29", end: "2003-10-05")), out var l, out var e), e?.Message);
        Assert.Equal(new DateTime(2003, 9, 29), l!.Request.Start);
    }

    [Fact]
    public void A_year_of_366_days_is_the_most_that_is_accepted()
    {
        Assert.True(Parse(HandSigned(Body(start: "2025-01-01", end: "2026-01-01")), out _, out _));
    }

    // ---- text that is not a link, or is a hostile one ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/run?v=1")]
    [InlineData("ddposter://other?v=1&sig=x")]
    [InlineData("C:\\Windows\\System32\\calc.exe")]
    [InlineData("--register")]
    public void Anything_that_is_not_one_of_our_links_is_refused_as_not_a_link(string? raw) =>
        Assert.Equal(PosterLinkProblem.NotALink, Refused(raw));

    [Theory]
    [InlineData("\"")]
    [InlineData(" ")]
    [InlineData("%22")]
    [InlineData("^")]
    [InlineData("|")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("`")]
    [InlineData(";")]
    [InlineData("\\")]
    [InlineData("\n")]
    [InlineData("\u00e9")]
    [InlineData("$(calc)")]
    public void Characters_outside_the_small_allowed_set_are_refused_before_anything_else(string bad)
    {
        var link = Make();
        int at = link.IndexOf("scope=", StringComparison.Ordinal);
        Assert.Equal(PosterLinkProblem.Malformed, Refused(link[..at] + bad + link[at..]));
        // Spaces and line breaks around the whole link are trimmed on purpose (the pipe adds a newline); anything else on the end is refused.
        if (!string.IsNullOrWhiteSpace(bad)) Assert.Equal(PosterLinkProblem.Malformed, Refused(link + bad));
    }

    [Theory]
    [InlineData("&calc.exe")]
    [InlineData("&extra=1")]
    [InlineData("&sig=AAAA")]
    [InlineData("?x=y")]
    [InlineData("/../..")]
    public void Allowed_characters_do_not_let_extra_text_ride_along(string extra)
    {
        var link = Make();
        int at = link.IndexOf("scope=", StringComparison.Ordinal);
        Assert.False(Parse(link + extra, out _, out _));
        Assert.False(Parse(link[..at] + extra + link[at..], out _, out _));
    }

    [Fact]
    public void A_link_over_the_length_limit_is_refused()
    {
        Assert.Equal(PosterLinkProblem.Malformed, Refused(Make() + new string('a', PosterLinkRules.MaxLength)));
    }

    // ---- what the site will not make ----

    [Fact]
    public void The_site_will_not_make_a_link_the_poster_would_refuse()
    {
        var s = Signer();
        PostingRequest R(DateTime a, DateTime b, int scope = 3, params int[] ids) => new(ids.Length == 0 ? [1] : ids, a, b, scope);
        var ok = new DateTime(2026, 10, 1);
        Assert.False(s.TryCreate(R(new DateTime(2003, 9, 28), ok), 1, Now, TimeSpan.FromMinutes(10), out var l1, out var e1));
        Assert.Equal("", l1); Assert.Contains("2003-09-29", e1);
        Assert.False(s.TryCreate(R(ok, ok, 16), 1, Now, TimeSpan.FromMinutes(10), out _, out _));
        Assert.False(s.TryCreate(R(ok, ok, 4), 1, Now, TimeSpan.FromMinutes(10), out _, out _));
        Assert.False(s.TryCreate(R(ok, ok.AddDays(-1)), 1, Now, TimeSpan.FromMinutes(10), out _, out _));
        Assert.False(s.TryCreate(new PostingRequest([], ok, ok, 3), 1, Now, TimeSpan.FromMinutes(10), out _, out var none));
        Assert.Contains("at least one store", none);
        Assert.False(s.TryCreate(new PostingRequest(Enumerable.Range(1, 201).ToList(), ok, ok, 3), 1, Now, TimeSpan.FromMinutes(10), out _, out var many));
        Assert.Contains("200", many);
        Assert.False(s.TryCreate(R(ok, ok), 0, Now, TimeSpan.FromMinutes(10), out _, out _));
        Assert.False(s.TryCreate(new PostingRequest([0], ok, ok, 3), 1, Now, TimeSpan.FromMinutes(10), out _, out _));
    }

    [Fact]
    public void The_lifetime_cannot_be_zero_or_longer_than_the_maximum()
    {
        var s = Signer();
        Assert.Throws<ArgumentOutOfRangeException>(() => s.TryCreate(Req(), 1, Now, TimeSpan.Zero, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.TryCreate(Req(), 1, Now, PosterLinkRules.MaxLifetime + TimeSpan.FromSeconds(1), out _, out _));
    }

    [Fact]
    public void A_signer_refuses_keys_that_are_not_P256_or_ids_that_are_not_valid()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<ArgumentException>(() => new PosterLinkSigner(p384, "test1"));
        Assert.Throws<ArgumentException>(() => new PosterLinkSigner(_key, "Test 1"));
        Assert.Throws<ArgumentException>(() => new PosterLinkSigner(_key, ""));
    }

    // ---- the service the site registers ----

    [Fact]
    public void With_no_settings_the_service_is_off_and_makes_no_links()
    {
        var s = new PosterLinkService(null, "");
        Assert.False(s.IsConfigured);
        Assert.False(s.TryCreate(Req(), 1, Now, out var link, out var err));
        Assert.Equal("", link);
        Assert.Contains("not set up", err);
    }

    [Fact]
    public void With_a_key_the_service_makes_links_the_poster_accepts()
    {
        var s = new PosterLinkService(Convert.ToBase64String(_key.ExportPkcs8PrivateKey()), "test1");
        Assert.True(s.IsConfigured);
        Assert.Equal("test1", s.KeyId);
        Assert.True(s.TryCreate(Req(), 7, Now, out var link, out var err), err);
        Assert.True(Parse(link, out var l, out _));
        Assert.Equal(7, l!.WebUserId);
    }

    [Fact]
    public void Half_a_setting_or_a_bad_key_stops_the_site_at_start_and_never_echoes_the_key()
    {
        var pkcs8 = Convert.ToBase64String(_key.ExportPkcs8PrivateKey());
        Assert.Throws<InvalidOperationException>(() => new PosterLinkService(pkcs8, ""));
        Assert.Throws<InvalidOperationException>(() => new PosterLinkService("", "prod1"));
        const string junk = "bm90IGEga2V5";
        var ex = Assert.Throws<InvalidOperationException>(() => new PosterLinkService(junk, "prod1"));
        Assert.DoesNotContain(junk, ex.Message);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<InvalidOperationException>(() => new PosterLinkService(Convert.ToBase64String(p384.ExportPkcs8PrivateKey()), "prod1"));
    }

    [Theory]
    [InlineData(999, 30)]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    public void The_configured_lifetime_is_kept_between_one_and_thirty_minutes(int configured, int actual)
    {
        var s = new PosterLinkService(Convert.ToBase64String(_key.ExportPkcs8PrivateKey()), "test1", configured);
        Assert.True(s.TryCreate(Req(), 1, Now, out var link, out _));
        Assert.True(Parse(link, out var l, out var e), e?.Message);
        Assert.Equal(Now.AddMinutes(actual).ToUnixTimeSeconds(), l!.Expires.ToUnixTimeSeconds());
    }

    // ---- the trusted-keys file ----

    string PublicB64() => TrustedKey.ExportPublicBase64(_key);

    [Fact]
    public void A_trusted_keys_file_lists_public_keys_by_id()
    {
        var keys = TrustedKeyFile.Parse($$"""{ "keys": [ { "kid": "prod1", "publicKey": "{{PublicB64()}}" }, { "kid": "staging1", "publicKey": "{{PublicB64()}}" } ] }""", out var problems);
        Assert.Empty(problems);
        Assert.Equal(["prod1", "staging1"], keys.Select(k => k.KeyId));
    }

    [Fact]
    public void Bad_entries_are_skipped_with_a_reason_and_good_ones_still_load()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var json = $$"""
            { "keys": [
                { "kid": "good1", "publicKey": "{{PublicB64()}}" },
                { "kid": "good1", "publicKey": "{{PublicB64()}}" },
                { "kid": "Bad Id", "publicKey": "{{PublicB64()}}" },
                { "kid": "p384", "publicKey": "{{TrustedKey.ExportPublicBase64(p384)}}" },
                { "kid": "junk", "publicKey": "AAAA" },
                { "kid": "nokey" },
                42
            ] }
            """;
        var keys = TrustedKeyFile.Parse(json, out var problems);
        Assert.Equal(["good1"], keys.Select(k => k.KeyId));
        Assert.Equal(6, problems.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "keys": 5 }""")]
    public void A_broken_file_gives_no_keys_and_a_reason_instead_of_an_exception(string json)
    {
        Assert.Empty(TrustedKeyFile.Parse(json, out var problems));
        Assert.Single(problems);
    }

    [Fact]
    public void A_missing_keys_file_means_no_keys_not_a_crash()
    {
        Assert.Empty(TrustedKeyFile.Load(Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid(), TrustedKeyFile.FileName), out var problems));
        Assert.Single(problems);
    }

    // ---- the poster's command line and the registered command ----

    [Fact]
    public void The_command_line_means_only_a_link_register_or_unregister()
    {
        var link = Make();
        Assert.Equal(new PosterCommand(PosterAction.Run, null), PosterCommandLine.Parse([]));
        Assert.Equal(new PosterCommand(PosterAction.Run, link), PosterCommandLine.Parse([link]));
        Assert.Equal(new PosterCommand(PosterAction.Run, link), PosterCommandLine.Parse(["/x", "-y", link, "ddposter://run?second"]));
        Assert.Equal(new PosterCommand(PosterAction.Run, null), PosterCommandLine.Parse(["C:\\evil.exe", "--something"]));
        Assert.Equal(PosterAction.Register, PosterCommandLine.Parse(["--REGISTER"]).Action);
        Assert.Equal(PosterAction.Unregister, PosterCommandLine.Parse([link, "--unregister"]).Action);
        Assert.Equal(new PosterCommand(PosterAction.Run, "DDPOSTER:x"), PosterCommandLine.Parse(["DDPOSTER:x"]));
    }

    [Fact]
    public void The_registered_command_quotes_the_path_and_the_link_and_refuses_odd_paths()
    {
        Assert.Equal("\"C:\\Program Files\\DD\\DDQuickBooksPoster.exe\" \"%1\"", ProtocolCommand.Build(@"C:\Program Files\DD\DDQuickBooksPoster.exe"));
        Assert.Equal("\"\\\\server\\share\\p.exe\" \"%1\"", ProtocolCommand.Build(@"\\server\share\p.exe"));
        Assert.Throws<ArgumentException>(() => ProtocolCommand.Build("DDQuickBooksPoster.exe"));
        Assert.Throws<ArgumentException>(() => ProtocolCommand.Build("C:\\a\" \"b.exe"));
        Assert.Throws<ArgumentException>(() => ProtocolCommand.Build(""));
    }

    // ---- a pinned sample, so the format cannot drift without a test saying so ----
    // Made once with a throwaway key whose private half was thrown away; only the public key and the signed link are kept. Not used anywhere else.

    const string PinnedPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqeUYiHv0I62m41HcC+WnK676hsaeo8NtJEHU/31hs2aiIP7wJ9KxYiD7oyfXQ+LB5wBXDvDnRGT6MNxYllbV9g==";
    const string PinnedLink = "ddposter://run?v=1&start=2026-10-01&end=2026-10-31&scope=3&stores=4,7,12&exp=1793632200&kid=vector1&uid=12&sig=shagl8Ba2IOSOFXcYJwP_MYKtH97aCdYkaq7-c1EgCrzvR6ym962V53jWvXpLM76DV9B865kQkxYZTCt2Ss5GA";

    [Fact]
    public void A_sample_link_signed_once_and_kept_still_verifies()
    {
        var keys = new[] { TrustedKey.FromPublicBase64("vector1", PinnedPublicKey) };
        Assert.True(PosterLink.TryParse(PinnedLink, keys, Now, out var l, out var e), e?.Message);
        Assert.Equal([4, 7, 12], l!.Request.StoreIds);
        Assert.Equal(new DateTime(2026, 10, 1), l.Request.Start);
        Assert.Equal(new DateTime(2026, 10, 31), l.Request.End);
        Assert.Equal(3, l.Request.Scope);
        Assert.Equal(12, l.WebUserId);
        Assert.Equal("vector1", l.KeyId);
        Assert.False(PosterLink.TryParse(PinnedLink.Replace("stores=4,7,12", "stores=4,7,13"), keys, Now, out _, out var bad));
        Assert.Equal(PosterLinkProblem.BadSignature, bad!.Problem);
        Assert.False(PosterLink.TryParse(PinnedLink, keys, Now.AddHours(1), out _, out var late));
        Assert.Equal(PosterLinkProblem.Expired, late!.Problem);
    }

    // ---- day 0 ----

    [Fact]
    public void A_database_with_a_later_day_zero_raises_the_floor_and_never_lowers_it()
    {
        Assert.Equal(new DateTime(2003, 9, 29), PosterLinkRules.EffectiveEarliest(null));
        Assert.Equal(new DateTime(2003, 9, 29), PosterLinkRules.EffectiveEarliest(new DateTime(2003, 9, 29)));
        Assert.Equal(new DateTime(2003, 9, 29), PosterLinkRules.EffectiveEarliest(new DateTime(1999, 1, 3)));
        Assert.Equal(new DateTime(2005, 1, 2), PosterLinkRules.EffectiveEarliest(new DateTime(2005, 1, 2, 13, 30, 0)));
    }
}
