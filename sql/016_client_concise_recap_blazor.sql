-- Run on each CLIENT database (or let Database Updater do it). Safe to run again (it ALTERs or CREATEs).
-- Adds dbo.fn_ConciseDailyRecapBlazor: the numbers of the emailed "Daily Sales Recap" (dbo.fn_ConciseDailyRecapHTML2), as a plain table, for the home screen.
--   * The HTML function only formats rows from dbo.fn_ConciseDailyRecapUpright, which in turn calls dbo.fn_ConciseDailyRecapInnerCore. This function calls
--     InnerCore for the three dates the email compared (the day, 364 days back, 728 days back: the same weekday) and lines them up per store. Nothing is formatted here.
--   * One row per store, ordered by RowSeq (an inline function cannot promise an order, so the caller does "order by RowSeq"), then one "All Stores" row (IsTotal = 1).
--   * Per date: GrossSales, Discounts, Sales (net), WeekToDate, Covers, WeekToDateCovers. Plain names are the day itself; ...LastYear is 364 days back, ...TwoYearsAgo is 728 back.
--     A store with nothing on a comparison date gets NULL there (the email showed a blank).
--   * Only the stores in @StoreListCSV (StoreIDs, comma separated) are returned, and the "All Stores" row adds up ONLY those stores. InnerCore knows every store
--     in the database, so the filter matters: a login that may see two stores must never see a total that includes the others. An empty list returns nothing.
--   * The email's "All Stores" block is InnerCore's own total row. This function adds up the stores it returns instead, so with every store listed the figures match,
--     and with a few stores listed the total is theirs alone.
-- Nothing existing is changed. Needs dbo.fn_ConciseDailyRecapInnerCore (all DD Cash And Sales databases that have the emailed recap have it).
-- Needs compatibility level 110 or lower-compatible features only (no STRING_SPLIT, no batch mode); the store list is split inline (XML) as in script 012.
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
    WITH StoreList AS (
        -- every number in the list; anything that is not a number is ignored; no list, no stores
        SELECT DISTINCT TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) AS StoreID
        FROM (SELECT CAST('<i>' + REPLACE(REPLACE(REPLACE(@StoreListCSV, ' ', ''), ';', ','), ',', '</i><i>') + '</i>' AS xml) AS doc) s
        CROSS APPLY s.doc.nodes('/i') x(i)
        WHERE LEN(ISNULL(@StoreListCSV, '')) > 0 AND TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) IS NOT NULL
    ),
    Allowed AS (
        -- InnerCore reports by StoreNumber (the same key the emailed recap lined its columns up on)
        SELECT DISTINCT st.StoreNumber
        FROM dbo.tblStores st WITH (NOLOCK)
        JOIN StoreList sl ON sl.StoreID = st.StoreID
    ),
    Cur AS (
        SELECT c.*
        FROM dbo.fn_ConciseDailyRecapInnerCore(@EndingDate) c
        WHERE c.StoreNumber IN (SELECT StoreNumber FROM Allowed)
          AND NOT (c.DisplayOrder = 99 AND c.StoreNumber = 9999)          -- InnerCore's own all-stores row is replaced by Totals below
    ),
    Ly1 AS (
        SELECT c.*
        FROM dbo.fn_ConciseDailyRecapInnerCore(DATEADD(day, -364, @EndingDate)) c
        WHERE c.StoreNumber IN (SELECT StoreNumber FROM Allowed)
          AND NOT (c.DisplayOrder = 99 AND c.StoreNumber = 9999)
    ),
    Ly2 AS (
        SELECT c.*
        FROM dbo.fn_ConciseDailyRecapInnerCore(DATEADD(day, -728, @EndingDate)) c
        WHERE c.StoreNumber IN (SELECT StoreNumber FROM Allowed)
          AND NOT (c.DisplayOrder = 99 AND c.StoreNumber = 9999)
    ),
    Stores AS (
        SELECT CAST(0 AS bit) AS IsTotal, c.DisplayOrder, c.StoreGroupName, c.StoreNumber, c.StoreName,
               c.GrossSales, c.Discounts, c.Sales, c.WeekToDate, c.Covers, c.WeekToDateCovers,
               a.GrossSales AS GrossSalesLastYear, a.Discounts AS DiscountsLastYear, a.Sales AS SalesLastYear,
               a.WeekToDate AS WeekToDateLastYear, a.Covers AS CoversLastYear, a.WeekToDateCovers AS WeekToDateCoversLastYear,
               b.GrossSales AS GrossSalesTwoYearsAgo, b.Discounts AS DiscountsTwoYearsAgo, b.Sales AS SalesTwoYearsAgo,
               b.WeekToDate AS WeekToDateTwoYearsAgo, b.Covers AS CoversTwoYearsAgo, b.WeekToDateCovers AS WeekToDateCoversTwoYearsAgo
        FROM Cur c
        LEFT JOIN Ly1 a ON a.DisplayOrder = c.DisplayOrder AND a.StoreNumber = c.StoreNumber
        LEFT JOIN Ly2 b ON b.DisplayOrder = c.DisplayOrder AND b.StoreNumber = c.StoreNumber
    ),
    Totals AS (
        -- each date adds up its own allowed stores (a store that closed since last year still counts in last year's total, as in the email)
        SELECT CAST(1 AS bit) AS IsTotal, 99 AS DisplayOrder, CAST('All Stores' AS varchar(100)) AS StoreGroupName, 9999 AS StoreNumber, CAST('All Stores' AS varchar(100)) AS StoreName,
               n.GrossSales, n.Discounts, n.Sales, n.WeekToDate, n.Covers, n.WeekToDateCovers,
               a.GrossSales AS GrossSalesLastYear, a.Discounts AS DiscountsLastYear, a.Sales AS SalesLastYear,
               a.WeekToDate AS WeekToDateLastYear, a.Covers AS CoversLastYear, a.WeekToDateCovers AS WeekToDateCoversLastYear,
               b.GrossSales AS GrossSalesTwoYearsAgo, b.Discounts AS DiscountsTwoYearsAgo, b.Sales AS SalesTwoYearsAgo,
               b.WeekToDate AS WeekToDateTwoYearsAgo, b.Covers AS CoversTwoYearsAgo, b.WeekToDateCovers AS WeekToDateCoversTwoYearsAgo
        FROM (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
              FROM Cur HAVING COUNT(*) > 0) n
        CROSS JOIN (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
                      FROM Ly1) a
        CROSS JOIN (SELECT SUM(GrossSales) AS GrossSales, SUM(Discounts) AS Discounts, SUM(Sales) AS Sales, SUM(WeekToDate) AS WeekToDate, SUM(Covers) AS Covers, SUM(WeekToDateCovers) AS WeekToDateCovers
                      FROM Ly2) b
    )
    SELECT ROW_NUMBER() OVER (ORDER BY u.IsTotal, u.DisplayOrder, u.StoreNumber) AS RowSeq,
           DATENAME(weekday, @EndingDate) AS DayName,
           u.*
    FROM (SELECT * FROM Stores UNION ALL SELECT * FROM Totals) u
)
GO
