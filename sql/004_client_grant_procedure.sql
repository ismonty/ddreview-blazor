-- Run on each CLIENT database (or let Admin > Database updater add it). Safe to run again. SQL Server 2012 and later.
-- Adds one stored procedure, dbo.usp_DDReviewGrantAccess, that holds EVERY permission the web login needs in a client database
-- (execute on the dbo.DrillPair table type, and the Setup pages' access and audit tables). Adding it changes no permissions by itself. You launch it, as someone who may grant:
--
--     exec dbo.usp_DDReviewGrantAccess @UserName = N'the web login user';                   -- lists what it WOULD grant, changes nothing
--     exec dbo.usp_DDReviewGrantAccess @UserName = N'the web login user', @Execute = 1;     -- does it
--
-- When a later script needs another permission, add its line to the list inside the procedure (marked below) and re-run this script.
if object_id(N'dbo.usp_DDReviewGrantAccess', N'P') is not null
    drop procedure dbo.usp_DDReviewGrantAccess;
go
create procedure dbo.usp_DDReviewGrantAccess
    @UserName sysname = null,   -- the database user the web login maps to (the Database updater page shows it)
    @Execute  bit     = 0       -- 0 = only list the statements; 1 = run them
as
begin
    set nocount on;

    if @UserName is null or not exists (select 1 from sys.database_principals where name = @UserName and type in ('S', 'U', 'G', 'R'))
    begin
        raiserror(N'Give @UserName = an existing database user (the web login''s user in this database).', 16, 1);
        return;
    end

    declare @q nvarchar(300) = quotename(@UserName);
    declare @todo table (Seq int identity(1, 1) primary key, Stmt nvarchar(max) not null);

    -- ===== THE LIST: one line per permission the web login needs. Add new ones here. =====
    if type_id(N'dbo.DrillPair') is not null
        insert @todo (Stmt) values (N'grant execute on type::dbo.DrillPair to ' + @q + N';');
    -- Setup pages (sql/007): the web login may read and write who is allowed in, but only add to the audit trail.
    if object_id(N'dbo.tblDDReviewAdminAccess', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, update on dbo.tblDDReviewAdminAccess to ' + @q + N';');
    if object_id(N'dbo.tblDDReviewAdminAudit', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert on dbo.tblDDReviewAdminAudit to ' + @q + N';');
    -- General Ledger Review > Request Batch Resubmission (sql/010).
    if object_id(N'dbo.usp_DDReviewRequestBatchResubmission', N'P') is not null
        insert @todo (Stmt) values (N'grant execute on object::dbo.usp_DDReviewRequestBatchResubmission to ' + @q + N';');
    if object_id(N'dbo.tblWebUserFeatures', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, delete on dbo.tblWebUserFeatures to ' + @q + N';');
    if object_id(N'dbo.cs_spConsolidatedTipOut3Crunch', N'P') is not null
        insert @todo (Stmt) values (N'grant execute on dbo.cs_spConsolidatedTipOut3Crunch to ' + @q + N';');
    if object_id(N'dbo.cs_spGlobalSettingsSet', N'P') is not null
        insert @todo (Stmt) values (N'grant execute on dbo.cs_spGlobalSettingsSet to ' + @q + N';');
    if object_id(N'dbo.cs_spConsolidatedTipOut3Crunch2', N'P') is not null
        insert @todo (Stmt) values (N'grant execute on dbo.cs_spConsolidatedTipOut3Crunch2 to ' + @q + N';');
    if object_id(N'dbo.cs_spConsolidatedTipOut3CrunchComplianceLetters2', N'P') is not null
        insert @todo (Stmt) values (N'grant execute on dbo.cs_spConsolidatedTipOut3CrunchComplianceLetters2 to ' + @q + N';');
    if object_id(N'dbo.fn_ConsolidatedTipOut32', N'TF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_ConsolidatedTipOut32 to ' + @q + N';');
    if object_id(N'dbo.fn_ConsolidatedTipOutDateRangeWithYearToDate32', N'TF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_ConsolidatedTipOutDateRangeWithYearToDate32 to ' + @q + N';');
    if object_id(N'dbo.fn_ConsolidatedTipOutTotalAdjustments', N'TF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_ConsolidatedTipOutTotalAdjustments to ' + @q + N';');
    if object_id(N'dbo.fn_ConsolidatedTipOutADPExport3', N'TF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_ConsolidatedTipOutADPExport3 to ' + @q + N';');
    if object_id(N'dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, update, delete on dbo.tblCashAndSalesTimeEntriesConsolidatedTipOutAdjustments to ' + @q + N';');
    if object_id(N'dbo.fn_TipsByEmployee', N'IF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_TipsByEmployee to ' + @q + N';');
    if object_id(N'dbo.fn_CustomReport_TipsExportNew', N'TF') is not null
        insert @todo (Stmt) values (N'grant select on dbo.fn_CustomReport_TipsExportNew to ' + @q + N';');
    if object_id(N'dbo.tblTipPayrollSettings', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, update, delete on dbo.tblTipPayrollSettings to ' + @q + N';');
    if object_id(N'dbo.tblTipPayrollExcludedDepartments', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, update, delete on dbo.tblTipPayrollExcludedDepartments to ' + @q + N';');
    if object_id(N'dbo.tblTipPayrollReceiptTypeFee', N'U') is not null
        insert @todo (Stmt) values (N'grant select, insert, update, delete on dbo.tblTipPayrollReceiptTypeFee to ' + @q + N';');
    -- custom report exports: the index function and every report function it lists (a name that is not a plain dbo function is skipped)
    if object_id(N'dbo.fn_CustomReports_Index') is not null
    begin
        insert @todo (Stmt) values (N'grant select on dbo.fn_CustomReports_Index to ' + @q + N';');
        -- dynamic, so this procedure still compiles on a database whose index function is not there or is shaped differently
        declare @list nvarchar(max);
        exec sys.sp_executesql
            N'select @s = isnull(@s, N'''') + N''grant select on dbo.'' + quotename(o.name) + N'' to '' + @qq + N'';''
              from dbo.fn_CustomReports_Index() i
              join sys.objects o on o.schema_id = schema_id(N''dbo'') and o.type in (''TF'', ''IF'')
               and o.name = replace(replace(replace(i.CustomReportSQLFunctionName, N''['', N''''), N'']'', N''''), N''dbo.'', N'''')',
            N'@s nvarchar(max) output, @qq nvarchar(300)', @s = @list output, @qq = @q;
        if @list is not null insert @todo (Stmt) values (@list);
    end
    -- insert @todo (Stmt) values (N'grant select on dbo.SomeTable to ' + @q + N';');
    -- =====================================================================================

    if @Execute = 0
    begin
        select Stmt as WouldRun from @todo order by Seq;
        print N'Nothing was changed. Add  @Execute = 1  to run these.';
        return;
    end

    declare @seq int = 0, @stmt nvarchar(max);
    while 1 = 1
    begin
        select top (1) @seq = Seq, @stmt = Stmt from @todo where Seq > @seq order by Seq;
        if @@rowcount = 0 break;
        begin try
            exec sys.sp_executesql @stmt;
            print N'ok:     ' + @stmt;
        end try
        begin catch
            print N'FAILED: ' + @stmt + N'  -- ' + error_message();
        end catch
    end
end
go
