using System.Collections.Concurrent;
using System.Data;
using DDReview.Core.Tenancy;
using DDReview.Payroll.Adp;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll.Consolidated;

/// <summary>The saved ADP tip-export settings (tblGlobalSettings tags, same names the Access form used).</summary>
public sealed record ConsolidatedTipSettings(string CoCode = "", string BatchId = "", string BatchDescription = "", string ChargedTipsMemoCode = "", string ChargedSalesMemoCode = "",
    string TotalSalesMemoCode = "", string TotalTipsEarnings34 = "", string TotalTipsEarningsCode = "", string UnpaidChargedTipsNegativeDeductionCode = "");

public sealed class ConsolidatedPayrollService
{
    // The crunch procedure empties and refills one shared work table that the function then reads, so two runs at once
    // (in one database) could read each other's rows. Runs are queued per database inside this program.
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    static readonly (string Tag, Func<ConsolidatedTipSettings, string> Get, Func<ConsolidatedTipSettings, string, ConsolidatedTipSettings> Set)[] Fields =
    [
        ("ADPCoCode", s => s.CoCode, (s, v) => s with { CoCode = v }),
        ("ADPBatchID", s => s.BatchId, (s, v) => s with { BatchId = v }),
        ("ADPBatchDescription", s => s.BatchDescription, (s, v) => s with { BatchDescription = v }),
        ("ADPChargedTipsMemoCode", s => s.ChargedTipsMemoCode, (s, v) => s with { ChargedTipsMemoCode = v }),
        ("ADPChargedSalesMemoCode", s => s.ChargedSalesMemoCode, (s, v) => s with { ChargedSalesMemoCode = v }),
        ("ADPTotalSalesMemoCode", s => s.TotalSalesMemoCode, (s, v) => s with { TotalSalesMemoCode = v }),
        ("ADPTotalTipsEarnings34", s => s.TotalTipsEarnings34, (s, v) => s with { TotalTipsEarnings34 = v }),
        ("ADPTotalTipsEarningsCode", s => s.TotalTipsEarningsCode, (s, v) => s with { TotalTipsEarningsCode = v }),
        ("ADPUnpaidChargedTipsNegativeDeductionCode", s => s.UnpaidChargedTipsNegativeDeductionCode, (s, v) => s with { UnpaidChargedTipsNegativeDeductionCode = v }),
    ];

    public async Task<ConsolidatedTipSettings> GetSettingsAsync(string connectionString, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        var s = new ConsolidatedTipSettings();
        foreach (var f in Fields)
        {
            await using var cmd = new SqlCommand("select top 1 GlobalSettingStringValue from dbo.tblGlobalSettings where GlobalSettingTag = @t order by ItemID", cn) { CommandTimeout = 60 };
            cmd.Parameters.Add("@t", SqlDbType.VarChar, 100).Value = f.Tag;
            var v = await cmd.ExecuteScalarAsync(ct);
            s = f.Set(s, v is null or DBNull ? "" : Convert.ToString(v)!.Trim());
        }
        return s;
    }

