using System.Data;
using DDReview.Core.Labor;
using DDReview.Core.Sales;
using Xunit;

namespace DDReview.Tests;

public class LaborTests
{
    private static DataTable Table(params string[] cols)
    {
        var t = new DataTable();
        foreach (var c in cols)
            t.Columns.Add(c, c.EndsWith("Name") || c.StartsWith("StaffDepartment") || c == "PayrollIdentifier" || c == "StoreNumber" ? typeof(string)
                : c.EndsWith("Time") ? typeof(DateTime) : typeof(decimal));
        return t;
    }

    [Fact]
    public void Script_HasHeaderSets_AndParameterizesThem()
    {
        var req = new LaborRequest(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25), LaborDimensions.Store | LaborDimensions.PunchDetail, [3, 1]);
        var script = new LaborService().BuildScript(req);
        Assert.Contains("set @StoreListCSV='1,3'", script);
        Assert.Contains("set @EmployeeFlag=1", script);          // punch detail implies employee
        Assert.Contains("set @EmployeeDetailFlag=1", script);
        var (sql, pars) = SalesService.Parameterize(script);
        Assert.DoesNotContain("set @StoreListCSV='1,3'", sql);
        Assert.Contains(pars, p => p.ParameterName == "@p_StoreListCSV" && (string)p.Value == "1,3");
        Assert.Contains(pars, p => p.ParameterName == "@p_StartDayNumber" && (int)p.Value == DDReview.Core.Support.DayNumber.FromDate(new DateOnly(2026, 10, 19)));
        Assert.Equal(22, pars.Count);                              // every header assignment
    }

    [Fact]
    public void Filters_AreValidated()
    {
        var bad = new LaborRequest(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25), LaborDimensions.Store, [1],
            new Dictionary<string, string> { ["Employee"] = "1'; drop table x;--" });
        Assert.Throws<ArgumentException>(() => new LaborService().BuildScript(bad));
    }

    [Fact]
    public void Layout_HoursMode_ShowsDimensionsDaysTotals()
    {
        var t = Table("StoreNumber", "StoreName", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D1Pay", "S1", "TotalHours", "HourlyPay", "ST", "TotalOTHours", "TotalDTHours");
        var h = LaborLayout.Build(LaborDimensions.Store, LaborDisplay.Hours, t).Select(c => c.Header).ToList();
        Assert.Equal(["Store #", "Store Name", "Mon Hours", "Tue Hours", "Wed Hours", "Thu Hours", "Fri Hours", "Sat Hours", "Sun Hours", "Total Hours", "OT Hours", "DT Hours"], h);
    }

    [Fact]
    public void Layout_VerboseMode_AddsSalesAndLaborPercent_PunchDetailAddsTimes()
    {
        var t = Table("EmployeeName", "PayrollIdentifier", "StartTime", "EndTime", "D1", "D1Pay", "S1", "TotalHours", "HourlyPay", "ST");
        var h = LaborLayout.Build(LaborDimensions.PunchDetail, LaborDisplay.Verbose, t).Select(c => c.Header).ToList();
        Assert.Contains("Date", h); Assert.Contains("Start Time", h); Assert.Contains("End Time", h);
        Assert.Contains("Mon Labor %", h); Assert.Contains("Labor %", h); Assert.Contains("Total Sales", h);
        var dollars = LaborLayout.Build(LaborDimensions.Employee, LaborDisplay.Dollars, t).Select(c => c.Header).ToList();
        Assert.DoesNotContain("Mon Hours", dollars); Assert.Contains("Mon Labor $", dollars); Assert.DoesNotContain("Labor %", dollars);
    }
}
