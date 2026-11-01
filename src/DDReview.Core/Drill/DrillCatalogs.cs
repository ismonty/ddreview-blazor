namespace DDReview.Core.Drill;

/// <summary>The control-column tables of the three reviews (SalesCostsReviewPage, ReceiptsReviewPage and LaborReviewPage).</summary>
public static class DrillCatalogs
{
    const string Fn = "fn_ParsePairSetStringAdd";

    public static readonly IReadOnlyList<DrillColumn> Sales =
    [
        new("StoreGroup", ["StoreGroup"], "StoreGroupID", "Store"),
        new("Store", ["StoreNumber", "StoreName"], "StoreID", "MainType"),
        new("ProfitCenter", ["ProfitCenter"], "ProfitCenterID", "MainType", Fn + "ProfitCenter"),
        new("Week", ["WeekName"], "WeekNumber", "Week", SortColumn: "WeekNumber"),
        new("WeekDay", ["WeekDayName"], "WeekDayNumber", "Day", SortColumn: "WeekDayNumber"),
        new("Day", ["DayName"], "DayNumber", "Day", SortColumn: "DayNumber"),
        new("StoreStaff", ["StoreStaff"], "StoreStaffID", "CheckNumber", Fn + "StoreStaff"),
        new("CheckNumber", ["CheckNumber"], "CheckNumber", "SaleType", Fn + "CheckNumber"),
        new("MainType", ["MainType"], "MainTypeID", "SaleType"),
        new("MainGroup", ["MainGroup"], "MainGroupID", "SaleType", Fn + "MainGroup"),
        new("SaleType", ["SaleType"], "SaleTypeID", "MenuItem", Fn + "SaleType"),
        new("MenuItem", ["MenuItem"], "MenuItemID", "MenuItemPLU", Fn + "MenuItem"),
        new("MenuItemPLU", ["MenuItemPLU"], "MenuItemID", "MenuItemPLU", Fn + "MenuItemPLU"),
        new("Discount", ["Discount"], "DiscountID", "Discount", Fn + "Discount", SortColumn: "Discounts"),
        new("DayPart", ["DayPart"], "DayPartID", "DayHour", SortColumn: "DayPartID"),
        new("DayHour", ["DayHourName"], "DayHourNumber", "DayHour", SortColumn: "DayHourNumber"),
    ];

    /// <summary>The fixed receipt classes and their ids, as the report's own SQL defines them (@tblReceiptClasses); "Other" is 3.</summary>
    static readonly IReadOnlyDictionary<string, string> ReceiptClassIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Cash"] = "1", ["Credit Card"] = "2", ["Other"] = "3", ["GC Redeem"] = "4", ["Bad Payment"] = "1000",
    };

    public static readonly IReadOnlyList<DrillColumn> Receipts =
    [
        new("StoreGroup", ["StoreGroupName"], "StoreGroupID", "Store"),
        new("Store", ["StoreNumber", "StoreName"], "StoreID", "ProfitCenter"),
        new("ProfitCenter", ["ProfitCenter"], "ProfitCenterID", "Cashier"),
        new("ReceiptClass", ["ReceiptClass"], "ReceiptClassID", "Cashier", IdByName: ReceiptClassIds),
        new("ReceiptType", ["ReceiptType"], "ReceiptTypeID", "Cashier"),
        new("CreditCardBatchNumber", ["CreditCardBatchNumber"], "CreditCardBatchNumber", "ReceiptType"),
        new("CashierStaffDepartment", ["CashierStaffDepartmentName"], "CashierStaffDepartmentName", "Cashier", Fn + "StaffDepartmentName"),
        new("Cashier", ["CashierName"], "CashierStaffID", "CheckNumber", Fn + "StoreStaff"),
        new("CheckNumber", ["CheckNumber"], "CheckNumberID", "Server", Fn + "CheckNumber"),
        new("Server", ["ServerName"], "ServerStaffID", "Server", Fn + "StoreStaff"),
        new("DayName", ["DayName"], "DayNameID", "Cashier"),
        new("WeekName", ["WeekName"], "WeekNameID", "Cashier"),
        new("CashierSessionNumber", ["CashierSessionNumber"], "CashierSessionNumber", "CheckNumber"),
        new("DayHourNumber", ["DayHourName"], "DayHourNumber", "CheckNumber"),
    ];

    public static readonly IReadOnlyList<DrillColumn> Labor =
    [
        new("StoreGroup", ["StoreGroupName"], "StoreGroupID", "Store"),
        new("Store", ["StoreNumber", "StoreName"], "StoreID", "LaborType"),
        new("LaborType", ["StaffDepartmentType"], "StaffDepartmentTypeID", "LaborGroup"),
        new("LaborGroup", ["StaffDepartmentGroup"], "StaffDepartmentGroupID", "LaborDepartment", Fn + "StaffDepartmentGroup"),
        new("LaborDepartment", ["StaffDepartmentName"], "StaffDepartmentID", "Employee", Fn + "StaffDepartmentName"),
        new("Employee", ["PayrollIdentifier", "EmployeeName"], "PayrollID", "PunchDetail"),   // clicking an employee opens their punches
        new("OriginalPayrollID", ["PayrollID"], "PayrollID", "OriginalPayrollID", FilterName: "PayrollID"),
        new("DayHourSlot", ["DayHourSlotName"], "DayHourSlot", "DayTimeSlot", SortColumn: "DayHourSlot"),
        new("DayTimeSlot", ["DayTimeSlotName"], "DayTimeSlot", "Employee", SortColumn: "DayTimeSlot"),
    ];
}
