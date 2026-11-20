-- Run on each CLIENT database (or let Database Updater do it). Safe to run again (it ALTERs or CREATEs).
-- A faster dbo.fn_ConciseDailyRecapBlazor (replaces the body that script 016 created; same name, same columns, same meaning).
-- Why 016 was slow: it called dbo.fn_ConciseDailyRecapInnerCore three times (the day, 364 and 728 days back), and each call
--   * reads sales from the 1st of the month (or the last 7 days) although the recap only needs the current WEEK,
--   * works out Last 7 Days, Month To Date, a weekly budget (a scalar function run on every row of tblBudgetMain, on every call), a completeness rating and batch counts, none of which the recap shows,
--   * does it for EVERY store in the database, and only afterwards were the user's stores picked out,
--   * runs one after the other (a multi-statement function cannot run in parallel).
-- This version reads tblCashAndSalesSales ONCE for all three days, only from the Monday of each week through that day, only for the user's stores,
-- and computes only what the recap shows. The rules are copied from InnerCore as they are (profit-center mapping incl. pseudo stores, sale types that count as sales,
-- main types 1 to 3, discount = Discount <> 0, stores shown when active, group display order <> 0, covers from the receipt details with their fixed exclusions).
-- One difference on purpose: InnerCore also listed a store that had sales earlier in the month but none this week (all zeros today); this version lists a store only when it has sales
-- in the current week of that day, so a store closed all week no longer shows its comparison figures either. Run sql/tools_concise_recap_compare.sql to see any such difference.
-- Needs compatibility level 110 or lower-compatible features only (no STRING_SPLIT, no batch mode).
set nocount on;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF OBJECT_ID(N'dbo.fn_ConciseDailyRecapBlazor', N'IF') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_ConciseDailyRecapBlazor() RETURNS TABLE AS RETURN SELECT 1 AS Placeholder');
GO
ALTER FUNCTION dbo.fn_ConciseDailyRecapBlazor (@EndingDate datetime, @StoreListCSV varchar(8000))
RETURNS TABLE
AS
RETURN
(
    /* select * from dbo.fn_ConciseDailyRecapBlazor('04/29/2021', '1,2,3') order by RowSeq */
    -- LEAN RECAP: reads the sales tables directly, once for all three days (script 017)
    WITH StoreList AS (
        -- every number in the list; anything that is not a number is ignored; no list, no stores
        SELECT DISTINCT TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) AS StoreID
        FROM (SELECT CAST('<i>' + REPLACE(REPLACE(REPLACE(@StoreListCSV, ' ', ''), ';', ','), ',', '</i><i>') + '</i>' AS xml) AS doc) s
        CROSS APPLY s.doc.nodes('/i') x(i)
        WHERE LEN(ISNULL(@StoreListCSV, '')) > 0 AND TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) IS NOT NULL
    ),
    Dates AS (
        -- Slot 0 = the day, 1 = 364 days back, 2 = 728 days back (the same weekday); a week runs Monday to Sunday (day number 0 is a Monday)
        SELECT d.Slot, d.Dt, dbo.fn_CalcDayNumber(d.Dt) AS TodayDN, (dbo.fn_CalcDayNumber(d.Dt) / 7) * 7 AS WeekStartDN
        FROM (SELECT 0 AS Slot, @EndingDate AS Dt
              UNION ALL SELECT 1, DATEADD(day, -364, @EndingDate)
              UNION ALL SELECT 2, DATEADD(day, -728, @EndingDate)) d
    ),
    ProfitCenterMap AS (
        -- a profit center counts toward its own store, unless it is mapped into a pseudo store, then toward that store (as in InnerCore)
        SELECT pc.StoreID AS SourceStoreID, pc.ProfitCenterID AS SourceProfitCenterID, pc.StoreID AS DestinationStoreID
        FROM dbo.tblProfitCenters pc
        LEFT OUTER JOIN (
            SELECT p.StoreID, p.ItemID AS ProfitCenterItemID
            FROM dbo.tblProfitCenters p
            INNER JOIN dbo.tblStoresPseudoProfitCenters sp ON p.ItemID = sp.ProfitCenterItemID
        ) ppc ON pc.StoreID = ppc.StoreID AND pc.ItemID = ppc.ProfitCenterItemID
        WHERE ppc.ProfitCenterItemID IS NULL
        UNION ALL
        SELECT p.StoreID, p.ProfitCenterID, sp.StoreID
        FROM dbo.tblStores s
        INNER JOIN dbo.tblStoresPseudoProfitCenters sp ON s.StoreID = sp.StoreID
        INNER JOIN dbo.tblProfitCenters p ON p.ItemID = sp.ProfitCenterItemID
    ),
    Sold AS (
        -- one pass over the sales of the three weeks, for the user's stores only
        SELECT dn.Slot, m.DestinationStoreID AS StoreID,
               SUM(CASE WHEN css.DayNumber = dn.TodayDN AND Discount = 0 THEN Amount ELSE 0 END) AS GrossSales,
               SUM(CASE WHEN css.DayNumber = dn.TodayDN AND Discount <> 0 THEN Amount ELSE 0 END) AS Discounts,
               SUM(CASE WHEN css.DayNumber = dn.TodayDN THEN Amount ELSE 0 END) AS Sales,
               SUM(CASE WHEN FLOOR(css.DayNumber / 7) = FLOOR(dn.TodayDN / 7) THEN Amount ELSE 0 END) AS WeekToDate
        FROM Dates dn
        INNER JOIN dbo.tblCashAndSalesSales css ON css.DayNumber BETWEEN dn.WeekStartDN AND dn.TodayDN
        INNER JOIN dbo.tblSaleTypes tss ON css.StoreID = tss.StoreID AND css.SaleTypeID = tss.SaleTypeID
        INNER JOIN dbo.tblCashAndSalesBatches csb ON csb.BatchID = css.BatchID
        INNER JOIN ProfitCenterMap m ON m.SourceStoreID = css.StoreID AND m.SourceProfitCenterID = css.ProfitCenterID
        INNER JOIN StoreList sl ON sl.StoreID = m.DestinationStoreID
        WHERE tss.ExcludeFromSales = 0
          AND css.MainTypeID IN (1, 2, 3)
        GROUP BY dn.Slot, m.DestinationStoreID
    ),
    Cov AS (
        -- covers by receipt (the fixed exclusions are InnerCore's: stores 1, 4 and 11, profit center 60001)
        SELECT dn.Slot, csb.StoreID,
               SUM(CASE WHEN csb.DayNumber = dn.TodayDN THEN csrd.CoverCount ELSE 0 END) AS Covers,
               SUM(csrd.CoverCount) AS WeekToDateCovers
        FROM Dates dn
        INNER JOIN dbo.tblCashAndSalesBatches csb WITH (NOLOCK) ON csb.DayNumber >= dn.WeekStartDN AND csb.DayNumber <= dn.TodayDN
        INNER JOIN dbo.tblCashAndSalesReceiptsDetail csrd WITH (NOLOCK) ON csb.BatchID = csrd.BatchID
        WHERE NOT csb.StoreID IN (1, 4, 11)
          AND NOT csrd.ProfitCenterID IN (60001)
          AND csb.StoreID IN (SELECT StoreID FROM StoreList)
        GROUP BY dn.Slot, csb.StoreID
    ),
    PerStore AS (
        SELECT s.Slot, sg.DisplayOrder, sg.StoreGroupName, st.StoreNumber,
               ISNULL(st.StoreReportName, st.StoreName) AS StoreName,
               s.GrossSales, s.Discounts, s.Sales, s.WeekToDate,
               CAST(ISNULL(cv.Covers, 0) AS money) AS Covers, CAST(ISNULL(cv.WeekToDateCovers, 0) AS money) AS WeekToDateCovers
        FROM Sold s
        INNER JOIN Dates dn ON dn.Slot = s.Slot
        INNER JOIN dbo.tblStores st ON st.StoreID = s.StoreID
        INNER JOIN dbo.tblStoreGroups sg ON sg.StoreGroupID = st.StoreGroupID
        LEFT OUTER JOIN Cov cv ON cv.Slot = s.Slot AND cv.StoreID = s.StoreID
        WHERE (st.FinalDate IS NULL OR dn.Dt BETWEEN st.ActiveDate AND DATEADD(day, 6, st.FinalDate))
          AND sg.DisplayOrder <> 0
    ),
    Stores AS (
        SELECT CAST(0 AS bit) AS IsTotal, c.DisplayOrder, c.StoreGroupName, c.StoreNumber, c.StoreName,
               c.GrossSales, c.Discounts, c.Sales, c.WeekToDate, c.Covers, c.WeekToDateCovers,
               a.GrossSales AS GrossSalesLastYear, a.Discounts AS DiscountsLastYear, a.Sales AS SalesLastYear,
               a.WeekToDate AS WeekToDateLastYear, a.Covers AS CoversLastYear, a.WeekToDateCovers AS WeekToDateCoversLastYear,
               b.GrossSales AS GrossSalesTwoYearsAgo, b.Discounts AS DiscountsTwoYearsAgo, b.Sales AS SalesTwoYearsAgo,
               b.WeekToDate AS WeekToDateTwoYearsAgo, b.Covers AS CoversTwoYearsAgo, b.WeekToDateCovers AS WeekToDateCoversTwoYearsAgo
        FROM PerStore c
        LEFT OUTER JOIN PerStore a ON a.Slot = 1 AND a.DisplayOrder = c.DisplayOrder AND a.StoreNumber = c.StoreNumber
        LEFT OUTER JOIN PerStore b ON b.Slot = 2 AND b.DisplayOrder = c.DisplayOrder AND b.StoreNumber = c.StoreNumber
        WHERE c.Slot = 0
    ),
    Totals AS (
        -- each date adds up its own stores (a store that closed since last year still counts in last year's total, as in the email)
        SELECT CAST(1 AS bit) AS IsTotal, 99 AS DisplayOrder, CAST('All Stores' AS varchar(100)) AS StoreGroupName, 9999 AS StoreNumber, CAST('All Stores' AS varchar(100)) AS StoreName,
               n.GrossSales, n.Discounts, n.Sales, n.WeekToDate, n.Covers, n.WeekToDateCovers,
               a.GrossSales AS GrossSalesLastYear, a.Discounts AS DiscountsLastYear, a.Sales AS SalesLastYear,
               a.WeekToDate AS WeekToDateLastYear, a.Covers AS CoversLastYear, a.WeekToDateCovers AS WeekToDateCoversLastYear,
               b.GrossSales AS GrossSalesTwoYearsAgo, b.Discounts AS DiscountsTwoYearsAgo, b.Sales AS SalesTwoYearsAgo,
               b.WeekToDate AS WeekToDateTwoYearsAgo, b.Covers AS CoversTwoYearsAgo, b.WeekToDateCovers AS WeekToDateCoversTwoYearsAgo
        FROM (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
              FROM PerStore WHERE Slot = 0 HAVING COUNT(*) > 0) n
        CROSS JOIN (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
                      FROM PerStore WHERE Slot = 1) a
        CROSS JOIN (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
                      FROM PerStore WHERE Slot = 2) b
    )
    SELECT ROW_NUMBER() OVER (ORDER BY u.IsTotal, u.DisplayOrder, u.StoreNumber) AS RowSeq,
           DATENAME(weekday, @EndingDate) AS DayName,
           u.*
    FROM (SELECT * FROM Stores UNION ALL SELECT * FROM Totals) u
)
GO
