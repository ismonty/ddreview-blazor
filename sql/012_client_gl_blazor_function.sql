-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again (it ALTERs or CREATEs).
-- Adds dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor: the same result as dbo.fn_CashAndSalesGLandARMultiStoreSelect, written as ONE set-based query.
--   * no cursors, no table variables, no UPDATE passes (an inline table-valued function, so the optimizer sees the whole thing)
--   * the store list is split inline (XML), so it does not call the one-integer-at-a-time splitter
--   * the per-store function dbo.fn_CashAndSalesGLandAR is still called, once per store, through CROSS APPLY (stage 1; it is rewritten later)
--   * TotalDebit, TotalCredit, TotalSales (combined mode), DistributionCount and the QuickBooks type fill are window functions / joins
-- Nothing existing is changed. The old function stays as the fallback. One extra trailing column, RowSeq, carries the legacy sort order:
-- an inline function cannot promise an order, so the caller does "order by RowSeq".
-- Type fill order (identical to the old function plus script 011): the line's own type, then the Adjust GL line's type, then the account's
-- type (lowest across stores), then Cost Of Goods Sold (debit) / Expense, and none at all on suspense lines.
-- Needs compatibility level 110 or lower-compatible features only (no STRING_SPLIT, no batch mode).
set nocount on;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF OBJECT_ID(N'dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', N'IF') IS NULL
    EXEC (N'CREATE FUNCTION dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor() RETURNS TABLE AS RETURN SELECT 1 AS Placeholder');
GO
ALTER FUNCTION dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor
    (@StoreListCSV varchar(8000), @StartDate datetime, @EndDate datetime, @ARFlag int, @SalesTaxFlag bit)
