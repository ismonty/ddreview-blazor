-- Run on each CLIENT database (or let Admin > Database updater add it). Safe to run again. SQL Server 2012 and later.
-- Two NEW tables for the web Setup pages (the replacement for the DDAdminCS "G/L Setup" and "A/R Setup" buttons). Nothing existing is changed.
--
--   dbo.tblDDReviewAdminAccess : which client web logins an administrator has allowed to use which Setup area.
--                                Administrators (tblWebUserSecurity.Administrator = 1) need no row; they may use every area.
--                                A revoke only stamps RevokedUtc, so the history stays.
--   dbo.tblDDReviewAdminAudit  : one row per change made through the Setup pages (who, from where, which table and key, old and new values).
--                                The web login is only given SELECT and INSERT on it, so it cannot rewrite its own trail.
--
-- The web login's permissions on these two tables come from  exec dbo.usp_DDReviewGrantAccess @UserName = N'<web login user>', @Execute = 1;
if object_id(N'dbo.tblDDReviewAdminAccess', N'U') is null
begin
    create table dbo.tblDDReviewAdminAccess (
        ItemID             int          not null identity(1, 1) constraint PK_tblDDReviewAdminAccess primary key,
        WebUserID          int          not null,                  -- dbo.tblWebUserSecurity.WebUserID in THIS database
        Area               varchar(30)  not null,                  -- GlSetup, ArSetup or StoreAndGlobal
        GrantedByWebUserID int          not null,
        GrantedUtc         datetime2    not null constraint DF_tblDDReviewAdminAccess_Granted default (sysutcdatetime()),
        RevokedUtc         datetime2    null,
        RevokedByWebUserID int          null
    );
    -- one live grant per login and area (revoked rows may repeat)
    create unique index UX_tblDDReviewAdminAccess_Live on dbo.tblDDReviewAdminAccess (WebUserID, Area) where RevokedUtc is null;
end
go
if object_id(N'dbo.tblDDReviewAdminAudit', N'U') is null
begin
    create table dbo.tblDDReviewAdminAudit (
        AuditID          bigint        not null identity(1, 1) constraint PK_tblDDReviewAdminAudit primary key,
        AtUtc            datetime2     not null constraint DF_tblDDReviewAdminAudit_At default (sysutcdatetime()),
        CentralWebUserID int           not null,                   -- the login in the central master
        ClientWebUserID  int           not null,                   -- the login in this database
        UserName         varchar(50)   null,
        GoogleEmail      varchar(320)  null,
        Area             varchar(30)   not null,
        TableName        varchar(128)  not null,
        KeyValue         varchar(200)  null,
        Action           varchar(10)   not null,                   -- insert, update, delete, grant or revoke
        OldValues        nvarchar(max) null,                       -- JSON
        NewValues        nvarchar(max) null,                       -- JSON
        ClientIp         varchar(64)   null
    );
    create index IX_tblDDReviewAdminAudit_At on dbo.tblDDReviewAdminAudit (AtUtc desc);
    create index IX_tblDDReviewAdminAudit_Table on dbo.tblDDReviewAdminAudit (TableName, KeyValue);
end
go
