using System.Globalization;
using DDReview.QuickBooks.Export;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;

namespace DDReview.Tests.QuickBooks;

public class ImportFilesTests
{
    static PostingPlan Plan() => PlanBuilder.Build(CsvPackageReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", "OctoberPackage.csv")));

    [Fact]
    public void Every_IIF_transaction_balances_to_zero()
    {
        var iif = ImportFiles.Iif(Plan());
        decimal sum = 0; int blocks = 0;
        foreach (var line in iif.Split("\r\n"))
        {
            var c = line.Split('\t');
            if (c[0] is "TRNS" or "SPL") sum += decimal.Parse(c[6], CultureInfo.InvariantCulture);
            if (c[0] == "ENDTRNS") { Assert.Equal(0m, sum); sum = 0; blocks++; }
        }
        Assert.Equal(25, blocks);
    }

    [Fact]
    public void IIF_has_the_lists_and_headers_QuickBooks_expects()
    {
        var iif = ImportFiles.Iif(Plan());
        Assert.StartsWith("!ACCNT\tNAME\tACCNTTYPE", iif);
        Assert.Contains("ACCNT\tA/R Posting\tOCASSET", iif);
        Assert.Contains("!TRNS\tTRNSTYPE", iif);
        Assert.Contains("TRNS\tGENERAL JOURNAL\t10/1/2026\tCash:NBC Checking Main", iif);
    }

    [Fact]
    public void CSV_debits_equal_credits_for_journal_entries()
    {
        var csv = ImportFiles.Csv(Plan()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Where(l => l.Contains(",Journal Entry,")).ToList();
        Assert.NotEmpty(csv);
        decimal d = 0, c = 0;
        foreach (var l in csv)
        {
            var f = System.Text.RegularExpressions.Regex.Split(l, ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
            if (f[6].Length > 0) d += decimal.Parse(f[6], CultureInfo.InvariantCulture);
            if (f[7].Length > 0) c += decimal.Parse(f[7], CultureInfo.InvariantCulture);
        }
        Assert.Equal(d, c);
    }

    [Fact]
    public void QBXML_file_lists_accounts_with_types_before_transactions()
    {
        var doc = ImportFiles.Qbxml(Plan());
        var names = doc.Root!.Element("QBXMLMsgsRq")!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(names.IndexOf("AccountAddRq") < names.IndexOf("JournalEntryAddRq"));
        Assert.All(doc.Descendants("AccountAdd"), a => Assert.NotNull(a.Element("AccountType")));
    }
}
