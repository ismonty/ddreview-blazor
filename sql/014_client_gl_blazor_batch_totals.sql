-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again. Needs scripts 012 and 013.
-- Stage 3. The per-statement profile of the new GL function showed ONE statement costing almost all the time:
--     update @output_table set AROffsetBalance=@AROffsetBalance, TotalSales=@TotalSales, PercentOfTotalSales=... where BatchID=@BatchID
-- run once per batch of EVERY store in the date range (the cursor above it has no StoreID filter): 209,596 runs, each scanning the table.
-- This script rebuilds dbo.fn_CashAndSalesGLandARBlazor (the copy made by 013) from this database's own dbo.fn_CashAndSalesGLandAR with two changes:
--   1) it calls dbo.fn_CashAndSalesMappedRecordsBlazor (as in 013). (A recompile hint on that statement was tried and removed: planning it on
--      every store call cost more than it saved; see script 015.)
--   2) the batch loop becomes one UPDATE with the same arithmetic (a batch with no A/R offset line and no total-sales line is left alone, as before)
-- The original dbo.fn_CashAndSalesGLandAR is not touched.
set nocount on;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
declare @fnId int = object_id(N'dbo.fn_CashAndSalesGLandAR', N'TF');
if @fnId is null or object_id(N'dbo.fn_CashAndSalesMappedRecordsBlazor', N'IF') is null
begin
    raiserror(N'Needs dbo.fn_CashAndSalesGLandAR and script 013 (dbo.fn_CashAndSalesMappedRecordsBlazor).', 16, 1);
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
declare @startTok nvarchar(60) = N'declare @AROffsetBalance money';
declare @endTok nvarchar(80) = N'update @output_table set SuspenseFlag';
declare @s int = charindex(@startTok, @def collate Latin1_General_CI_AS);
declare @e int = charindex(@endTok, @def collate Latin1_General_CI_AS);
declare @starts int = (len(@def) - len(replace(@def collate Latin1_General_CI_AS, @startTok, N''))) / len(@startTok);
declare @ends int = (len(@def) - len(replace(@def collate Latin1_General_CI_AS, @endTok, N''))) / len(@endTok);
if @fnWord = 0 or @nameAt = 0 or @nameAt - @fnWord > 40 or @calls <> 1 or @starts <> 1 or @ends <> 1 or @s = 0 or @e <= @s
begin
    raiserror(N'dbo.fn_CashAndSalesGLandAR is not in the expected shape (header name, one mapping call, one batch-totals block before the SuspenseFlag update), so nothing was changed.', 16, 1);
    return;
end

-- 2) the batch loop -> one set-based update
declare @block nvarchar(max) = N'-- PATCH: batch totals set-based (replaces the per-batch update loop)
	update ot
		set AROffsetBalance = agg.AROffsetBalance,
			TotalSales = agg.TotalSales,
			PercentOfTotalSales = case when agg.TotalSales <> 0 then 100.0 * (isnull(ot.Credit, 0) - isnull(ot.Debit, 0)) / agg.TotalSales else null end
		from @output_table ot
		inner join (
			select BatchID,
				sum(case when AROffsetLine = 1 then isnull(Debit, 0) - isnull(Credit, 0) else 0 end) as AROffsetBalance,
				sum(case when TotalSalesAccount = 1 then isnull(Credit, 0) - isnull(Debit, 0) else 0 end) as TotalSales
			from @output_table
			where AROffsetLine = 1 or TotalSalesAccount = 1
			group by BatchID
			) agg
		on agg.BatchID = ot.BatchID
	';
declare @body nvarchar(max) = stuff(@def, @s, @e - @s, @block);

-- name, mapping call
set @body = N'ALTER ' + substring(@body, @fnWord, len(@body));
set @nameAt = charindex(N'fn_CashAndSalesGLandAR', @body collate Latin1_General_CI_AS);
set @body = stuff(@body, @nameAt + len(N'fn_CashAndSalesGLandAR'), 0, N'Blazor');
set @body = replace(@body collate Latin1_General_CI_AS, @callTok, N'fn_CashAndSalesMappedRecordsBlazor(');

if object_id(N'dbo.fn_CashAndSalesGLandARBlazor', N'TF') is null
    set @body = N'CREATE ' + substring(@body, len(N'ALTER ') + 1, len(@body));   -- first time: create from the same text
exec sys.sp_executesql @body;
GO
-- the multi-store function must (still) call the copy: nothing to do if 013 already did that
if object_id(N'dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', N'IF') is not null
   and charindex(N'dbo.fn_CashAndSalesGLandARBlazor(', object_definition(object_id(N'dbo.fn_CashAndSalesGLandARMultiStoreSelectBlazor', N'IF'))) = 0
    raiserror(N'Run script 013 as well: the multi-store function does not call dbo.fn_CashAndSalesGLandARBlazor yet.', 16, 1);
GO
