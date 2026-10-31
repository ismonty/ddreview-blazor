using DDReview.Core.Gl;
using DDReview.Core.Security;
using DDReview.Core.Support;
using DDReview.Core.Tenancy;

namespace DDReview.Tests;

public class DayNumberTests
{
    [Fact] public void Base_date_is_day_zero() => Assert.Equal(0, DayNumber.FromDate(new DateOnly(2003, 9, 29)));
    [Fact] public void Corpus_script_day_8428_is_2026_10_26() => Assert.Equal(new DateOnly(2026, 10, 26), DayNumber.ToDate(8428));
    [Fact] public void Round_trips() { var d = new DateOnly(2026, 10, 27); Assert.Equal(d, DayNumber.ToDate(DayNumber.FromDate(d))); }
}

public class PasswordTests
{
    [Fact] public void Hash_verifies_and_is_salted()
    {
        var a = PasswordHasher.Hash("S3cret!", 1000); var b = PasswordHasher.Hash("S3cret!", 1000);
        Assert.NotEqual(a, b);
        Assert.True(PasswordHasher.Verify("S3cret!", a));
        Assert.False(PasswordHasher.Verify("s3cret!", a));
    }
    [Fact] public void Garbage_hash_is_rejected() { Assert.False(PasswordHasher.Verify("x", "nonsense")); Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$abc$!!$!!")); }
    [Fact] public void Legacy_compare_is_case_insensitive_like_the_old_app()
    {
        Assert.True(PasswordHasher.VerifyLegacy("abc123", "ABC123"));
        Assert.False(PasswordHasher.VerifyLegacy("abc124", "ABC123"));
        Assert.False(PasswordHasher.VerifyLegacy("x", null));
    }
}

public class ThrottleTests
{
    [Fact] public void Locks_after_max_failures_then_expires()
    {
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var t = new LoginThrottle(3, TimeSpan.FromMinutes(15), () => now);
        for (var i = 0; i < 3; i++) t.RecordFailure("k");
        Assert.True(t.IsLockedOut("k"));
        now = now.AddMinutes(16);
        Assert.False(t.IsLockedOut("k"));
    }
    [Fact] public void Success_resets() { var t = new LoginThrottle(2); t.RecordFailure("k"); t.RecordFailure("k"); t.Reset("k"); Assert.False(t.IsLockedOut("k")); }
}

public class LoginServiceTests
{
    private sealed class FakeCentral(params CentralAccount[] accounts) : ICentralDirectory
    {
        public List<(int, string)> Stored = new(); public int Logged;
        public Task<IReadOnlyList<CentralAccount>> FindAccountsAsync(string u, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CentralAccount>>(accounts.Where(a => a.UserName.Equals(u, StringComparison.OrdinalIgnoreCase)).ToList());
        public Task<CentralAccount?> GetByIdAsync(int id, CancellationToken ct = default) => Task.FromResult(accounts.FirstOrDefault(a => a.WebUserId == id));
        public Task StoreHashAsync(int id, string h, CancellationToken ct = default) { Stored.Add((id, h)); return Task.CompletedTask; }
        public Task<int> LogLoginAsync(int id, string? ua, string? ip, string? host, CancellationToken ct = default) { Logged++; return Task.FromResult(77); }
        public Dictionary<string, int> Google = new();
        public Task<CentralAccount?> FindByGoogleAsync(string sub, CancellationToken ct = default) =>
            Task.FromResult(Google.TryGetValue(sub, out var id) ? accounts.FirstOrDefault(a => a.WebUserId == id) : null);
        public Task<bool> LinkGoogleAsync(int id, string sub, string? email, CancellationToken ct = default)
        { if (Google.TryGetValue(sub, out var other) && other != id) return Task.FromResult(false); Google[sub] = id; return Task.FromResult(true); }
        public Task<string?> GetGoogleEmailAsync(int id, CancellationToken ct = default) => Task.FromResult<string?>(Google.ContainsValue(id) ? "me@example.com" : null);
        public Task UnlinkGoogleAsync(int id, CancellationToken ct = default) { foreach (var k in Google.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) Google.Remove(k); return Task.CompletedTask; }
    }
    private static CentralAccount Acct(string pw, string? dsn = "Server=x;Database=y", string? redirect = null, string? hash = null) =>
        new(1, "Valentinis", pw, hash, dsn, "u", "p", 3, null, null, false, true, false, false, redirect);

