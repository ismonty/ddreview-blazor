-- Run on each CLIENT database (the one the reports read), once. Safe to run again. SQL Server 2012 and later.
-- Adds one table type, dbo.DrillPair, used to pass drill-down filters as (store, item) pairs instead of text such as
-- '1,2,3:8901,8902 1,2:8903 3:8904'. Nothing existing is changed or dropped; the functions fn_ParsePairSetStringAdd* are still what
-- turns a clicked name into those ids. Until this is run the reports keep using the text lists, so nothing breaks.
if type_id('dbo.DrillPair') is null
    create type dbo.DrillPair as table
    (
        Dim     smallint not null,   -- which filter (one number per dimension in a report run)
        StoreID int      not null,
        ItemID  bigint   not null,   -- profit center, menu item, staff, check key ... whatever the dimension's id is
        primary key (Dim, StoreID, ItemID)
    );
go
-- The web login needs to use the type:
-- grant execute on type::dbo.DrillPair to [the web user];
