-- NOT part of the Database Updater. Optional, run once in SSMS on a client database (safe to repeat). Helps the GL functions, old and new.
-- 1) Indexes. Batches (StoreID, PostingDate) and Receipts (BatchID) already have indexes in this database, so only Adjust GL lacks one.
if not exists (select 1 from sys.indexes where name = 'IX_DDR_GLAdjust_BatchID' and object_id = object_id('dbo.tblCashAndSalesGeneralLedgerAdjustments'))
    create index IX_DDR_GLAdjust_BatchID on dbo.tblCashAndSalesGeneralLedgerAdjustments (BatchID);
GO
-- 2) Fresh statistics (the database is in SIMPLE recovery, compatibility 110: statistics are the main thing the optimizer has to go on).
update statistics dbo.tblCashAndSalesBatches with fullscan;
update statistics dbo.tblCashAndSalesReceipts with fullscan;
update statistics dbo.tblCashAndSalesGeneralLedgerAdjustments with fullscan;
update statistics dbo.tblStores with fullscan;
exec sp_updatestats;
GO
-- 3) Timing: run each with "Include Actual Execution Plan" off, compare Elapsed time and logical reads in the Messages tab.
-- set statistics time on; set statistics io on;
-- select count(*) from dbo.fn_CashAndSalesGLandARMultiStoreSelect('', '01/01/2010', '12/31/2010', 19, 0);
-- select count(*) from dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor('', '01/01/2010', '12/31/2010', 19, 0) option (recompile);
