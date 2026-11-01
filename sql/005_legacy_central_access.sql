-- Run on the CENTRAL master (DDReview-CentralMaster), test copy first. Safe to run again. SQL Server 2012 and later.
-- Adds dbo.usp_DDReviewLegacyGrantCentral: every permission the OLD site's own SQL login needs on the central master, in one place.
-- Nothing is granted until you launch it (same pattern as sql/004):
--
--   -- once, at server level (pick your own long random password; do not reuse the old shared login and never put it in git):
--   --   create login [ddreview_classic] with password = N'...', default_database = [DDReview-CentralMaster], check_policy = on;
--   --   use [DDReview-CentralMaster]; create user [ddreview_classic] for login [ddreview_classic];
--   exec dbo.usp_DDReviewLegacyGrantCentral @UserName = N'ddreview_classic';                 -- lists what it WOULD grant
--   exec dbo.usp_DDReviewLegacyGrantCentral @UserName = N'ddreview_classic', @Execute = 1;   -- does it
--
-- What the old site does here (read from the source): the login lookup (dbo.fn_CentralSecurityLogin, tblWebUserSecurity), the login log
-- procedures, and the phone-browser check. It needs no INSERT/UPDATE/DELETE on any table, no DDL, and no access to the new site's tables
-- (tblWebUserCredential, tblWebUserGoogle). Objects that do not exist in your copy are skipped and listed.
if object_id(N'dbo.usp_DDReviewLegacyGrantCentral', N'P') is not null
    drop procedure dbo.usp_DDReviewLegacyGrantCentral;
go
create procedure dbo.usp_DDReviewLegacyGrantCentral
    @UserName sysname = null,
    @Execute  bit     = 0
as
begin
    set nocount on;
    if @UserName is null or not exists (select 1 from sys.database_principals where name = @UserName and type in ('S', 'U', 'G'))
    begin
        raiserror(N'Give @UserName = an existing database user for the old site''s login.', 16, 1);
        return;
    end

    declare @q nvarchar(300) = quotename(@UserName);
    declare @todo table (Seq int identity(1, 1) primary key, Stmt nvarchar(max) not null);

    -- ===== THE LIST: object names the old site uses on the central master =====
    declare @names table (Name sysname primary key);
    insert @names values (N'fn_CentralSecurityLogin'), (N'tblWebUserSecurity'), (N'tblWebUserSecurityStores'),
        (N'cs_spLoginLogAdd'), (N'cs_spLoginLogAddWithBrowserString'), (N'cs_spLoginLogAddWithRequestDetails2'),
        (N'fn_BrowserStringIndicatesPhone');
    -- ===========================================================================

    insert @todo (Stmt)
    select case when o.type in ('U', 'V', 'IF', 'TF') then N'grant select on ' else N'grant execute on ' end
           + quotename(schema_name(o.schema_id)) + N'.' + quotename(o.name) + N' to ' + @q + N';'
    from @names n join sys.objects o on o.name = n.Name and o.schema_id = schema_id(N'dbo')
    where o.type in ('U', 'V', 'IF', 'TF', 'FN', 'P')
    order by o.name;

    insert @todo (Stmt)
    select N'-- missing in this database, skipped: ' + n.Name
    from @names n where not exists (select 1 from sys.objects o where o.name = n.Name and o.schema_id = schema_id(N'dbo'));

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
        if left(@stmt, 2) = N'--' begin print @stmt; continue; end
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
