using System.Xml.Linq;
using DDReview.QuickBooks.Model;
using DDReview.QuickBooks.Package;
using DDReview.QuickBooks.Planning;
using DDReview.QuickBooks.Qbxml;

namespace DDReview.Tests.QuickBooks;

/// <summary>The October package, run through the port, must give the plan DDAdminCS saved for the same package.</summary>
public class GoldenPlanTests
{
    static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "QuickBooks", "Data", name);

    static PostingPlan Plan() => PlanBuilder.Build(CsvPackageReader.ReadFile(Data("OctoberPackage.csv")));

    [Fact]
    public void Package_reads_every_row()
    {
        var rows = CsvPackageReader.ReadFile(Data("OctoberPackage.csv"));
        Assert.Equal(665, rows.Count);
        Assert.Equal(612, rows.Count(r => r.PackageNumber == 99));
    }

    [Fact]
    public void Plan_matches_the_one_the_Access_program_saved()
    {
        var expected = XElement.Parse(File.ReadAllText(Data("OctoberPlan.xml")).TrimStart('﻿'));
        var actual = LegacyPlanXml.Write(Plan());
        var e = expected.Elements().ToList();
        var a = actual.Elements().ToList();
        Assert.Equal(e.Count, a.Count);
        for (int i = 0; i < e.Count; i++)
            Assert.True(XNode.DeepEquals(e[i], a[i]), $"check {i}:\nexpected {e[i]}\n  actual {a[i]}");
    }

    [Fact]
    public void Plan_has_24_journal_entries_one_payment_and_no_invoices()
    {
        var plan = Plan();
        Assert.Equal(24, plan.Of<JournalEntry>().Count());
        Assert.Single(plan.Of<ReceivePayment>());
        var p = plan.Of<ReceivePayment>().Single();
        Assert.Equal("551011-8284", p.RefNumber);
        Assert.Equal(2055.37m, p.TotalAmount);
    }
}
