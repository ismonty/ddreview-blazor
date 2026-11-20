-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again.
-- Stage 2 of the set-based General Ledger: the sales-mapping step.
-- Timing showed almost all of the per-store function's time is dbo.fn_CashAndSalesMappedRecords: a cursor over every sales line times every
-- matching mapping rule, saving values in variables. This script adds, beside the old objects (nothing existing is changed):
--   1) dbo.fn_CashAndSalesMappedRecordsBlazor  - the same rows as fn_CashAndSalesMappedRecords, as one inline query:
--        per sales line the FIRST normal mapping (lowest Priority) gives the account, the FIRST discount mapping gives the discount account.
--   2) dbo.fn_CashAndSalesGLandARBlazor        - a COPY of this database's own dbo.fn_CashAndSalesGLandAR (text read from the database, so its
--        local differences are kept) whose only change is that it calls the function above.
--   3) dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor is altered to call that copy.
-- Old behaviour that is matched on purpose (so old and new compare equal; fix later, separately, if wanted): the quantity passes through an int
-- (decimals dropped), and the very last line of the run comes out with no DayNumber.
-- If the base function changes later, run this script again to refresh the copy. To undo: run script 012 again (it points the function back).
set nocount on;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF OBJECT_ID(N'dbo.fn_CashAndSalesMappedRecordsBlazor', N'IF') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_CashAndSalesMappedRecordsBlazor() RETURNS TABLE AS RETURN SELECT 1 AS Placeholder');
GO
ALTER FUNCTION dbo.fn_CashAndSalesMappedRecordsBlazor (@StoreID int, @StartDate datetime, @EndDate datetime, @ARFlag int)
RETURNS TABLE
AS
RETURN
(
    /* select * from dbo.fn_CashAndSalesMappedRecordsBlazor(1,'09/22/2015','09/22/2015',1) where plu=20001 */
    WITH B AS (
        SELECT BatchID FROM dbo.tblCashAndSalesBatches WITH (NOLOCK)
        WHERE StoreID = @StoreID AND PostingDate BETWEEN @StartDate AND @EndDate
    ),
    S AS (
        SELECT css.BatchID, css.DayNumber, css.ItemID, css.StoreID, css.MainTypeID, css.ProfitCenterID, css.SaleTypeID, css.Amount,
            css.SalesDiscounted, css.Special, css.Discount, css.PLU, css.SalesTaxExempt AS TaxExempt, css.Quantity, css.CreditAccountID,
            CASE WHEN css.ItemPrice < css.ItemBasePrice AND css.Special = 1 THEN (css.ItemBasePrice - css.ItemPrice) * css.Quantity ELSE 0 END AS SpecialDiscount,
            ISNULL(css.SalesTaxRate1, 0) + ISNULL(css.SalesTaxRate2, 0) + ISNULL(css.SalesTaxRate3, 0) + ISNULL(css.SalesTaxRate4, 0) AS SalesTaxRate,
            css.SaleItemID, css.POSWindowID
        FROM B
        INNER JOIN dbo.tblCashAndSalesSales css WITH (NOLOCK) ON css.BatchID = B.BatchID
        LEFT OUTER JOIN dbo.tblMenuItems mi ON mi.PLU = css.PLU
        WHERE (mi.StoreID IS NULL OR mi.StoreID = @StoreID)
          AND (mi.GiftCertificate IS NULL OR mi.GiftCertificate = 0)
    ),
    M AS (
        -- every sales line with every mapping rule that applies to it
        SELECT S.BatchID, S.DayNumber, S.ItemID, S.StoreID, S.MainTypeID, S.ProfitCenterID, S.SaleTypeID, S.Amount, S.SalesDiscounted,
            S.Special, S.Discount, S.PLU, S.TaxExempt, S.Quantity, S.SpecialDiscount, S.SalesTaxRate, S.SaleItemID, S.POSWindowID,
            map.Priority,
            CASE WHEN S.CreditAccountID IS NULL THEN map.CreditAccountID ELSE S.CreditAccountID END AS CreditAccountID,
            CASE WHEN S.TaxExempt <> 0 AND ac.ExemptTaxTypeID IS NOT NULL THEN ac.ExemptTaxTypeID ELSE ac.TaxTypeID END AS TaxTypeID,
            CASE WHEN map.Discount <> 0 THEN 1 ELSE 0 END AS DiscountMapping,
            CASE WHEN S.CreditAccountID IS NULL THEN NULL ELSE map.CreditAccountID END AS OverrideCreditAccountID
        FROM S
        INNER JOIN dbo.tblAccountsSalesMapping map WITH (NOLOCK) ON S.StoreID = map.StoreID
        LEFT OUTER JOIN dbo.tblAccountsCredit ac WITH (NOLOCK)
            ON ac.CreditAccountID = CASE WHEN S.CreditAccountID IS NULL THEN map.CreditAccountID ELSE S.CreditAccountID END
        WHERE (map.ProfitCenterID IS NULL OR map.ProfitCenterID = S.ProfitCenterID)
          AND (map.MainTypeID IS NULL OR map.MainTypeID = S.MainTypeID)
          AND (map.SaleTypeID IS NULL OR map.SaleTypeID = S.SaleTypeID)
          AND (map.Special IS NULL OR map.Special = S.Special)
          AND (map.Discount IS NULL OR (map.Discount = -1 AND S.Discount <> 0) OR map.Discount = S.Discount)
          AND (map.PLU IS NULL OR (map.PLU = -1 AND S.PLU <> 0) OR map.PLU = S.PLU)
          AND (map.TaxExempt IS NULL OR map.TaxExempt = S.TaxExempt)
          AND (map.CompositeSalesTaxRate IS NULL OR map.CompositeSalesTaxRate = S.SalesTaxRate)
          AND (map.POSWindowID IS NULL OR map.POSWindowID = S.POSWindowID)
    ),
    R1 AS (
        SELECT M.*, ROW_NUMBER() OVER (PARTITION BY M.ItemID, M.DiscountMapping ORDER BY M.Priority) AS Rk
        FROM M
    ),
    R AS (
        -- one pass: each line carries its item's first discount mapping, and the key of the run's last line (the legacy loop wrote that line without a DayNumber)
        SELECT R1.*,
            MAX(CASE WHEN R1.DiscountMapping = 1 AND R1.Rk = 1 THEN R1.CreditAccountID END) OVER (PARTITION BY R1.ItemID) AS DiscCreditAccountID,
            MAX(CASE WHEN R1.DiscountMapping = 1 AND R1.Rk = 1 THEN R1.TaxTypeID END) OVER (PARTITION BY R1.ItemID) AS DiscTaxTypeID,
            MAX(CASE WHEN R1.DiscountMapping = 1 AND R1.Rk = 1 THEN 1 END) OVER (PARTITION BY R1.ItemID) AS HasDisc,
            MAX(CAST(R1.BatchID AS bigint) * 4294967296 + CAST(R1.ItemID AS bigint) + 2147483648) OVER () AS LastKey
        FROM R1
    ),
    Mapped AS (
        SELECT
            n.BatchID,
            CASE WHEN n.ItemID > 0 AND CAST(n.BatchID AS bigint) * 4294967296 + CAST(n.ItemID AS bigint) + 2147483648 = n.LastKey THEN NULL ELSE n.DayNumber END AS DayNumber,
            n.ItemID, n.StoreID, n.MainTypeID, n.ProfitCenterID, n.SaleTypeID, n.Amount, n.SalesDiscounted, n.Special, n.Discount, n.PLU, n.TaxExempt,
            CAST(CAST(n.Quantity AS int) AS decimal(18, 2)) AS Quantity,
            COALESCE(n.DiscCreditAccountID, n.CreditAccountID) AS CreditAccountID,
            n.CreditAccountID AS NonDiscountCreditAccountID,
            n.TaxTypeID,
            CASE WHEN n.HasDisc = 1 THEN n.DiscTaxTypeID ELSE n.TaxTypeID END AS DiscountTaxTypeID,
            CAST(NULL AS int) AS Priority,
            n.SpecialDiscount, n.SalesTaxRate, n.SaleItemID, n.OverrideCreditAccountID, n.POSWindowID
        FROM R n
        WHERE n.DiscountMapping = 0 AND n.Rk = 1
    ),
    Adj AS (
        SELECT csb.BatchID, csb.DayNumber, csb.StoreID, stadjust.SalesAmount AS Amount, stadjust.CreditAccountID, ac.TaxTypeID
        FROM dbo.tblCashAndSalesBatches csb WITH (NOLOCK)
        INNER JOIN (SELECT DISTINCT BatchID FROM S) ab ON csb.BatchID = ab.BatchID
        INNER JOIN dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales stadjust WITH (NOLOCK) ON csb.BatchID = stadjust.BatchID
        INNER JOIN dbo.tblAccountsCredit ac ON stadjust.CreditAccountID = ac.CreditAccountID
    )
    SELECT BatchID, DayNumber, ItemID, StoreID, MainTypeID, ProfitCenterID, SaleTypeID, Amount, SalesDiscounted, Special, Discount, PLU, TaxExempt,
           Quantity, CreditAccountID, NonDiscountCreditAccountID, TaxTypeID, DiscountTaxTypeID, Priority, SpecialDiscount, SalesTaxRate,
           SaleItemID, OverrideCreditAccountID, POSWindowID
    FROM Mapped
    UNION ALL
    SELECT BatchID, DayNumber, 0, StoreID, 0, 0, 0, Amount, 0, 0, 0, 0, 0,
           0, CreditAccountID, CreditAccountID, TaxTypeID, TaxTypeID, NULL, 0, NULL,
           0, NULL, -4
    FROM Adj
)
GO
-- 2) the copy of the per-store function that calls it
declare @fnId int = object_id(N'dbo.fn_CashAndSalesGLandAR', N'TF');
if @fnId is null
begin
    raiserror(N'This database has no dbo.fn_CashAndSalesGLandAR, so the copy cannot be made.', 16, 1);
    return;
