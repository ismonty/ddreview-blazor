-- Run on each CLIENT database (or let Admin > Database updater add it). Safe to run again. SQL Server 2012 and later.
-- Adds one stored procedure, dbo.usp_DDReviewGrantAccess, that holds EVERY permission the web login needs in a client database
-- (today: execute on the dbo.DrillPair table type). Adding it changes no permissions by itself. You launch it, as someone who may grant:
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