    [Fact] public async Task Linked_google_account_signs_in_without_a_password()
    {
        var c = new FakeCentral(Acct("Pass1")); c.Google["g-123"] = 1;
        var r = await new LoginService(c, new LoginThrottle(), false).SignInWithGoogleAsync("g-123", "1.2.3.4", "ua", "host");
        Assert.Equal(LoginOutcome.Success, r.Outcome); Assert.Equal(77, r.WebLoginId); Assert.Equal(1, c.Logged);
    }

    [Fact] public async Task Unlinked_google_account_is_refused()
    {
        var c = new FakeCentral(Acct("Pass1"));
        var r = await new LoginService(c, new LoginThrottle(), false).SignInWithGoogleAsync("someone-else", "1.2.3.4", "ua", "host");
        Assert.Equal(LoginOutcome.InvalidCredentials, r.Outcome); Assert.Equal(0, c.Logged);
    }

    [Fact] public async Task Google_sign_in_still_honours_missing_client_database_and_redirects()
    {
        var c = new FakeCentral(Acct("p", dsn: null)); c.Google["g"] = 1;
        Assert.Equal(LoginOutcome.NoClientDatabase, (await new LoginService(c, new LoginThrottle(), false).SignInWithGoogleAsync("g", null, null, null)).Outcome);
        var c2 = new FakeCentral(Acct("p", redirect: "https://other.example/")); c2.Google["g"] = 1;
        Assert.Equal(LoginOutcome.Redirect, (await new LoginService(c2, new LoginThrottle(), false).SignInWithGoogleAsync("g", null, null, null)).Outcome);
    }

    [Fact] public async Task Legacy_password_signs_in_and_is_upgraded()
    {
        var c = new FakeCentral(Acct("Pass1")); var s = new LoginService(c, new LoginThrottle(), upgradeLegacyPasswords: true);
        var r = await s.SignInAsync("valentinis", "pass1", "1.2.3.4", "ua", "h");
        Assert.Equal(LoginOutcome.Success, r.Outcome); Assert.Equal(77, r.WebLoginId);
        Assert.Single(c.Stored); Assert.True(PasswordHasher.Verify("pass1", c.Stored[0].Item2));
    }
    [Fact] public async Task Hashed_account_ignores_legacy_column()
    {
        var c = new FakeCentral(Acct("OLD", hash: PasswordHasher.Hash("NewPass", 1000)));
        var s = new LoginService(c, new LoginThrottle(), true);
        Assert.Equal(LoginOutcome.InvalidCredentials, (await s.SignInAsync("valentinis", "OLD", null, null, null)).Outcome);
        Assert.Equal(LoginOutcome.Success, (await s.SignInAsync("valentinis", "NewPass", null, null, null)).Outcome);
    }
    [Fact] public async Task Wrong_password_locks_out_after_five()
    {
        var s = new LoginService(new FakeCentral(Acct("Pass1")), new LoginThrottle(), true);
        for (var i = 0; i < 5; i++) await s.SignInAsync("valentinis", "bad", "ip", null, null);
        Assert.Equal(LoginOutcome.LockedOut, (await s.SignInAsync("valentinis", "Pass1", "ip", null, null)).Outcome);
    }
    [Fact] public async Task Redirect_and_missing_dsn_are_distinct()
    {
        var s1 = new LoginService(new FakeCentral(Acct("p", redirect: "https://new.example/")), new LoginThrottle(), false);
        Assert.Equal(LoginOutcome.Redirect, (await s1.SignInAsync("valentinis", "p", null, null, null)).Outcome);
        var s2 = new LoginService(new FakeCentral(Acct("p", dsn: null)), new LoginThrottle(), false);
        Assert.Equal(LoginOutcome.NoClientDatabase, (await s2.SignInAsync("valentinis", "p", null, null, null)).Outcome);
    }
}

public class GlBalanceTests
{
    private static GlLine L(int batch, decimal d, decimal c, bool susp = false) =>
        new(batch, new DateTime(2026, 10, 1), "A", null, null, null, d, c, null, null, susp, null, null, null, 1, true, null, false, 1, "S", null, null, null, null);