end
declare @def nvarchar(max) = object_definition(@fnId);
if @def is null
begin
    raiserror(N'This login may not read the text of dbo.fn_CashAndSalesGLandAR, so the copy cannot be made here.', 16, 1);
    return;
end

declare @fnWord int = charindex(N'FUNCTION', @def collate Latin1_General_CI_AS);
declare @nameAt int = charindex(N'fn_CashAndSalesGLandAR', @def collate Latin1_General_CI_AS, @fnWord);
declare @callTok nvarchar(60) = N'fn_CashAndSalesMappedRecords(';
declare @calls int = (len(@def) - len(replace(@def collate Latin1_General_CI_AS, @callTok, N''))) / len(@callTok);
if @fnWord = 0 or @nameAt = 0 or @nameAt - @fnWord > 40 or @calls <> 1
begin
    raiserror(N'dbo.fn_CashAndSalesGLandAR is not in the expected shape (one header name, one call to fn_CashAndSalesMappedRecords), so nothing was copied.', 16, 1);
    return;
end
set @def = N'CREATE ' + substring(@def, @fnWord, len(@def));
set @nameAt = charindex(N'fn_CashAndSalesGLandAR', @def collate Latin1_General_CI_AS);
set @def = stuff(@def, @nameAt + len(N'fn_CashAndSalesGLandAR'), 0, N'Blazor');
set @def = replace(@def collate Latin1_General_CI_AS, @callTok, N'fn_CashAndSalesMappedRecordsBlazor(');
if object_id(N'dbo.fn_CashAndSalesGLandARBlazor', N'TF') is not null drop function dbo.fn_CashAndSalesGLandARBlazor;
exec sys.sp_executesql @def;
GO
-- 3) the multi-store Blazor function now calls the copy
declare @mid int = object_id(N'dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', N'IF');
if @mid is null
begin
    raiserror(N'Run script 012 first: dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor does not exist.', 16, 1);
    return;
end
declare @mdef nvarchar(max) = object_definition(@mid);
if @mdef is null
begin
    raiserror(N'This login may not read the text of dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor.', 16, 1);
    return;
end
if charindex(N'dbo.fn_CashAndSalesGLandARBlazor(', @mdef collate Latin1_General_CI_AS) > 0
begin
    print N'The multi-store function already calls the copy.';
    return;
end
declare @tok nvarchar(60) = N'dbo.fn_CashAndSalesGLandAR(';
declare @n int = (len(@mdef) - len(replace(@mdef collate Latin1_General_CI_AS, @tok, N''))) / len(@tok);
if @n <> 1
begin
    raiserror(N'dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor is not in the expected shape (one call to fn_CashAndSalesGLandAR), so it was not changed.', 16, 1);
    return;
end
declare @w int = charindex(N'FUNCTION', @mdef collate Latin1_General_CI_AS);
set @mdef = N'ALTER ' + substring(@mdef, @w, len(@mdef));
set @mdef = replace(@mdef collate Latin1_General_CI_AS, @tok, N'dbo.fn_CashAndSalesGLandARBlazor(');
exec sys.sp_executesql @mdef;
GO
