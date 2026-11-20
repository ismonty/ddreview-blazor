using DDReview.Core.Receipts;
using DDReview.Core.Sales;
using System.Text.RegularExpressions;
using Xunit;

namespace DDReview.Tests;

public class ReportSqlTuningTests
{
    [Fact]
    public void Table_variables_become_temp_tables_with_database_collation_and_every_use_is_renamed()
    {
        var sql = "declare @t table (Id int primary key, Name varchar(50), Note nvarchar(max), C char(3))\ninsert into @t select 1, 'a', 'b', 'c'\nselect * from @t t inner join @tOther o on o.Id=t.Id";
        var r = ReportSqlTuning.TempTables(sql);
        Assert.Contains("if object_id('tempdb..#t') is not null drop table #t", r);
        Assert.Contains("create table #t (Id int primary key, Name varchar(50) collate database_default, Note nvarchar(max) collate database_default, C char(3) collate database_default)", r);
        Assert.Contains("insert into #t select", r);
        Assert.Contains("from #t t inner join @tOther o", r);   // a different variable that merely starts with the same letters is left alone
    }

    [Fact]
    public void A_table_variable_whose_name_is_already_a_temp_table_is_left_alone()
    {
        var sql = "declare @x table (A int)\nselect * from #x";
        Assert.Equal(sql, ReportSqlTuning.TempTables(sql));
    }

    [Fact]
    public void Day_number_function_becomes_dateadd_off_one_base_date_read_once()
    {
        var sql = "select dbo.fn_CalcDayNumberDate(cs.DayNumber) as D, dbo.fn_CalcDayNumberDate(floor(x.DayNumber/7)*7) as W";
        var r = ReportSqlTuning.DayNumberDates(sql);
        Assert.StartsWith("declare @DDR_DayZero datetime = dbo.fn_CalcWeekNumberBaseDate();", r);
        Assert.Contains("dateadd(d, (cs.DayNumber), @DDR_DayZero) as D", r);
        Assert.Contains("dateadd(d, (floor(x.DayNumber/7)*7), @DDR_DayZero) as W", r);
        Assert.DoesNotContain("fn_CalcDayNumberDate", r);
        Assert.Equal("select 1", ReportSqlTuning.DayNumberDates("select 1"));
    }

    [Fact]
    public void Nested_day_number_calls_and_function_calls_inside_the_argument_survive()
    {
        var r = ReportSqlTuning.DayNumberDates("select dbo.fn_CalcDayNumberDate(isnull(dbo.fn_CalcDayNumber(@d), 0))");
        Assert.Contains("dateadd(d, (isnull(dbo.fn_CalcDayNumber(@d), 0)), @DDR_DayZero)", r);
    }

    [Fact]
    public void Csv_splitter_becomes_an_inline_split_and_keeps_the_alias()
    {
        var sql = "inner join\n\tfn_CreateKeyTableValueFromCSVString(@StoreListCSV) csv\non csv.KeyValue=tblStores.StoreID\nwhere (select count(*) from dbo.fn_CreateKeyTableValueFromCSVString(@StoreListCSV))<>1";
        var r = ReportSqlTuning.CsvSplits(sql);
        Assert.DoesNotContain("fn_CreateKeyTableValueFromCSVString", r);
        Assert.Contains("as KeyValue", r);
        Assert.Contains("nodes('/i')", r);
        Assert.Contains(") csv\non csv.KeyValue", r);               // the existing alias is kept
        Assert.Contains(") as DDR_split2)<>1", r);                  // a call with no alias gets one
        Assert.Contains("nullif(x.i.value('.', 'varchar(20)'), '')", r);   // an empty list gives no rows (convert('') would give 0)
    }

    [Fact]
    public void A_comment_with_an_apostrophe_or_parenthesis_does_not_confuse_the_declaration_scan()
    {
        var sql = "declare @t table (A int, -- the store's (main) id\n B varchar(5))\nselect * from @t";
        var r = ReportSqlTuning.TempTables(sql);
        Assert.Contains("create table #t (A int, -- the store's (main) id\n B varchar(5) collate database_default)", r);
    }

    [Fact]
    public void Switching_it_off_leaves_the_script_as_generated()
    {
        var was = ReportSqlTuning.Enabled;
        try { ReportSqlTuning.Enabled = false; var s = "declare @t table (A int)\nselect dbo.fn_CalcDayNumberDate(1)"; Assert.Equal(s, ReportSqlTuning.Apply(s)); }
        finally { ReportSqlTuning.Enabled = was; }
    }

    static void AssertTuned(string raw)
    {
        var (sql, _) = SalesService.Parameterize(raw);
        var t = ReportSqlTuning.Apply(sql);
        Assert.DoesNotContain("fn_CreateKeyTableValueFromCSVString", t);
        Assert.DoesNotContain("fn_CalcDayNumberDate", t);
        Assert.False(Regex.IsMatch(t, @"declare\s+@\w+\s+table", RegexOptions.IgnoreCase), "a table variable is still declared");
        // every temp table that is used is created, and the parentheses still balance exactly as they did
        foreach (var n in Regex.Matches(t, @"#(\w+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
            Assert.Contains("create table #" + n, t);
        Assert.Equal(sql.Count(c => c == '(') - sql.Count(c => c == ')'), t.Count(c => c == '(') - t.Count(c => c == ')'));
    }

    [Theory]
    [InlineData(SalesDimensions.Store)]
    [InlineData(SalesDimensions.MenuItem)]
    [InlineData(SalesDimensions.Day | SalesDimensions.DayPart | SalesDimensions.Store)]
    [InlineData(SalesDimensions.Week | SalesDimensions.WeekDay | SalesDimensions.ProfitCenter)]
    public void Generated_sales_scripts_are_fully_rewritten(SalesDimensions d)
    {
        var req = new SalesRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), d, [1, 2, 3]);
        AssertTuned(new SalesService().BuildScript("", req, new SalesScriptCharacterizationTests_Lookups()));
    }

    [Theory]
    [InlineData(ReceiptsDimensions.Store)]
    [InlineData(ReceiptsDimensions.StoreGroup | ReceiptsDimensions.ReceiptClass)]
    [InlineData(ReceiptsDimensions.Store | ReceiptsDimensions.ReceiptType | ReceiptsDimensions.DayName)]
    public void Generated_receipts_scripts_are_fully_rewritten(ReceiptsDimensions d)
    {
        var req = new ReceiptsRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), d, [1, 2, 3]);
        AssertTuned(new ReceiptsService().BuildScript(req, new ReceiptsDbFacts(true, 0)));
    }
}
