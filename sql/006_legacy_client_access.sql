-- Run on each CLIENT database, test copy first. Safe to run again. SQL Server 2012 and later.
-- Adds dbo.usp_DDReviewLegacyGrantClient: the permissions the OLD site's login needs in a client database. Nothing is granted until you launch it:
--
--   -- once per database (the server login comes from sql/005's instructions; each client database needs its own user):
--   --   create user [ddreview_classic] for login [ddreview_classic];
--   exec dbo.usp_DDReviewLegacyGrantClient @UserName = N'ddreview_classic';                 -- lists
--   exec dbo.usp_DDReviewLegacyGrantClient @UserName = N'ddreview_classic', @Execute = 1;   -- does it
--
-- The old report pages build their SQL as text, so they need to read most tables: the login gets READ access (db_datareader) and execute on the
-- few procedures the login path calls. It gets no write access and no DDL, and it is explicitly denied writes (deny beats any later grant by mistake).
-- Known gap: the old code tries to "self-update" the database (ALTER TABLE for new columns in CheckUserDatabaseForNewFields, function updaters in
-- SQLFunctionUpdaters.cs). Those calls are wrapped in try/catch and will quietly fail under this login; do those updates as an administrator
-- (the new site's Database updater page is the supported route). If an old page shows a "permission denied" error, add its procedure to the list below.
if object_id(N'dbo.usp_DDReviewLegacyGrantClient', N'P') is not null
    drop procedure dbo.usp_DDReviewLegacyGrantClient;
go
create procedure dbo.usp_DDReviewLegacyGrantClient
    @UserName sysname = null,
    @Execute  bit     = 0
as
begin
    set nocount on;
    if @UserName is null or not exists (select 1 from sys.database_principals where name = @UserName and type in ('S', 'U', 'G'))
    begin
        raiserror(N'Give @UserName = an existing database user for the old site''s login in this database.', 16, 1);
        return;
    end

    declare @q nvarchar(300) = quotename(@UserName);
    declare @todo table (Seq int identity(1, 1) primary key, Stmt nvarchar(max) not null);

    -- ===== THE LIST =====
    insert @todo (Stmt) values (N'alter role db_datareader add member ' + @q + N';');
    insert @todo (Stmt) values (N'alter role db_denydatawriter add member ' + @q + N';');
    insert @todo (Stmt)
    select N'grant execute on ' + quotename(schema_name(schema_id)) + N'.' + quotename(name) + N' to ' + @q + N';'
    from sys.procedures where schema_id = schema_id(N'dbo') and name in (N'cs_spCheckDDReviewQueryExecutionCounter', N'cs_spGlobalSettingsGetNoValue');
    insert @todo (Stmt)
    select N'grant execute on ' + quotename(schema_name(schema_id)) + N'.' + quotename(name) + N' to ' + @q + N';'
    from sys.objects where schema_id = schema_id(N'dbo') and type = 'FN';   -- scalar helper functions the generated SQL calls (table functions are covered by db_datareader)
    -- ====================

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
