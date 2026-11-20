-- Run on each CLIENT database (or let Database Updater do it). Safe to run again (it ALTERs or CREATEs). Nothing existing is changed.
-- Adds two functions for the 2025 "no tax on tips" payroll (compulsory service charge kept apart from voluntary tips):
--   dbo.fn_TipsByEmployee(@StoreIDList, @StartingDate, @EndingDate)
--       One row per store, employee and department group with the sales, the tip outs, and the three pay lines: CTP (cash tips), CCT (credit card tips,
--       including the voluntary tip left on top of a service charge) and SVC (compulsory service charge after the card fee and the tip out).
--   dbo.fn_CustomReport_TipsExportNew(@StoreIDList, @StartingDate, @EndingDate)
--       The Paycor import lines (same columns as fn_CustomReport_TipsExport): one CTP, CCT and SVC line per employee and payroll department.
-- Unlike the older tips export these read the receipts directly (no work table to crunch first, so no stale results), honor the store list (the old one
-- is fixed to store 13), and every join between tables that have a StoreID includes the StoreID.
-- Rules (see docs/PAYROLL-PORT-NOTES.md):
--   * A check (BatchID, CheckNumber, PersonNumber) is a service charge check when its receipts carry a service charge (AmountCharge<>0), otherwise a credit card
--     check when it carries a tip, otherwise cash. Same classification as cs_spConsolidatedTipOut3Crunch2.
--   * Card fee: 1.75 percent per receipt (half up to the cent) of the service charge on receipts whose type has ReceiptClass 2 (credit card / EMV). Gift card, comp,
--     cash, check, house charge and inventory receipts carry no fee, so a check part paid by gift card carries no fee on that part. The fee on tips is the one the
--     receipt already holds in AmountTipCreditCardFee (negative). The 1.75 is the default; a store's own rate is in dbo.tblTipPayrollSettings, and which receipt types carry the fee can be overridden in dbo.tblTipPayrollReceiptTypeFee.
--   * Tip out: the department group's percentage (the second rate after its effective day) of the day's sales, half up to the cent per employee and day, as
--     Crunch2 does; the part on service charge sales comes out of SVC, the part on cash and credit card sales comes out of CCT.
--   * Manual moves (tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments) are applied as the consolidated screen applies them.
--   * Departments listed in dbo.tblTipPayrollExcludedDepartments are left out. (tblStaffDepartments.ExcludeFromPayrollCalculations is not used: the old consolidated objects ignore it too.)
--   * Employees are kept to the payroll ID limits in tblGlobal. Needs no features above compatibility level 110 (no STRING_SPLIT).
-- Untested against a live database when written: compare a known pay period with the consolidated screen and the spreadsheets before relying on it.
set nocount on;
GO
-- Per-client settings (all optional: with no rows the functions behave as if the defaults were entered):
--   dbo.tblTipPayrollSettings               one row per store: CardFeePercent (default 1.75)
--   dbo.tblTipPayrollExcludedDepartments    departments left out of fn_TipsByEmployee altogether
--   dbo.tblTipPayrollReceiptTypeFee         per receipt type: does a service charge paid that way carry the card fee? No row = yes for ReceiptClass 2 (credit card / EMV), no otherwise
IF OBJECT_ID(N'dbo.tblTipPayrollSettings', N'U') IS NULL
    CREATE TABLE dbo.tblTipPayrollSettings (
        StoreID int NOT NULL,
        CardFeePercent decimal(9, 4) NOT NULL CONSTRAINT DF_tblTipPayrollSettings_Fee DEFAULT (1.75),
        CONSTRAINT PK_tblTipPayrollSettings PRIMARY KEY (StoreID),
        CONSTRAINT CK_tblTipPayrollSettings_Fee CHECK (CardFeePercent >= 0 AND CardFeePercent <= 100));
GO
IF OBJECT_ID(N'dbo.tblTipPayrollExcludedDepartments', N'U') IS NULL
    CREATE TABLE dbo.tblTipPayrollExcludedDepartments (
        StoreID int NOT NULL,
        StaffDepartmentID int NOT NULL,
        CONSTRAINT PK_tblTipPayrollExcludedDepartments PRIMARY KEY (StoreID, StaffDepartmentID));
