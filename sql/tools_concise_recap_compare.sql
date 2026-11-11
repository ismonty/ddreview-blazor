-- Compare dbo.fn_ConciseDailyRecapBlazor2 (sql/018) with the emailed recap's own data, dbo.fn_ConciseDailyRecapUpright, for one day. Read-only: run it in SSMS on a TEST copy first.
-- Expected result: no rows (one difference is on purpose: a store with sales earlier in the month but none in the current week is not listed by the new function).
-- To time it:  set statistics time on;  then run the select below once, and  select * from dbo.fn_ConciseDailyRecapUpright(@d)  once, to see the old cost. Any row is a figure where the two disagree (OldValue = the emailed recap, NewValue = the Blazor function).
--   Slot 0 = the day, 1 = 364 days back, 2 = 728 days back.   K = the Upright row kind: 1 Gross, 2 Discounts, 3 Net, 5 Week To Date, 8 Covers, 9 Week To Date covers.
-- All stores are listed on purpose, so the "All Stores" lines (StoreNumber 9999) should agree too. A difference only on 9999 is InnerCore's own total, which is split by
-- batch count (it can come out as more than one line); the Blazor total is one sum of the listed stores.
declare @d datetime = '04/29/2021';                 -- change to a day that has sales
declare @list varchar(8000) = stuff((select ',' + cast(StoreID as varchar(10)) from dbo.tblStores for xml path('')), 1, 1, '');

;with n as (
    select r.StoreNumber, v.Slot, v.K, v.V
    from dbo.fn_ConciseDailyRecapBlazor2(@d, dbo.fn_CalcDayNumber(@d), dbo.fn_CalcDayNumber(dateadd(day, -364, @d)), dbo.fn_CalcDayNumber(dateadd(day, -728, @d)), @list) r
    cross apply (values
        (0, 1, r.GrossSales), (0, 2, r.Discounts), (0, 3, r.Sales), (0, 5, r.WeekToDate), (0, 8, r.Covers), (0, 9, r.WeekToDateCovers),
        (1, 1, r.GrossSalesLastYear), (1, 2, r.DiscountsLastYear), (1, 3, r.SalesLastYear), (1, 5, r.WeekToDateLastYear), (1, 8, r.CoversLastYear), (1, 9, r.WeekToDateCoversLastYear),
        (2, 1, r.GrossSalesTwoYearsAgo), (2, 2, r.DiscountsTwoYearsAgo), (2, 3, r.SalesTwoYearsAgo), (2, 5, r.WeekToDateTwoYearsAgo), (2, 8, r.CoversTwoYearsAgo), (2, 9, r.WeekToDateCoversTwoYearsAgo)
    ) v(Slot, K, V)
),
o as (
    select 0 as Slot, u.GroupOrder / 100000000 as K, u.GroupOrder % 100000000 as StoreNumber, u.MoneyColumn2 as V
    from dbo.fn_ConciseDailyRecapUpright(@d) u where u.RowLevel = 5
    union all
    select 1, u.GroupOrder / 100000000, u.GroupOrder % 100000000, u.MoneyColumn2
    from dbo.fn_ConciseDailyRecapUpright(dateadd(day, -364, @d)) u where u.RowLevel = 5
    union all
    select 2, u.GroupOrder / 100000000, u.GroupOrder % 100000000, u.MoneyColumn2
    from dbo.fn_ConciseDailyRecapUpright(dateadd(day, -728, @d)) u where u.RowLevel = 5
)
select coalesce(n.StoreNumber, o.StoreNumber) as StoreNumber, coalesce(n.Slot, o.Slot) as Slot, coalesce(n.K, o.K) as K, o.V as OldValue, n.V as NewValue
from n
full outer join o on o.Slot = n.Slot and o.K = n.K and o.StoreNumber = n.StoreNumber
where isnull(o.V, 0) <> isnull(n.V, 0)
order by 1, 2, 3;
