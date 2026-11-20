-- NOT part of the Database Updater. Run in SSMS on a client database AFTER script 012, to prove the new function matches the old one.
-- Edit the four variables, run it, and read the two counts at the end: both must be 0. Repeat for different stores / dates / flags
-- (a single store, several stores, an empty list = all active, a month with Adjust GL lines, suspense, A/R flags 2 and 19, @tax 0 and 1),
-- and once with tblGlobal.CombineGeneralLedgerBatches = 1 and once with 0 (put it back afterwards).
set nocount on;
declare @Stores varchar(8000) = '2', @Start datetime = '09/01/2010', @End datetime = '09/30/2010', @Ar int = 19, @Tax bit = 0;

if object_id('tempdb..#old') is not null drop table #old;
if object_id('tempdb..#new') is not null drop table #new;

set statistics time on;
select * into #old from dbo.fn_CashAndSalesGLandARMultiStoreSelect(@Stores, @Start, @End, @Ar, @Tax);
select * into #new from dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor(@Stores, @Start, @End, @Ar, @Tax) option (recompile);
set statistics time off;

alter table #new drop column RowSeq;   -- the only column the old function does not have

select 'old rows' as Which, count(*) as N from #old union all select 'new rows', count(*) from #new;

select 'in old, not in new' as Problem, * from (select * from #old except select * from #new) a;
select 'in new, not in old' as Problem, * from (select * from #new except select * from #old) b;

select (select count(*) from (select * from #old except select * from #new) a) as OldNotInNew,
       (select count(*) from (select * from #new except select * from #old) b) as NewNotInOld;
-- Row ORDER is checked separately by the app (order by RowSeq); EXCEPT compares the rows themselves, duplicates collapsed,
-- so the row counts above must also match.

-- ---- Stage 2 (after script 013): the pieces on their own, for one store. Both counts in each pair must be 0.
-- declare @s int = <a store id>, @a datetime = '01/01/2026', @b datetime = '01/31/2026';
-- select (select count(*) from (select * from dbo.fn_CashAndSalesMappedRecords(@s,@a,@b,3) except select * from dbo.fn_CashAndSalesMappedRecordsBlazor(@s,@a,@b,3)) x) as MapOldNotNew,
--        (select count(*) from (select * from dbo.fn_CashAndSalesMappedRecordsBlazor(@s,@a,@b,3) except select * from dbo.fn_CashAndSalesMappedRecords(@s,@a,@b,3)) y) as MapNewNotOld;
-- select (select count(*) from (select * from dbo.fn_CashAndSalesGLandAR(@s,@a,@b,3,0) except select * from dbo.fn_CashAndSalesGLandARBlazor(@s,@a,@b,3,0)) x) as GlOldNotNew,
--        (select count(*) from (select * from dbo.fn_CashAndSalesGLandARBlazor(@s,@a,@b,3,0) except select * from dbo.fn_CashAndSalesGLandAR(@s,@a,@b,3,0)) y) as GlNewNotOld;
-- set statistics time on; select count(*) from dbo.fn_CashAndSalesMappedRecordsBlazor(@s,@a,@b,3); select count(*) from dbo.fn_CashAndSalesGLandARBlazor(@s,@a,@b,3,0); set statistics time off;