GO
IF OBJECT_ID(N'dbo.tblTipPayrollReceiptTypeFee', N'U') IS NULL
    CREATE TABLE dbo.tblTipPayrollReceiptTypeFee (
        StoreID int NOT NULL,
        ReceiptTypeID int NOT NULL,
        ChargesFee bit NOT NULL,
        CONSTRAINT PK_tblTipPayrollReceiptTypeFee PRIMARY KEY (StoreID, ReceiptTypeID));
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF OBJECT_ID(N'dbo.fn_TipsByEmployee', N'IF') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_TipsByEmployee() RETURNS TABLE AS RETURN SELECT 1 AS Placeholder');
GO
ALTER FUNCTION dbo.fn_TipsByEmployee (@StoreIDList varchar(max), @StartingDate datetime, @EndingDate datetime)
RETURNS TABLE
AS
RETURN
(
    /* select * from dbo.fn_TipsByEmployee('13', '02/09/2026', '02/22/2026') order by StaffDepartmentGroup, EmployeeName */
    WITH StoreList AS (
        -- an empty list means every active store; anything that is not a number is ignored
        SELECT sto.StoreID
        FROM dbo.tblStores sto
        WHERE (LEN(ISNULL(@StoreIDList, '')) = 0 AND sto.Active <> 0)
           OR sto.StoreID IN (
                SELECT TRY_CONVERT(int, x.i.value('.', 'varchar(20)'))
                FROM (SELECT CAST('<i>' + REPLACE(REPLACE(REPLACE(ISNULL(@StoreIDList, ''), ' ', ''), ';', ','), ',', '</i><i>') + '</i>' AS xml) AS doc) s
                CROSS APPLY s.doc.nodes('/i') x(i))
    ),
    Rates AS (
        SELECT CAST(1000000000000000 AS bigint) AS BigNumber,
               (SELECT TOP 1 PayrollIDLowerLimit FROM dbo.tblGlobal) AS IDLow, (SELECT TOP 1 PayrollIDUpperLimit FROM dbo.tblGlobal) AS IDHigh
    ),
    Batches AS (
        SELECT b.StoreID, b.BatchID, b.DayNumber
        FROM dbo.tblCashAndSalesBatches b
        INNER JOIN StoreList sl ON sl.StoreID = b.StoreID
        WHERE b.PostingDate BETWEEN @StartingDate AND @EndingDate
    ),
    CheckMode AS (
        SELECT d.StoreID, d.BatchID, d.CheckNumber, d.PersonNumber,
               CASE WHEN SUM(ISNULL(d.AmountCharge, 0)) <> 0 THEN 3
                    WHEN SUM(ISNULL(d.AmountTip, 0) + ISNULL(d.AmountTipCreditCardFee, 0)) <> 0 THEN 2
                    ELSE 1 END AS Mode
        FROM dbo.tblCashAndSalesReceiptsDetail d
        INNER JOIN Batches b ON b.StoreID = d.StoreID AND b.BatchID = d.BatchID
        WHERE d.CheckNumber <> 0
        GROUP BY d.StoreID, d.BatchID, d.CheckNumber, d.PersonNumber
    ),
    Receipt AS (
        SELECT b.StoreID, b.BatchID, b.DayNumber, te.StoreStaffID, sd.StaffDepartmentGroupID, cm.Mode,
               ISNULL(d.AmountFood, 0) + ISNULL(d.AmountBeverage, 0) AS Sales,
               ISNULL(d.AmountCharge, 0) AS Charge,
               CASE WHEN d.AmountCharge > 0 AND ISNULL(CAST(rf.ChargesFee AS int), CASE WHEN rt.ReceiptClass = 2 THEN 1 ELSE 0 END) = 1
                    THEN FLOOR(ISNULL(ts.CardFeePercent, 1.75) * d.AmountCharge + 0.5) / 100.0 ELSE 0 END AS ChargeFee,
               CASE WHEN ISNULL(d.ReceiptDetails, '') = 'Cash Tips Earned' THEN ISNULL(d.AmountTip, 0) ELSE 0 END AS CashTip,
               CASE WHEN ISNULL(d.ReceiptDetails, '') <> 'Cash Tips Earned' THEN ISNULL(d.AmountTip, 0) + ISNULL(d.AmountTipCreditCardFee, 0) ELSE 0 END AS NetTip
        FROM dbo.tblCashAndSalesReceiptsDetail d
        INNER JOIN Batches b ON b.StoreID = d.StoreID AND b.BatchID = d.BatchID AND d.DayNumber = b.DayNumber
        INNER JOIN dbo.tblCashAndSalesTimeEntries te ON te.StoreID = d.StoreID AND te.BatchID = d.BatchID AND te.TimeEntryItemID = d.ServerTimeEntryItemID
        INNER JOIN dbo.tblStaffDepartments sd ON sd.StoreID = te.StoreID AND sd.StaffDepartmentID = te.StaffDepartmentID
        LEFT OUTER JOIN CheckMode cm ON cm.StoreID = d.StoreID AND cm.BatchID = d.BatchID AND cm.CheckNumber = d.CheckNumber AND cm.PersonNumber = d.PersonNumber
        LEFT OUTER JOIN dbo.tblReceiptTypes rt ON rt.StoreID = d.StoreID AND rt.ReceiptTypeID = d.ReceiptTypeID
        LEFT OUTER JOIN dbo.tblTipPayrollReceiptTypeFee rf ON rf.StoreID = d.StoreID AND rf.ReceiptTypeID = d.ReceiptTypeID
        LEFT OUTER JOIN dbo.tblTipPayrollSettings ts ON ts.StoreID = d.StoreID
        LEFT OUTER JOIN dbo.tblTipPayrollExcludedDepartments ex ON ex.StoreID = te.StoreID AND ex.StaffDepartmentID = te.StaffDepartmentID
        WHERE ex.StaffDepartmentID IS NULL
        -- receipts without a check number (the 'Cash Tips Earned' lines may be) have no mode and count only as cash tips
    ),
    PerDay AS (
        -- one row per employee, department group and day, so the tip out is rounded per day as the consolidated screen does
        SELECT x.StoreID, x.BatchID, x.DayNumber, x.StoreStaffID, x.StaffDepartmentGroupID,
               SUM(CASE WHEN x.Mode = 1 THEN x.Sales ELSE 0 END) AS CashSales,
               SUM(CASE WHEN x.Mode = 2 THEN x.Sales ELSE 0 END) AS CreditCardSales,
               SUM(CASE WHEN x.Mode = 3 THEN x.Sales ELSE 0 END) AS ServiceChargeSales,
               SUM(x.CashTip) AS CashTips,
               SUM(CASE WHEN x.Mode = 2 THEN x.NetTip ELSE 0 END) AS CardTips,
               SUM(CASE WHEN x.Mode = 3 THEN x.NetTip ELSE 0 END) AS VoluntaryTipOnServiceCharge,
               SUM(CASE WHEN x.Mode = 3 THEN x.Charge ELSE 0 END) AS ServiceChargeGross,
               SUM(CASE WHEN x.Mode = 3 THEN x.ChargeFee ELSE 0 END) AS ServiceChargeFee
        FROM Receipt x
        GROUP BY x.StoreID, x.BatchID, x.DayNumber, x.StoreStaffID, x.StaffDepartmentGroupID
    ),
    PerDayRated AS (
        SELECT p.*,
               CASE WHEN ISNULL(g.StaffDepartmentDefaultTipOutPercentage2, 0) <> 0 AND p.DayNumber >= ISNULL(g.StaffDepartmentDefaultTipOutPercentage2EffectiveDayNumber, 0)
                    THEN g.StaffDepartmentDefaultTipOutPercentage2 ELSE g.StaffDepartmentDefaultTipOutPercentage END AS TipOutPercent
        FROM PerDay p
        INNER JOIN dbo.tblStaffDepartmentGroups g ON g.StoreID = p.StoreID AND g.StaffDepartmentGroupID = p.StaffDepartmentGroupID
    ),
    Lines AS (
        SELECT StoreID, StoreStaffID, StaffDepartmentGroupID,
               CashSales, CreditCardSales, ServiceChargeSales, CashTips, CardTips, VoluntaryTipOnServiceCharge, ServiceChargeGross, ServiceChargeFee,
               FLOOR((CashSales + CreditCardSales) * TipOutPercent + 0.5) / 100.0 AS TipOutOnCashCreditSales,
               FLOOR(ServiceChargeSales * TipOutPercent + 0.5) / 100.0 AS TipOutOnServiceChargeSales,
               CAST(0 AS money) AS MovedCardTips, CAST(0 AS money) AS MovedServiceChargeTips, CAST(TipOutPercent AS decimal(18, 4)) AS TipOutPercent
        FROM PerDayRated
        UNION ALL
        -- manual moves: the receiving employee gets +, the giving employee -
        SELECT a.StoreID, a.AddStoreStaffID, a.AddStaffDepartmentGroupID,
               ISNULL(a.CashSales, 0), ISNULL(a.CreditCardSales, 0), ISNULL(a.ServiceChargeSales, 0), ISNULL(a.CashTips, 0), 0, 0, 0, 0,
               FLOOR(a.AddTipOutPercentage * (ISNULL(a.CashSales, 0) + ISNULL(a.CreditCardSales, 0)) + 0.5) / 100.0,
               FLOOR(a.AddTipOutPercentage * ISNULL(a.ServiceChargeSales, 0) + 0.5) / 100.0,
               ISNULL(a.CreditCardTips, 0), ISNULL(a.ServiceChargeTips, 0), CAST(0 AS decimal(18, 4))
        FROM dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments a
        INNER JOIN Batches b ON b.StoreID = a.StoreID AND b.BatchID = a.BatchID
        UNION ALL
        SELECT a.StoreID, a.SubtractStoreStaffID, a.SubtractStaffDepartmentGroupID,
               -ISNULL(a.CashSales, 0), -ISNULL(a.CreditCardSales, 0), -ISNULL(a.ServiceChargeSales, 0), -ISNULL(a.CashTips, 0), 0, 0, 0, 0,
               -FLOOR(a.SubtractTipOutPercentage * (ISNULL(a.CashSales, 0) + ISNULL(a.CreditCardSales, 0)) + 0.5) / 100.0,
               -FLOOR(a.SubtractTipOutPercentage * ISNULL(a.ServiceChargeSales, 0) + 0.5) / 100.0,
               -ISNULL(a.CreditCardTips, 0), -ISNULL(a.ServiceChargeTips, 0), CAST(0 AS decimal(18, 4))
        FROM dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments a
        INNER JOIN Batches b ON b.StoreID = a.StoreID AND b.BatchID = a.BatchID
    ),
    Totals AS (
        SELECT StoreID, StoreStaffID, StaffDepartmentGroupID,
               SUM(CashSales) AS CashSales, SUM(CreditCardSales) AS CreditCardSales, SUM(ServiceChargeSales) AS ServiceChargeSales,
               SUM(CashTips) AS CashTips, SUM(CardTips) AS CardTips, SUM(VoluntaryTipOnServiceCharge) AS VoluntaryTipOnServiceCharge,
               SUM(ServiceChargeGross) AS ServiceChargeGross, SUM(ServiceChargeFee) AS ServiceChargeFee,
               SUM(TipOutOnCashCreditSales) AS TipOutOnCashCreditSales, SUM(TipOutOnServiceChargeSales) AS TipOutOnServiceChargeSales,
               SUM(MovedCardTips) AS MovedCardTips, SUM(MovedServiceChargeTips) AS MovedServiceChargeTips, MAX(TipOutPercent) AS TipOutPercent
        FROM Lines
        GROUP BY StoreID, StoreStaffID, StaffDepartmentGroupID
    )
    SELECT t.StoreID, t.StoreStaffID,
           CAST(stf.PayrollID % r.BigNumber AS bigint) AS PayrollID,
           ISNULL(RTRIM(LTRIM(stf.LastName)) + ', ', '') + ISNULL(RTRIM(LTRIM(stf.FirstName)), '') AS EmployeeName,
           t.StaffDepartmentGroupID, g.StaffDepartmentGroup, g.PayrollDepartment, g.StaffDepartmentGroupSort,
           CAST(t.CashSales AS money) AS CashSales, CAST(t.CreditCardSales AS money) AS CreditCardSales, CAST(t.ServiceChargeSales AS money) AS ServiceChargeSales,
           CAST(t.TipOutOnCashCreditSales AS money) AS TipOutOnCashCreditSales, CAST(t.TipOutOnServiceChargeSales AS money) AS TipOutOnServiceChargeSales,
           CAST(ISNULL(NULLIF(t.TipOutPercent, 0), g.StaffDepartmentDefaultTipOutPercentage) AS decimal(18, 4)) AS TipOutPercent,
           CAST(t.ServiceChargeGross AS money) AS ServiceChargeGross, CAST(t.ServiceChargeFee AS money) AS ServiceChargeFee,
           CAST(t.VoluntaryTipOnServiceCharge AS money) AS VoluntaryTipOnServiceCharge,
           CAST(t.CashTips AS money) AS CTP,
           CAST(t.CardTips + t.MovedCardTips - t.TipOutOnCashCreditSales AS money) AS CreditCardTipsLessTipOut,
           CAST(t.CardTips + t.MovedCardTips - t.TipOutOnCashCreditSales + t.VoluntaryTipOnServiceCharge AS money) AS CCT,
           CAST(t.ServiceChargeGross - t.ServiceChargeFee + t.MovedServiceChargeTips - t.TipOutOnServiceChargeSales AS money) AS SVC
    FROM Totals t
    INNER JOIN dbo.tblStaff stf ON stf.StoreID = t.StoreID AND stf.StoreStaffID = t.StoreStaffID
    INNER JOIN dbo.tblStaffDepartmentGroups g ON g.StoreID = t.StoreID AND g.StaffDepartmentGroupID = t.StaffDepartmentGroupID
    CROSS JOIN Rates r
    WHERE stf.PayrollID <> 0
      AND (stf.PayrollID % r.BigNumber) BETWEEN r.IDLow AND r.IDHigh
);
GO
IF OBJECT_ID(N'dbo.fn_CustomReport_TipsExportNew', N'TF') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_CustomReport_TipsExportNew(@StoreIDList varchar(max), @StartingDate datetime, @EndingDate datetime)
            RETURNS @table TABLE (ItemID int identity(1,1) primary key) AS BEGIN RETURN END');