    [Fact] public void Balanced_batches() =>
        Assert.True(GlBalance.Compute([L(1, 100, 0), L(1, 0, 60), L(1, 0, 40), L(2, 5, 0), L(2, 0, 5)]).Balanced);

    [Fact] public void Detects_out_of_balance_batch_even_if_grand_total_cancels()
    {
        var r = GlBalance.Compute([L(1, 100, 0), L(1, 0, 90), L(2, 0, 10), L(2, 10, 0), L(3, 0, 10)]);
        Assert.False(r.Balanced); Assert.Contains(r.OutOfBalance, b => b.BatchId == 1 && b.Difference == 10m);
    }

    /// <summary>Golden master: the real 593-row GL export from the restored Valentini's copy. Set DDREVIEW_GL_SAMPLE to its path.</summary>
    [Fact] public void Real_gl_sample_balances_when_available()
    {
        var path = Environment.GetEnvironmentVariable("DDREVIEW_GL_SAMPLE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return; // data stays out of the repo; run locally with the variable set
        var lines = ReadSsmsCsv(path);
        var report = GlBalance.Compute(lines);
        Assert.Equal(593, lines.Count);
        Assert.Equal(23, report.Batches.Count);
        Assert.Equal(134955.20m, report.TotalDebit);
        Assert.True(report.Balanced);
    }

    private static List<GlLine> ReadSsmsCsv(string path)
    {
        using var p = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, System.Text.Encoding.UTF8) { Delimiters = [","], HasFieldsEnclosedInQuotes = true };
        var h = p.ReadFields()!; var ix = h.Select((n, i) => (n.TrimStart('﻿'), i)).ToDictionary(x => x.Item1, x => x.i);
        var list = new List<GlLine>();
        while (!p.EndOfData)
        {
            var f = p.ReadFields()!;
            string? S(string c) => f[ix[c]] == "NULL" ? null : f[ix[c]];
            decimal M(string c) => S(c) is { } s ? decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : 0m;
            list.Add(new GlLine(int.Parse(f[ix["BatchID"]]), DateTime.Parse(f[ix["PostingDate"]], System.Globalization.CultureInfo.InvariantCulture),
                S("Account"), S("ARAccount"), null, null, M("Debit"), M("Credit"), S("DistReference"), null, S("Suspense") == "1", null, null, null, null, false, null, false, null, null, null, null, null, null));
        }
        return list;
    }
}

public class SalesLayoutTests
{
    private static System.Data.DataTable Table(params string[] cols)
    {
        var t = new System.Data.DataTable();
        foreach (var c in cols) t.Columns.Add(c, c.Contains("Name") || c == "MainType" || c.EndsWith("Label") ? typeof(string) : typeof(decimal));
        return t;
    }

