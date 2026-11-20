-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again; it does nothing when already applied.
-- Changes ONE existing function, dbo.fn_CashAndSalesGLandARMultiStoreSelect: an Adjust GL line keeps the QuickBooks type chosen on it
-- (tblCashAndSalesGeneralLedgerAdjustments.DefaultAccountType). Before, those lines arrived with no type and were given "Cost Of Goods Sold"
-- (debit) or "Expense" by the function's last step; now the type on the line is used first, then the account's type, then that default.
--
-- The patch is made on THIS database's own copy of the function (its text is read, one block is added before the step that fills in the types,
-- and it is altered), so a database whose function differs a little from another's keeps its own differences. If the place to add the block
-- cannot be found exactly once, nothing is changed and an error says so. To undo: remove the block marked PATCH from the function and ALTER it again.
set nocount on;

declare @marker nvarchar(100) = N'-- PATCH: an Adjust GL line carries its own QuickBooks type';
declare @anchor nvarchar(100) = N'declare @Account varchar(200)';
declare @fnId int = object_id(N'dbo.fn_CashAndSalesGLandARMultiStoreSelect', N'TF');

if @fnId is null
begin
    raiserror(N'This database has no dbo.fn_CashAndSalesGLandARMultiStoreSelect, so there is nothing to patch.', 16, 1);
    return;
end

declare @def nvarchar(max) = object_definition(@fnId);
if @def is null
begin
    raiserror(N'This login may not read the text of dbo.fn_CashAndSalesGLandARMultiStoreSelect, so it cannot be patched here.', 16, 1);
    return;
end
if charindex(@marker, @def) > 0
begin
    print N'Already applied.';
    return;
end

declare @at int = charindex(@anchor, @def);
if @at = 0 or charindex(@anchor, @def, @at + 1) > 0
begin
    raiserror(N'The place to add the patch was not found exactly once in dbo.fn_CashAndSalesGLandARMultiStoreSelect, so nothing was changed.', 16, 1);
    return;
end

declare @fn int = charindex(N'function', @def collate Latin1_General_CI_AS);
declare @nl nvarchar(2) = char(13) + char(10);
declare @block nvarchar(max) =
      @marker + N' (tblCashAndSalesGeneralLedgerAdjustments.DefaultAccountType).' + @nl
    + N'	-- It is used first, ahead of the account''s type below and ahead of the "Cost Of Goods Sold / Expense" fallback.' + @nl
    + N'	update r' + @nl
    + N'		set DefaultQuickBooksType = adj.DefaultAccountType' + @nl
    + N'		from @return_table r' + @nl
    + N'		inner join (' + @nl
    + N'			select BatchID, Account, DistReference, min(DefaultAccountType) as DefaultAccountType' + @nl
    + N'			from dbo.tblCashAndSalesGeneralLedgerAdjustments' + @nl
    + N'			where isnull(DefaultAccountType, '''') <> '''' and DefaultAccountType <> ''0''' + @nl
    + N'			group by BatchID, Account, DistReference' + @nl
    + N'			) adj' + @nl
    + N'		on adj.BatchID = r.BatchID and adj.Account = r.Account and isnull(adj.DistReference, '''') = isnull(r.DistReference, '''')' + @nl
    + N'		where r.DefaultQuickBooksType is null' + @nl
    + N'	';

set @def = N'ALTER ' + substring(@def, @fn, @at - @fn) + @block + substring(@def, @at, len(@def));
exec sys.sp_executesql @def;
print N'Applied.';