RETURNS TABLE
AS
RETURN
(
    /* select * from dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor('2','09/17/2010','09/17/2010',19,0) order by RowSeq */
    WITH Settings AS (
        SELECT g.SalesTaxBreakoutMode, g.OutputCashAndSalesByAccountClass, g.CombineGeneralLedgerBatches AS CombineBatches
        FROM (SELECT 1 AS One) d
        LEFT JOIN (SELECT TOP 1 SalesTaxBreakoutMode, OutputCashAndSalesByAccountClass, CombineGeneralLedgerBatches FROM dbo.tblGlobal) g ON 1 = 1
    ),
    StoreList AS (
        -- a list: every number in it; no list: every active store. Anything that is not a number is ignored.
        SELECT DISTINCT TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) AS StoreID
        FROM (SELECT CAST('<i>' + REPLACE(REPLACE(REPLACE(@StoreListCSV, ' ', ''), ';', ','), ',', '</i><i>') + '</i>' AS xml) AS doc) s
        CROSS APPLY s.doc.nodes('/i') x(i)
        WHERE LEN(ISNULL(@StoreListCSV, '')) > 0 AND TRY_CONVERT(int, x.i.value('.', 'varchar(20)')) IS NOT NULL
        UNION
        SELECT st.StoreID FROM dbo.tblStores st WITH (NOLOCK) WHERE st.Active = 1 AND LEN(ISNULL(@StoreListCSV, '')) = 0
    ),
    Src AS (
        -- stage 1: the per-store function, one call per store. FirstOfBatch marks one row per batch so TotalSales is counted once per batch.
        SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.BatchID ORDER BY (SELECT NULL)) AS BatchRn
        FROM StoreList sl
        CROSS APPLY dbo.fn_CashAndSalesGLandAR(sl.StoreID, @StartDate, @EndDate, @ARFlag, @SalesTaxFlag) t
    ),
    Keyed AS (
        SELECT
            CASE WHEN st.CombineBatches = 1 THEN YEAR(tb.PostingDate) * 10000 + MONTH(tb.PostingDate) * 100 + DAY(tb.PostingDate) ELSE csb.BatchID END AS BatchKey,
            tb.PostingDate, tb.Account, tb.ARAccount, tb.AccountClassID, tb.Debit, tb.Credit, tb.DistReference, tb.Sorter, tb.Suspense,
            tb.BankRecCheckBookID, tb.BankRecType, tb.BankRecOption, tb.BankRecAccount, tb.BankRecAmount, tb.BankRecDebit, tb.BankRecCredit,
            tb.TaxTypeID, tb.ItemName, tb.CheckNumber, tb.TransactionNumber, tb.DebitCreditFlag,
            CASE WHEN st.SalesTaxBreakoutMode = 1 OR @SalesTaxFlag = 1 THEN CAST(tb.TaxSalesAmount AS decimal(19, 8)) ELSE 0 END AS TaxSalesAmount,
            CASE WHEN st.SalesTaxBreakoutMode = 1 OR @SalesTaxFlag = 1 THEN CAST(tb.TaxRate AS decimal(19, 8)) ELSE NULL END AS TaxRateKey,
            CASE WHEN @SalesTaxFlag = 1 THEN tb.StoreID ELSE NULL END AS StoreIDKey,
            tb.ExportFlag, ac.AccountClass, tb.AROffsetLine, tb.AROffsetBalance,
            CASE WHEN st.CombineBatches = 0 THEN s.StoreNumber ELSE NULL END AS StoreNumberKey,
            CASE WHEN st.CombineBatches = 0 THEN s.StoreName ELSE NULL END AS StoreNameKey,
            tb.DetailInvoiceMessage, tb.DuringPeriodInvoiceMessage, tb.EndOfPeriodInvoiceMessage, tb.DefaultQuickBooksType, tb.TotalSalesAccount,
            CASE WHEN st.CombineBatches = 1 THEN NULL ELSE tb.TotalSales END AS TotalSalesKey,
            CASE WHEN st.CombineBatches = 1 THEN NULL ELSE tb.PercentOfTotalSales END AS PercentKey,
            tb.SuspenseFlag,
            CASE WHEN tb.BatchRn = 1 THEN tb.TotalSales END AS BatchSalesOnce,
            st.CombineBatches, st.OutputCashAndSalesByAccountClass
        FROM Src tb
        CROSS JOIN Settings st
        INNER JOIN dbo.tblCashAndSalesBatches csb WITH (NOLOCK) ON tb.BatchID = csb.BatchID
        INNER JOIN dbo.tblStores s WITH (NOLOCK) ON csb.StoreID = s.StoreID
        LEFT JOIN dbo.tblAccountsClasses ac WITH (NOLOCK) ON tb.AccountClassID = ac.AccountClassID
    ),
    Grouped AS (
        SELECT
            BatchKey AS BatchID, PostingDate, Account, ARAccount, AccountClassID,
            SUM(Debit) AS Debit, SUM(Credit) AS Credit, DistReference, MIN(Sorter) AS Sorter, Suspense,
            BankRecCheckBookID, BankRecType, BankRecOption, BankRecAccount,
            SUM(BankRecAmount) AS BankRecAmount, SUM(BankRecDebit) AS BankRecDebit, SUM(BankRecCredit) AS BankRecCredit,
            TaxTypeID, ItemName, CheckNumber, TransactionNumber, DebitCreditFlag,
            SUM(TaxSalesAmount) AS TaxSalesAmount, TaxRateKey AS TaxRate, StoreIDKey AS StoreID, ExportFlag, AccountClass, AROffsetLine, AROffsetBalance,
            StoreNumberKey AS StoreNumber, StoreNameKey AS StoreName,
            DetailInvoiceMessage, DuringPeriodInvoiceMessage, EndOfPeriodInvoiceMessage, DefaultQuickBooksType, TotalSalesAccount,
            TotalSalesKey AS TotalSales, PercentKey AS PercentOfTotalSales, SuspenseFlag,
            SUM(BatchSalesOnce) AS SalesPart, CombineBatches, OutputCashAndSalesByAccountClass
        FROM Keyed
        GROUP BY BatchKey, PostingDate, Account, ARAccount, AccountClassID, DistReference, Suspense,
            BankRecCheckBookID, BankRecType, BankRecOption, BankRecAccount, TaxTypeID, ItemName, CheckNumber, TransactionNumber, AccountClass,
            DebitCreditFlag, ExportFlag, TaxRateKey, StoreIDKey, AROffsetLine, AROffsetBalance, StoreNumberKey, StoreNameKey,
            DetailInvoiceMessage, DuringPeriodInvoiceMessage, EndOfPeriodInvoiceMessage, DefaultQuickBooksType, TotalSalesAccount,
            TotalSalesKey, PercentKey, SuspenseFlag, CombineBatches, OutputCashAndSalesByAccountClass
    ),
    Netted AS (
        SELECT g.*,
            CASE WHEN ISNULL(g.Debit, 0) > ISNULL(g.Credit, 0) THEN ISNULL(g.Debit, 0) - ISNULL(g.Credit, 0) ELSE NULL END AS NetDebit,
            CASE WHEN ISNULL(g.Debit, 0) < ISNULL(g.Credit, 0) THEN ISNULL(g.Credit, 0) - ISNULL(g.Debit, 0) ELSE NULL END AS NetCredit,
            SUM(g.SalesPart) OVER () AS AllSales
        FROM Grouped g
    ),
    Typed AS (
        SELECT n.*,
            -- the line's own type, then the Adjust GL line's, then the account's, then the debit/credit default (only when there is an account)
            COALESCE(n.DefaultQuickBooksType, adj.DefaultAccountType, acct.DefaultQuickBooksType,
                     CASE WHEN n.Account IS NOT NULL THEN CASE WHEN n.DebitCreditFlag > 0 THEN 'Cost Of Goods Sold' ELSE 'Expense' END END) AS FilledType
        FROM Netted n
        LEFT JOIN (
            SELECT BatchID, Account, ISNULL(DistReference, '') AS DistReference, MIN(DefaultAccountType) AS DefaultAccountType
            FROM dbo.tblCashAndSalesGeneralLedgerAdjustments
            WHERE ISNULL(DefaultAccountType, '') <> '' AND DefaultAccountType <> '0'
            GROUP BY BatchID, Account, ISNULL(DistReference, '')
        ) adj ON n.DefaultQuickBooksType IS NULL AND adj.BatchID = n.BatchID AND adj.Account = n.Account AND adj.DistReference = ISNULL(n.DistReference, '')
        LEFT JOIN (
            SELECT Account, MIN(DefaultQuickBooksType) AS DefaultQuickBooksType
            FROM dbo.fn_CashAndSalesGLandARPostingAccountsMultiStoreSelect(@StoreListCSV)
            GROUP BY Account
        ) acct ON n.DefaultQuickBooksType IS NULL AND acct.Account = n.Account
    )
    SELECT
        t.BatchID, t.PostingDate, t.Account, t.ARAccount, t.AccountClassID,
        CAST(t.NetDebit AS money) AS Debit, CAST(t.NetCredit AS money) AS Credit,
        t.DistReference, t.Sorter, t.Suspense, t.BankRecCheckBookID, t.BankRecType, t.BankRecOption, t.BankRecAccount,
        t.BankRecAmount, t.BankRecDebit, t.BankRecCredit, t.TaxTypeID, t.ItemName, t.CheckNumber, t.TransactionNumber,
        CAST(SUM(t.NetDebit) OVER () AS money) AS TotalDebit,
        CAST(SUM(t.NetCredit) OVER () AS money) AS TotalCredit,
        t.DebitCreditFlag, CAST(t.TaxSalesAmount AS money) AS TaxSalesAmount, t.TaxRate, t.StoreID, t.ExportFlag,
        NULLIF(COUNT(t.FilledType) OVER (PARTITION BY t.PostingDate), 0) AS DistributionCount,
        t.AccountClass, t.AROffsetLine, t.AROffsetBalance, t.StoreNumber, t.StoreName,
        t.DetailInvoiceMessage, t.DuringPeriodInvoiceMessage, t.EndOfPeriodInvoiceMessage,
        CASE WHEN t.Suspense <> 0 THEN NULL ELSE t.FilledType END AS DefaultQuickBooksType,
        t.TotalSalesAccount,
        CASE WHEN t.CombineBatches = 1 THEN CAST(t.AllSales AS money) ELSE t.TotalSales END AS TotalSales,
        CASE WHEN t.CombineBatches = 1
             THEN CASE WHEN t.AllSales <> 0 THEN (ISNULL(t.NetCredit, 0) - ISNULL(t.NetDebit, 0)) / t.AllSales ELSE NULL END
             ELSE t.PercentOfTotalSales END AS PercentOfTotalSales,
        t.SuspenseFlag,
        ROW_NUMBER() OVER (ORDER BY t.PostingDate, t.ARAccount, t.CheckNumber, t.AccountClass,
                                    CASE WHEN t.OutputCashAndSalesByAccountClass = 1 THEN t.AccountClass END,
                                    t.TransactionNumber, t.DebitCreditFlag DESC, t.Account,
                                    CASE WHEN t.OutputCashAndSalesByAccountClass = 0 THEN t.AccountClass END) AS RowSeq
    FROM Typed t
)
GO
