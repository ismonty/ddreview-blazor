using DDReview.QuickBooks;

namespace DDReview.Tests.QuickBooks;

public class LogScrubberTests
{
    [Fact] public void Masks_a_password_in_a_connection_string() =>
        Assert.Equal("Server=s;User Id=u;Password=********;Database=d", LogScrubber.Scrub("Server=s;User Id=u;Password=hunter22;Database=d"));

    [Fact] public void Masks_pwd_in_any_case_and_quoted_values() =>
        Assert.Equal("a PWD=******** b", LogScrubber.Scrub("a PWD=\"x;y\" b"));

    [Fact] public void Masks_the_literal_password_where_it_appears_elsewhere()
    {
        string cs = "Server=s;User Id=u;Password=Tr0ub4dor&3";
        string r = LogScrubber.Scrub("Login failed for 'u' with Tr0ub4dor&3 at s", cs);
        Assert.DoesNotContain("Tr0ub4dor", r);
    }

    [Fact] public void Leaves_ordinary_lines_alone() =>
        Assert.Equal("6 Transactions Will Be Added", LogScrubber.Scrub("6 Transactions Will Be Added", "Server=s;Password=abcd1234"));

    [Fact] public void Null_and_empty_are_safe()
    {
        Assert.Equal("", LogScrubber.Scrub(null));
        Assert.Equal("", LogScrubber.Scrub(""));
    }
}
