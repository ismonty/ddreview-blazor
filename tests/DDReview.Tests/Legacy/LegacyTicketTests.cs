using DDReview.Core.Legacy;

namespace DDReview.Tests.Legacy;

public class LegacyTicketTests
{
    // Shared with the old (VS 2008) side: the same vectors must verify there. Signatures were computed independently with Python's hmac.
    const string Secret = "test-secret-0123456789abcdef0123456789ab";
    const string Vector1 = "v1.4242.1790000000.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b";
    const string Vector2 = "v1.7.1790000060.ffeeddccbbaa99887766554433221100.0b45fdfa0d53b489d3d9b80905297816a4c6e5d89ab42c441195a693ca1fcf34";
    static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1790000000 - 60);

    [Fact]
    public void Mint_matches_published_vectors()
    {
        Assert.Equal(Vector1, LegacyTicket.Mint(Secret, 4242, T0, TimeSpan.FromSeconds(60), "00112233445566778899aabbccddeeff"));
        Assert.Equal(Vector2, LegacyTicket.Mint(Secret, 7, T0.AddSeconds(60), TimeSpan.FromSeconds(60), "ffeeddccbbaa99887766554433221100"));
    }

    [Fact]
    public void Verify_accepts_vector_before_expiry_and_returns_user_and_nonce()
    {
        Assert.True(LegacyTicket.TryVerify(Secret, Vector1, T0.AddSeconds(30), out int uid, out string nonce));
        Assert.Equal(4242, uid);
        Assert.Equal("00112233445566778899aabbccddeeff", nonce);
    }

    [Fact]
    public void Verify_allows_small_clock_skew_but_not_more()
    {
        var expiry = DateTimeOffset.FromUnixTimeSeconds(1790000000);
        Assert.True(LegacyTicket.TryVerify(Secret, Vector1, expiry.AddSeconds(5), out _, out _));
        Assert.False(LegacyTicket.TryVerify(Secret, Vector1, expiry.AddSeconds(6), out _, out _));
    }

    [Fact]
    public void Verify_rejects_wrong_secret()
        => Assert.False(LegacyTicket.TryVerify("another-secret-0123456789abcdef012345", Vector1, T0, out _, out _));

    [Theory]
    [InlineData("v1.4243.1790000000.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b")]   // other user
    [InlineData("v1.4242.1790009999.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b")]   // later expiry
    [InlineData("v1.4242.1790000000.00112233445566778899aabbccddeef0.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b")]   // other nonce
    [InlineData("v2.4242.1790000000.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b")]   // other version
    [InlineData("v1.4242.1790000000.00112233445566778899aabbccddeeff.95AF2ACC38F15A88BB1D336AC4B6CFFAC066A03B98904B4A4FEB7F2B9418362B")]   // upper-case hex is not the format
    [InlineData("v1.4242.1790000000.00112233445566778899aabbccddeeff")]
    [InlineData("v1.-5.1790000000.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b9418362b")]
    [InlineData("v1.4242.1790000000.00112233445566778899aabbccddeeff.95af2acc38f15a88bb1d336ac4b6cffac066a03b98904b4a4feb7f2b941836")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("....")]
    public void Verify_rejects_tampered_or_malformed(string? ticket)
        => Assert.False(LegacyTicket.TryVerify(Secret, ticket, T0, out int uid, out string nonce) || uid != 0 || nonce != "");

    [Fact]
    public void Verify_rejects_short_secret_and_oversize_ticket()
    {
        Assert.False(LegacyTicket.TryVerify("short", Vector1, T0, out _, out _));
        Assert.False(LegacyTicket.TryVerify(Secret, new string('a', 201), T0, out _, out _));
    }

    [Fact]
    public void Mint_rejects_bad_input()
    {
        Assert.Throws<ArgumentException>(() => LegacyTicket.Mint("short", 1, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LegacyTicket.Mint(Secret, 0, T0));
        Assert.Throws<ArgumentException>(() => LegacyTicket.Mint(Secret, 1, T0, null, "XYZ"));
    }

    [Fact]
    public void Random_nonce_differs_and_roundtrips()
    {
        var now = DateTimeOffset.UtcNow;
        string a = LegacyTicket.Mint(Secret, 99, now), b = LegacyTicket.Mint(Secret, 99, now);
        Assert.NotEqual(a, b);
        Assert.True(LegacyTicket.TryVerify(Secret, a, now, out int uid, out _));
        Assert.Equal(99, uid);
    }
}
