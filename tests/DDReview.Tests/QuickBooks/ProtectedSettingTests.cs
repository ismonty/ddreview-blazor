using DDReview.QuickBooks.Settings;

namespace DDReview.Tests.QuickBooks;

public class ProtectedSettingTests
{
    static byte[] Scramble(byte[] b) { var r = b.Select(x => (byte)(x ^ 0x5A)).ToArray(); Array.Reverse(r); return r; }   // stands in for DPAPI

    const string Cs = "Server=s;Database=d;User Id=u;Password=digitaldining!;";

    [Fact] public void Sealed_text_does_not_show_the_password_and_opens_again()
    {
        var sealedText = ProtectedSetting.Seal(Cs, Scramble);
        Assert.StartsWith(ProtectedSetting.Prefix, sealedText);
        Assert.DoesNotContain("digitaldining", sealedText);
        Assert.DoesNotContain("Server", sealedText);
        Assert.Equal(Cs, ProtectedSetting.Open(sealedText, Scramble));
    }

    [Fact] public void Empty_stays_empty() { Assert.Equal("", ProtectedSetting.Seal("", Scramble)); Assert.Equal("", ProtectedSetting.Open("", Scramble)); Assert.Equal("", ProtectedSetting.Open(null, Scramble)); }

    [Fact] public void An_older_plain_text_value_is_still_read() { Assert.Equal(Cs, ProtectedSetting.Open(Cs, Scramble)); Assert.False(ProtectedSetting.IsSealed(Cs)); }

    [Fact] public void Text_that_cannot_be_opened_gives_empty_not_a_crash()
    {
        Assert.Equal("", ProtectedSetting.Open("dpapi:not base64!!", Scramble));
        Assert.Equal("", ProtectedSetting.Open("dpapi:AAAA", _ => throw new System.Security.Cryptography.CryptographicException("wrong user")));
    }
}
