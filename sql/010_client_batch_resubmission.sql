-- Run on each CLIENT database (or let Admin > Database updater add it). Safe to run again. SQL Server 2012 and later.
-- Adds dbo.usp_DDReviewRequestBatchResubmission, used by the General Ledger Review's "Request Batch Resubmission" button.
-- It marks one batch so the submission function returns it for more work: BatchSubmitted is set to the batch's PostingDate
-- and BatchSubmissionStatus to 4. Nothing else in the batch is touched.
--
-- Then give the web login permission to run it (the Database updater does this too):
--     exec dbo.usp_DDReviewGrantAccess @UserName = N'the web login user', @Execute = 1;
if object_id(N'dbo.usp_DDReviewRequestBatchResubmission', N'P') is not null
    drop procedure dbo.usp_DDReviewRequestBatchResubmission;
go
create procedure dbo.usp_DDReviewRequestBatchResubmission
    @BatchID int
as
begin
    set nocount on;

    update dbo.tblCashAndSalesBatches
       set BatchSubmitted = PostingDate,
           BatchSubmissionStatus = 4
     where BatchID = @BatchID;

    select cast(@@rowcount as int) as RowsChanged;   -- 0 = there is no such batch
end
go
