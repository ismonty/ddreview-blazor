using System.Data;
using System.Globalization;
using DDReview.Core.Sales;

namespace DDReview.Core.Labor;

public enum LaborDisplay { Hours, Dollars, Both, Verbose }

/// <summary>
/// The columns the old labor grid showed (LaborReviewPage: control columns, the Mon..Sun hours/pay/sales/labor % controls by
/// display mode, the punch-detail columns, and the total columns).
/// </summary>
public static class LaborLayout
{
    private static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public static List<SalesColumn> Build(LaborDimensions dims, LaborDisplay mode, DataTable t, bool showWeekdays = true)
    {
        bool Has(string c) => t.Columns.Contains(c);
        bool On(LaborDimensions d) => (dims & d) != 0;
        var cols = new List<SalesColumn>();
        void Text(string h, string c) { if (Has(c)) cols.Add(new SalesColumn(h, c, SalesCellKind.Text)); }
        void Num(string h, string c, string? den = null, SalesCellKind k = SalesCellKind.Number) { if (Has(c) && (den is null || Has(den))) cols.Add(new SalesColumn(h, c, k, den)); }

        if (On(LaborDimensions.StoreGroup)) Text("Store Group", "StoreGroupName");
        if (On(LaborDimensions.Store)) { Text("Store #", "StoreNumber"); Text("Store Name", "StoreName"); }
        if (On(LaborDimensions.LaborType)) Text("Labor Type", "StaffDepartmentType");
        if (On(LaborDimensions.LaborGroup)) Text("Labor Group", "StaffDepartmentGroup");
        if (On(LaborDimensions.LaborDepartment)) Text("Labor Dept.", "StaffDepartmentName");
        if (On(LaborDimensions.Employee | LaborDimensions.PunchDetail)) { Text("Employee Payroll ID", "PayrollIdentifier"); Text("Employee Name", "EmployeeName"); }
        if (On(LaborDimensions.OriginalPayrollID)) Text("Original Payroll ID", "PayrollID");
        if (On(LaborDimensions.DayHourSlot)) Text("Day Hour", "DayHourSlotName");
        if (On(LaborDimensions.DayTimeSlot)) Text("Quarter Hour", "DayTimeSlotName");

        if (On(LaborDimensions.PunchDetail))
        {
            if (Has("StartTime")) { cols.Add(new SalesColumn("Date", "StartTime", SalesCellKind.Date)); cols.Add(new SalesColumn("Start Time", "StartTime", SalesCellKind.Time)); }
            if (Has("EndTime")) cols.Add(new SalesColumn("End Time", "EndTime", SalesCellKind.Time));
        }

        bool hours = mode is LaborDisplay.Hours or LaborDisplay.Both or LaborDisplay.Verbose;
        bool dollars = mode is LaborDisplay.Dollars or LaborDisplay.Both or LaborDisplay.Verbose;
        bool verbose = mode == LaborDisplay.Verbose;

        if (showWeekdays)
            for (int i = 1; i <= 7; i++)
            {
                string d = Days[i - 1];
                if (hours) Num(d + " Hours", $"D{i}");
                if (dollars) Num(d + " Labor $", $"D{i}Pay");
                if (verbose) { Num(d + " Sales $", $"S{i}"); Num(d + " Labor %", $"D{i}Pay", $"S{i}", SalesCellKind.Percent); }
            }

        if (hours) Num("Total Hours", "TotalHours");
        if (dollars) Num("Total $", "HourlyPay");
        if (verbose) { Num("Total Sales", "ST"); Num("Labor %", "HourlyPay", "ST", SalesCellKind.Percent); }
        Num("OT Hours", "TotalOTHours");
        Num("DT Hours", "TotalDTHours");
        return cols;
    }

    public static string Format(DataRow row, SalesColumn c)
    {
        var v = row[c.Column];
        if (v is DBNull) return "";
        return c.Kind switch
        {
            SalesCellKind.Date => Convert.ToDateTime(v, CultureInfo.InvariantCulture).ToString("MM/dd", CultureInfo.InvariantCulture),
            SalesCellKind.Time => Convert.ToDateTime(v, CultureInfo.InvariantCulture).ToString("h:mm tt", CultureInfo.InvariantCulture),
            _ => SalesLayout.Format(row, c),
        };
    }
}
