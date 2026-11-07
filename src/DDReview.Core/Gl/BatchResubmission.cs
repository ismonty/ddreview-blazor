using System.Text.Json;
using DDReview.Core.Admin;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

/// <summary>
/// "Request Batch Resubmission" on the General Ledger Review: runs dbo.usp_DDReviewRequestBatchResubmission (sql/010), which sets the batch's
/// BatchSubmitted to its PostingDate and BatchSubmissionStatus to 4 so the submission function returns it for more work.
/// The store check and the audit row are here, in one transaction with the change.
/// </summary>
public sealed record BatchTimes(DateTime? Submitted, DateTime? SettingsUpdated)
{
    /// <summary>"Submitted 11/7/2026 9:12 AM, Settings 11/7/2026 8:00 AM" (or "Not submitted"); Settings is left off when the store has no such date.</summary>
    public string Text() =>
        (Submitted is DateTime d ? "Submitted " + d.ToString("M/d/yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : "Not submitted")
        + (SettingsUpdated is DateTime u ? ", Settings " + u.ToString("M/d/yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : "");
}

public sealed class BatchResubmissionService
{
    private const string AuditArea = "GlResubmit";

    /// <summary>When the batch was last submitted, as on record (null = never), and when its store's settings were last updated (null = unknown or never).</summary>
    public async Task<BatchTimes> GetTimesAsync(string cs, string? allowedStoresCsv, int batchId, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await GlAdjustmentService.EnsureBatchAllowedAsync(cn, null!, batchId, allowedStoresCsv, ct);
        try { return await ReadTimesAsync(cn, "st.SettingsUpdated", batchId, ct); }
        catch (SqlException ex) when (ex.Number == 207) { return await ReadTimesAsync(cn, "null", batchId, ct); }   // a database without tblStores.SettingsUpdated
    }

    private static async Task<BatchTimes> ReadTimesAsync(SqlConnection cn, string settingsColumn, int batchId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"select b.BatchSubmitted, {settingsColumn} as SettingsUpdated from dbo.tblCashAndSalesBatches b left join dbo.tblStores st on st.StoreID = b.StoreID where b.BatchID = @b", cn);
        cmd.Parameters.Add("@b", System.Data.SqlDbType.Int).Value = batchId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new BatchTimes(r["BatchSubmitted"] as DateTime?, r["SettingsUpdated"] as DateTime?) : new BatchTimes(null, null);
    }

    /// <param name="allowedStoresCsv">null = every store; otherwise the batch must belong to one of these stores.</param>
    public async Task RequestAsync(string cs, SetupActor actor, string? allowedStoresCsv, int batchId, CancellationToken ct = default)
    {
        if (batchId <= 0) throw new GlAdjustmentException("Choose a batch first.");
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await GlAdjustmentService.EnsureBatchAllowedAsync(cn, tx, batchId, allowedStoresCsv, ct);

        object before;
        await using (var read = new SqlCommand("select PostingDate, BatchSubmitted, BatchSubmissionStatus from dbo.tblCashAndSalesBatches where BatchID = @b", cn, tx))
        {
            read.Parameters.Add("@b", System.Data.SqlDbType.Int).Value = batchId;
            await using var r = await read.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new GlAdjustmentException("That batch does not exist.");
            before = new { PostingDate = r["PostingDate"] as DateTime?, BatchSubmitted = r["BatchSubmitted"] as DateTime?, BatchSubmissionStatus = r["BatchSubmissionStatus"] is DBNull ? (int?)null : Convert.ToInt32(r["BatchSubmissionStatus"]) };
        }

        try
        {
            await using var cmd = new SqlCommand("dbo.usp_DDReviewRequestBatchResubmission", cn, tx) { CommandType = System.Data.CommandType.StoredProcedure };
            cmd.Parameters.Add("@BatchID", System.Data.SqlDbType.Int).Value = batchId;
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) throw new GlAdjustmentException("That batch does not exist.");
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            throw new GlAdjustmentException("This database does not have the resubmission procedure yet. An administrator can add it from the Database Updater (script 010).");
        }
        catch (SqlException ex) when (ex.Number is 229 or 262)
        {
            throw new GlAdjustmentException("The web login may not run the resubmission procedure yet. An administrator can grant it from the Database Updater.");
        }

        try
        {
            await SetupAudit.WriteAsync(cn, tx, actor, AuditArea, "tblCashAndSalesBatches", batchId.ToString(), "resubmit",
                JsonSerializer.Serialize(before), JsonSerializer.Serialize(new { BatchSubmitted = "PostingDate", BatchSubmissionStatus = 4 }), ct);
        }
        catch (SqlException ex) when (ex.Number is 208 or 229 or 230)
        {
            throw new GlAdjustmentException("The change log table is not usable in this database yet, so nothing was changed. An administrator can add it from the Database Updater.");
        }
        await tx.CommitAsync(ct);
    }
}
