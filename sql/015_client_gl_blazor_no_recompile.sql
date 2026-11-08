-- Run on each CLIENT database (or let Admin > Database updater do it). Safe to run again. Needs script 014.
-- Script 014 first added OPTION (RECOMPILE) to the mapping statement inside dbo.fn_CashAndSalesGLandARBlazor. Measured on a 20-store run it cost
-- more than it saved (about 0.9 s of planning per store call), so this removes it from the copy. If it is not there, nothing happens.
set nocount on;
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
declare @id int = object_id(N'dbo.fn_CashAndSalesGLandARBlazor', N'TF');
if @id is null
begin
    raiserror(N'Run scripts 013 and 014 first: dbo.fn_CashAndSalesGLandARBlazor does not exist.', 16, 1);
    return;
end
declare @def nvarchar(max) = object_definition(@id);
if @def is null
begin
    raiserror(N'This login may not read the text of dbo.fn_CashAndSalesGLandARBlazor.', 16, 1);
    return;
end
declare @hint nvarchar(200) = N'fn_CashAndSalesMappedRecordsBlazor(@StoreID, @StartDate, @EndDate, @ARFlag) OPTION (RECOMPILE)';
if charindex(@hint, @def collate Latin1_General_CI_AS) = 0
begin
    print N'Already without the recompile hint.';
    return;
end
declare @w int = charindex(N'FUNCTION', @def collate Latin1_General_CI_AS);
set @def = N'ALTER ' + substring(@def, @w, len(@def));
set @def = replace(@def collate Latin1_General_CI_AS, @hint, N'fn_CashAndSalesMappedRecordsBlazor(@StoreID, @StartDate, @EndDate, @ARFlag)');
exec sys.sp_executesql @def;
GO
