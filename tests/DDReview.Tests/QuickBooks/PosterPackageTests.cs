using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DDReview.QuickBooks.Launch;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Sql;

namespace DDReview.Tests.QuickBooks;

public class PosterPackageTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
    static (PosterLinkSigner Signer, IReadOnlyList<TrustedKey> Trusted) NewKey(string kid = "k1")
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signer = PosterLinkSigner.FromPrivateBase64(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), kid);
        return (signer, [TrustedKey.FromPublicBase64(kid, signer.PublicKeyBase64)]);
    }

    static List<PackageRow> Rows() =>
    [
        new PackageRow { PackageNumber = 2, Account = "0100", DefaultQuickBooksType = "Income" },
        new PackageRow { PackageNumber = 99, BatchID = 8422, PostingDate = new DateTime(2026, 10, 26), Account = "4000", Debit = 0m, Credit = 1234.56m, DistReference = "Sales", Sorter = 3, CheckNumber = 12L, DebitCreditFlag = 1 },
        new PackageRow { PackageNumber = 99, BatchID = 8422, PostingDate = new DateTime(2026, 10, 26), Account = "1000", Debit = 1234.56m, Credit = 0m, DistReference = "Cash", Sorter = 4, DetailInvoiceMessage = "Memo \"quoted\" é" },
    ];

    static PostingRequest Req(params int[] stores) => new(stores, new DateTime(2026, 10, 26), new DateTime(2026, 10, 31), 3);

    static byte[] Make(PosterLinkSigner s, PostingRequest? r = null, PostingOptions? o = null, IReadOnlyList<PackageRow>? rows = null, IReadOnlyList<string>? names = null)
    {
        Assert.True(PosterPackage.TryCreate(s, r ?? Req(1, 2), names ?? ["1  A", "2  B"], 7, o ?? new PostingOptions(), rows ?? Rows(), Now, out var file, out var err), err);
        return file;
    }

    [Fact] public void Round_trip_keeps_rows_options_and_who_made_it()
    {
        var (s, t) = NewKey();
        var options = new PostingOptions { DetailPostingActive = true, ItemSalesTaxCode = "TAX", SalesTaxItemName = "GST" };
        var file = Make(s, o: options);
        Assert.True(PosterPackage.TryOpen(file, t, Now.AddMinutes(5), out var c, out var e), e.Message);
        Assert.Equal([1, 2], c!.Launch.Request.StoreIds);
        Assert.Equal(7, c.Launch.WebUserId);
        Assert.Equal(["1  A", "2  B"], c.StoreNames);
        Assert.Equal(3, c.Launch.Request.Scope);
        Assert.True(c.Options.DetailPostingActive); Assert.Equal("TAX", c.Options.ItemSalesTaxCode); Assert.Equal("GST", c.Options.SalesTaxItemName);
        Assert.Equal(3, c.Rows.Count);
        var r = c.Rows[1];
        Assert.Equal(1234.56m, r.Credit); Assert.Equal(new DateTime(2026, 10, 26), r.PostingDate); Assert.Equal(8422, r.BatchID); Assert.Equal(12L, r.CheckNumber);
        Assert.Equal("Memo \"quoted\" é", c.Rows[2].DetailInvoiceMessage);
        Assert.Null(c.Rows[0].Debit);
    }

    [Fact] public void A_package_with_the_same_rows_plans_the_same_as_the_original_rows()
    {
        var (s, t) = NewKey();
        var original = CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv"));
        var file = Make(s, Req(1), rows: original, names: ["1  Store"]);
        Assert.True(PosterPackage.TryOpen(file, t, Now, out var c, out _));
        var a = PlanBuilder.Build(original); var b = PlanBuilder.Build(c!.Rows, c.Options);
        Assert.Equal(DDReview.QuickBooks.Qbxml.LegacyPlanXml.Write(a).ToString(), DDReview.QuickBooks.Qbxml.LegacyPlanXml.Write(b).ToString());
    }

    [Fact] public void The_site_refuses_what_the_poster_would_refuse()
    {
        var (s, _) = NewKey();
        Assert.False(PosterPackage.TryCreate(s, Req(1), [], 7, new(), [], Now, out _, out var e1)); Assert.Contains("nothing to post", e1);
        Assert.False(PosterPackage.TryCreate(s, new PostingRequest([1], new DateTime(2001, 1, 1), new DateTime(2001, 1, 5), 3), [], 7, new(), Rows(), Now, out _, out _));
        Assert.False(PosterPackage.TryCreate(s, new PostingRequest([], new DateTime(2026, 10, 1), new DateTime(2026, 10, 5), 3), [], 7, new(), Rows(), Now, out _, out _));
        Assert.False(PosterPackage.TryCreate(s, Req(1), [], 0, new(), Rows(), Now, out _, out _));
        Assert.False(PosterPackage.TryCreate(s, new PostingRequest([1], new DateTime(2026, 10, 1), new DateTime(2026, 10, 5), 16), [], 7, new(), Rows(), Now, out _, out _));
    }

    [Fact] public void Changing_any_byte_of_the_data_breaks_the_signature()
    {
        var (s, t) = NewKey();
        var raw = Unzip(Make(s));
        int at = Encoding.UTF8.GetString(raw).IndexOf("1234.56", StringComparison.Ordinal);
        raw[at] = (byte)'9';
        Assert.False(PosterPackage.TryOpen(Zip(raw), t, Now, out _, out var e));
        Assert.Equal(PosterLinkProblem.BadSignature, e.Problem);
    }

    [Fact] public void A_package_signed_by_a_key_the_poster_does_not_trust_is_refused()
    {
        var (s, _) = NewKey("k1"); var (_, other) = NewKey("k1");
        Assert.False(PosterPackage.TryOpen(Make(s), other, Now, out _, out var e)); Assert.Equal(PosterLinkProblem.BadSignature, e.Problem);
        var (_, differentId) = NewKey("zzz");
        Assert.False(PosterPackage.TryOpen(Make(s), differentId, Now, out _, out var e2)); Assert.Equal(PosterLinkProblem.UnknownKey, e2.Problem);
        Assert.False(PosterPackage.TryOpen(Make(s), [], Now, out _, out var e3)); Assert.Equal(PosterLinkProblem.UnknownKey, e3.Problem);
    }

    [Fact] public void Expiry_and_clock_checks()
    {
        var (s, t) = NewKey();
        var file = Make(s);
        Assert.True(PosterPackage.TryOpen(file, t, Now.AddHours(23), out _, out _));
        Assert.False(PosterPackage.TryOpen(file, t, Now.AddHours(25), out _, out var e)); Assert.Equal(PosterLinkProblem.Expired, e.Problem);
        Assert.False(PosterPackage.TryOpen(file, t, Now.AddDays(-5), out _, out var e2)); Assert.Equal(PosterLinkProblem.ClockOff, e2.Problem);
    }

    [Fact] public void A_signed_package_that_breaks_a_limit_is_still_refused()
    {
        var (s, t) = NewKey();
        // hand-signed payloads with a bad scope, a date before day 0 and unsorted stores
        foreach (var json in new[]
        {
            Payload(scope: 16), Payload(start: "2001-01-01", end: "2001-01-05"), Payload(stores: "[2,1]"), Payload(stores: "[]"), Payload(stores: "[0]"), Payload(uid: 0),
        })
            Assert.False(PosterPackage.TryOpen(Hand(s, json), t, Now, out _, out _), json);
        Assert.True(PosterPackage.TryOpen(Hand(s, Payload()), t, Now, out _, out var e), e.Message);   // the hand-made baseline is itself fine
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("{\"format\":\"ddpackage-v1\"}")]
    public void Junk_is_refused_without_a_crash(string text)
    {
        var (_, t) = NewKey();
        Assert.False(PosterPackage.TryOpen(Encoding.UTF8.GetBytes(text), t, Now, out _, out _));
        Assert.False(PosterPackage.TryOpen(Zip(Encoding.UTF8.GetBytes(text)), t, Now, out _, out _));
    }

    [Fact] public void A_file_on_disk_opens_and_a_missing_one_is_refused()
    {
        var (s, t) = NewKey();
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "a.ddpackage"); File.WriteAllBytes(path, Make(s));
            Assert.True(PosterPackage.TryOpenFile(path, t, Now, out _, out _));
            Assert.False(PosterPackage.TryOpenFile(Path.Combine(dir.FullName, "nope.ddpackage"), t, Now, out _, out _));
        }
        finally { dir.Delete(true); }
    }

    // ---- helpers: build a file by hand so rule violations can be signed ----
    static string Payload(int scope = 3, string start = "2026-10-26", string end = "2026-10-31", string stores = "[1]", int uid = 7) =>
        $"{{\"V\":1,\"Start\":\"{start}\",\"End\":\"{end}\",\"Scope\":{scope},\"Stores\":{stores},\"Uid\":{uid},\"Created\":{Now.ToUnixTimeSeconds()},\"Exp\":{Now.AddHours(24).ToUnixTimeSeconds()},\"Kid\":\"k1\",\"Options\":{{}},\"Rows\":[]}}";

    static byte[] Hand(PosterLinkSigner s, string payload)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var sig = System.Buffers.Text.Base64Url.EncodeToString(s.SignData(Encoding.UTF8.GetBytes("ddpackage-v1\n").Concat(body).ToArray()));
        var head = Encoding.UTF8.GetBytes($"{{\"format\":\"ddpackage-v1\",\"kid\":\"k1\",\"sig\":\"{sig}\"}}\n");
        return Zip(head.Concat(body).ToArray());
    }

    static byte[] Zip(byte[] raw) { using var ms = new MemoryStream(); using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(raw); return ms.ToArray(); }
    static byte[] Unzip(byte[] file) { using var gz = new GZipStream(new MemoryStream(file), CompressionMode.Decompress); using var ms = new MemoryStream(); gz.CopyTo(ms); return ms.ToArray(); }
}