    /// <summary>Saves through the existing cs_spGlobalSettingsSet procedure, as the Access form did. Administrators only.</summary>
    public async Task SaveSettingsAsync(string connectionString, UserAccess admin, ConsolidatedTipSettings settings, CancellationToken ct = default)
    {
        if (!admin.Administrator) throw new UnauthorizedAccessException("Only an administrator may change the payroll export settings.");
        foreach (var f in Fields)
            if (f.Get(settings).Length > 250) throw new ArgumentException("A setting is too long (250 characters at most).");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        foreach (var f in Fields)
        {
            await using var cmd = new SqlCommand("dbo.cs_spGlobalSettingsSet", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 60 };
            cmd.Parameters.Add("@GlobalSettingTag", SqlDbType.VarChar, 100).Value = f.Tag;
            cmd.Parameters.Add("@GlobalSettingSubTag", SqlDbType.VarChar, 100).Value = DBNull.Value;
            cmd.Parameters.Add("@GlobalSettingStringValue", SqlDbType.VarChar, 250).Value = f.Get(settings);
            cmd.Parameters.Add("@GlobalSettingIntValue", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@GlobalSettingBigIntValue", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@GlobalSettingNTextValue", SqlDbType.NText).Value = DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Builds the ADP sales-and-tips file for one store and period: runs cs_spConsolidatedTipOut3Crunch, then reads fn_ConsolidatedTipOutADPExport3
    /// (payroll group 1, as the old form passed). Returns null when there is nothing to export. The store must be one the user is authorized for.
    /// </summary>
    public async Task<AdpFile?> ExportTipFileAsync(string connectionString, int storeId, DateOnly start, DateOnly end, bool checkReceiptFlag,
        IReadOnlySet<int> authorizedStoreIds, CancellationToken ct = default)
    {
        if (end < start) throw new ArgumentException("The end date is before the start date.");
        if (!authorizedStoreIds.Contains(storeId)) throw new UnauthorizedAccessException("You are not authorized for that store.");
        var s = await GetSettingsAsync(connectionString, ct);
        if (!int.TryParse(s.BatchId.Trim(), out var batch)) batch = 0;     // the old code used Val(): text that is not a number counts as 0

        var key = new SqlConnectionStringBuilder(connectionString) { Password = "", UserID = "" };
        var gate = Locks.GetOrAdd($"{key.DataSource}|{key.InitialCatalog}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await using var cn = new SqlConnection(connectionString);
            await cn.OpenAsync(ct);
            await using (var crunch = new SqlCommand("dbo.cs_spConsolidatedTipOut3Crunch", cn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 })
            {
                crunch.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
                crunch.Parameters.Add("@TipDate1", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
                crunch.Parameters.Add("@TipDate2", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
                crunch.Parameters.Add("@StoreStaffID", SqlDbType.Int).Value = 0;
                crunch.Parameters.Add("@ClassifyByIndividualReceipt", SqlDbType.Int).Value = 0;
                await crunch.ExecuteNonQueryAsync(ct);
            }
            await using var cmd = new SqlCommand(@"select * from dbo.fn_ConsolidatedTipOutADPExport3(@store, @s, @e, @chk, @co, @batch, @desc, @t34, @t34code, @sales, @ctips, @csales, @unpaid, 1)", cn) { CommandTimeout = 300 };
            cmd.Parameters.Add("@store", SqlDbType.Int).Value = storeId;
            cmd.Parameters.Add("@s", SqlDbType.DateTime).Value = start.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@e", SqlDbType.DateTime).Value = end.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("@chk", SqlDbType.Bit).Value = checkReceiptFlag;
            cmd.Parameters.Add("@co", SqlDbType.VarChar, 20).Value = s.CoCode;
            cmd.Parameters.Add("@batch", SqlDbType.Int).Value = batch;
            cmd.Parameters.Add("@desc", SqlDbType.VarChar, 30).Value = s.BatchDescription;
            cmd.Parameters.Add("@t34", SqlDbType.VarChar, 10).Value = s.TotalTipsEarnings34;
            cmd.Parameters.Add("@t34code", SqlDbType.VarChar, 10).Value = s.TotalTipsEarningsCode;
            cmd.Parameters.Add("@sales", SqlDbType.VarChar, 10).Value = s.TotalSalesMemoCode;
            cmd.Parameters.Add("@ctips", SqlDbType.VarChar, 10).Value = s.ChargedTipsMemoCode;
            cmd.Parameters.Add("@csales", SqlDbType.VarChar, 10).Value = s.ChargedSalesMemoCode;
            cmd.Parameters.Add("@unpaid", SqlDbType.VarChar, 10).Value = s.UnpaidChargedTipsNegativeDeductionCode;
            var rows = new List<ConsolidatedTipRow>();
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                string? S(string c) => rd[c] is DBNull ? null : Convert.ToString(rd[c]);
                decimal? D(string c) => rd[c] is DBNull ? null : Convert.ToDecimal(rd[c]);
                rows.Add(new ConsolidatedTipRow(S("Co Code"), rd["Batch ID"] is DBNull ? 0 : Convert.ToInt32(rd["Batch ID"]), rd["File #"] is DBNull ? 0 : Convert.ToInt64(rd["File #"]),
                    S("Batch Description"), rd["Earnings 34 Type"] is DBNull ? null : Convert.ToInt32(rd["Earnings 34 Type"]), S("Earnings 34 Code"), D("Earnings 34 Amount"),
                    S("Memo Code 1"), D("Memo Amount 1"), S("Memo Code 2"), D("Memo Amount 2"), S("Memo Code 3"), D("Memo Amount 3"), S("Deduction Code"), D("Deduction Amount")));
            }
            return ConsolidatedTipFile.Build(rows);
        }
        finally { gate.Release(); }
    }
}
