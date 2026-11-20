-- Run on each CLIENT database (or let Admin > Database updater add it). Safe to run again. SQL Server 2012 and later.
-- Adds one table, dbo.tblWebUserFeatures, that records which web users an administrator has switched an optional DD Review feature on for
-- (today: 'Payroll' and 'ConsolidatedPayroll'). A row means ON for that user; no row means OFF, so every feature starts off for everyone
-- except administrators, who always have them. Nothing existing is changed.
if object_id(N'dbo.tblWebUserFeatures', N'U') is null
    create table dbo.tblWebUserFeatures
    (
        WebUserID int         not null,                         -- dbo.tblWebUserSecurity.WebUserID
        Feature   varchar(40) not null,                         -- 'Payroll', 'ConsolidatedPayroll'
        EnabledOn datetime    not null constraint DF_tblWebUserFeatures_EnabledOn default (getdate()),
        EnabledBy int         null,                             -- the administrator's WebUserID
        constraint PK_tblWebUserFeatures primary key (WebUserID, Feature)
    );
go
-- The web login needs to read and change it:
-- grant select, insert, delete on dbo.tblWebUserFeatures to [the web user];
