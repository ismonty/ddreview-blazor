-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again; it does nothing when already applied.
-- Adds ONE column to the one-row table dbo.tblGlobal: DailyRecapEnabled (bit, default 1 = on). Setup > Global Settings shows it as a check box,
-- and the website hides the Daily Recap tab for this database when it is off. Every existing row (and every existing program) is unaffected:
-- the column is new, nothing is renamed or removed, and a database that has never run this script behaves as if the switch were on.
set nocount on;

if object_id(N'dbo.tblGlobal', N'U') is null
begin
    raiserror(N'This database has no dbo.tblGlobal, so there is nothing to add the Daily Recap switch to.', 16, 1);
    return;
end

if col_length(N'dbo.tblGlobal', N'DailyRecapEnabled') is not null
begin
    print N'Already applied.';
    return;
end

alter table dbo.tblGlobal add DailyRecapEnabled bit not null
    constraint DF_tblGlobal_DailyRecapEnabled default (1);
print N'Added dbo.tblGlobal.DailyRecapEnabled (on for every existing row).';