    [Fact]
    public void ByMainType_ShowsDimensionDaysAndTotals_NoStoreColumns()
    {
        var t = Table("MainType", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "TotalSales", "Discounts", "NetSales", "Quantity", "TotalVoids");
        var cols = DDReview.Core.Sales.SalesLayout.Build(DDReview.Core.Sales.SalesDimensions.MainType, t);
        Assert.Equal(["Main Type", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun", "Sales Total", "Discounts", "Net", "Quantity"], cols.Select(c => c.Header));
    }

    [Fact]
    public void ByStoreOnly_ShowsVoidsAndRefundPercent_ByDayHidesWeekdays()
    {
        var t = Table("StoreNumber", "StoreName", "DayName", "D1", "TotalSales", "Discounts", "NetSales", "Quantity", "TotalVoids", "TotalRefunds");
        var store = DDReview.Core.Sales.SalesLayout.Build(DDReview.Core.Sales.SalesDimensions.Store, t).Select(c => c.Header).ToList();
        Assert.Contains("Void %", store); Assert.Contains("Refund %", store); Assert.Contains("Mon", store);
        var byDay = DDReview.Core.Sales.SalesLayout.Build(DDReview.Core.Sales.SalesDimensions.Store | DDReview.Core.Sales.SalesDimensions.Day, t).Select(c => c.Header).ToList();
        Assert.DoesNotContain("Mon", byDay); Assert.DoesNotContain("Void %", byDay); Assert.Contains("Day", byDay);
    }

    [Fact]
    public void TotalsRow_SumsAndRecomputesPercent()
    {
        var t = Table("StoreNumber", "StoreName", "TotalSales", "TotalVoids");
        t.Rows.Add(1m, "A", 100m, 1m); t.Rows.Add(2m, "B", 300m, 3m);
        var cols = DDReview.Core.Sales.SalesLayout.Build(DDReview.Core.Sales.SalesDimensions.Store, t);
        var sums = DDReview.Core.Sales.SalesLayout.Totals(t, cols).Single().Sums;
        Assert.Equal(400m, sums["TotalSales"]);
        Assert.Equal("1.00%", DDReview.Core.Sales.SalesLayout.FormatPercent(sums["TotalVoids"], sums["TotalSales"]));
    }
}

public class UpdaterTests
{
    [Fact] public void Go_lines_split_batches_and_comment_only_batches_are_dropped()
    {
        var b = DDReview.Core.Updater.SqlBatches.Split("-- note\r\ncreate table a(i int)\r\ngo\r\n  GO  \r\ninsert a values(1)\r\nGo\r\n-- only a comment\r\n");
        Assert.Equal(2, b.Count);
        Assert.StartsWith("-- note", b[0]);
        Assert.Contains("insert a", b[1]);
    }

    [Fact] public void Every_registered_script_is_embedded_and_has_a_batch()
    {
        foreach (var s in DDReview.Core.Updater.ClientScripts.All)
            Assert.NotEmpty(DDReview.Core.Updater.SqlBatches.Split(s.ReadSql()));
    }

    [Fact] public void Drill_pair_script_creates_the_type_and_is_listed_first()
    {
        var s = DDReview.Core.Updater.ClientScripts.All[0];
        Assert.Equal("002", s.Id);
        Assert.Contains("create type dbo.DrillPair", s.ReadSql(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Grant_procedure_script_drops_and_recreates_and_defaults_to_listing_only()
    {
        var s = DDReview.Core.Updater.ClientScripts.All.Single(x => x.Id == "004");
        var sql = s.ReadSql();
        Assert.Equal(2, DDReview.Core.Updater.SqlBatches.Split(sql).Count);   // drop-if-exists, then create
        Assert.Contains("@Execute  bit     = 0", sql);
        Assert.Contains("grant execute on type::dbo.DrillPair", sql);
    }

    [Fact] public async Task Only_an_administrator_may_apply()
    {
        var u = new DDReview.Core.Updater.ClientUpdater();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => u.ApplyAsync("Server=.", new UserAccess { Administrator = false }));
    }

    [Fact] public void Ddladmin_hint_quotes_the_user_name()
    {
        var r = new DDReview.Core.Updater.UpdateReport("db", "o'brien", false, []);
        Assert.Equal("exec sp_addrolemember N'db_ddladmin', N'o''brien';", r.DdlAdminHint);
    }
}