GO
ALTER FUNCTION dbo.fn_CustomReport_TipsExportNew (@StoreIDList varchar(max), @StartingDate datetime, @EndingDate datetime)
RETURNS @table TABLE (
    ItemID int identity(1,1) primary key,
    [Employee Number] int,
    [Regular Hours] decimal(9,4),
    [Overtime Hours] decimal(9,4),
    [Hours Code] varchar(20),
    [Coded Hours] decimal(9,4),
    [Amount Code] varchar(20),
    [Coded Amount] decimal(9,4),
    [Deduction Code] varchar(20),
    [Deduction Amount] decimal(9,4),
    [Exception Department] varchar(20),
    [Exception Rate] money
)
AS
BEGIN
    /* select * from dbo.fn_CustomReport_TipsExportNew('13', '02/09/2026', '02/22/2026')
       Add it to the Exports list in dbo.fn_CustomReports_Index (a new database's index is its own):
           insert into @table (CustomReportName, CustomReportSQLFunctionName, CustomReportExcelFileName) values ('Tips Export','fn_CustomReport_TipsExportNew','Tips Export.xls') */
    INSERT INTO @table ([Employee Number], [Regular Hours], [Overtime Hours], [Hours Code], [Coded Hours], [Amount Code], [Coded Amount], [Deduction Code], [Deduction Amount], [Exception Department], [Exception Rate])
    SELECT PayrollID, NULL, NULL, '', NULL, AmountCode, Amount, '', NULL, PayrollDepartment, NULL
    FROM (
        SELECT PayrollID, PayrollDepartment, 'CTP' AS AmountCode, 3 AS Sort, SUM(CTP) AS Amount FROM dbo.fn_TipsByEmployee(@StoreIDList, @StartingDate, @EndingDate) GROUP BY PayrollID, PayrollDepartment
        UNION ALL
        SELECT PayrollID, PayrollDepartment, 'CCT', 2, SUM(CCT) FROM dbo.fn_TipsByEmployee(@StoreIDList, @StartingDate, @EndingDate) GROUP BY PayrollID, PayrollDepartment
        UNION ALL
        SELECT PayrollID, PayrollDepartment, 'SVC', 1, SUM(SVC) FROM dbo.fn_TipsByEmployee(@StoreIDList, @StartingDate, @EndingDate) GROUP BY PayrollID, PayrollDepartment
    ) q
    WHERE Amount <> 0
    ORDER BY PayrollID, PayrollDepartment, Sort DESC;
    RETURN;
END
GO
