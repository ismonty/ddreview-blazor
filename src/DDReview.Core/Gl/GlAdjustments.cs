using System.Globalization;
using System.Text.Json;
using DDReview.Core.Admin;
using Microsoft.Data.SqlClient;

namespace DDReview.Core.Gl;

// The General Ledger Review's two adjustment windows, as in DD Admin CS.
//   Adjust Sales  (dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales): changes a sales amount BEFORE the sales tax is worked out, so tax and totals follow it.
//   Adjust GL     (dbo.tblCashAndSalesGeneralLedgerAdjustments):      adds a ledger line AFTER the tax calculation; sales and tax are not changed.

/// <summary>A day's batch for one store, as the adjustment windows list it.</summary>
public sealed record AdjustBatch(int BatchId, int StoreId, string StoreName, DateTime PostingDate)
{
    public string Label => $"{StoreName}, {PostingDate:ddd MMM d, yyyy} (batch {BatchId})";
}

public sealed record GlAdjustRow(int ItemId, int BatchId, string? Account, decimal? Debit, decimal? Credit, string? DistReference, int? Sorter,
    string? BankRecAccount, int AccountClassId, string? Vendor, short DebitCreditFlag, string? DefaultAccountType);

public sealed record SalesAdjustRow(int ItemId, int BatchId, int CreditAccountId, decimal? SalesAmount, string? OffsetAccount, int OffsetAccountClassId, string? OffsetDistReference);

public sealed record NamedId(int Id, string Name);
public sealed record CreditAccountChoice(int CreditAccountId, int StoreId, string Label);

public sealed record AdjustData(IReadOnlyList<AdjustBatch> Batches, IReadOnlyList<GlAdjustRow> Gl, IReadOnlyList<SalesAdjustRow> Sales,
    IReadOnlyList<NamedId> Classes, IReadOnlyList<CreditAccountChoice> CreditAccounts);

public enum AdjustKind { Gl, Sales }

/// <summary>A ledger line the person double-clicked: opens Adjust GL ready to add an adjustment for that account (as DD Admin CS did), with the same batch, account, reference, class and flag.</summary>
public sealed record AdjustSeed(int BatchId, string? Account, string? DistReference, string? AccountClass, short DebitCreditFlag);

/// <summary>What is wrong with a row, in words for the person, or null when it can be saved.</summary>
public static class AdjustValidation
{
    public static bool TryMoney(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!decimal.TryParse(text.Trim().Replace(",", ""), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return false;
        if (decimal.Round(d, 2) != d || Math.Abs(d) > 9_999_999_999m) return false;
        value = d;
        return true;
    }

    public static string? Check(GlAdjustRow r)
    {
        if (r.BatchId <= 0) return "Choose the batch.";
        if (string.IsNullOrWhiteSpace(r.Account)) return "Enter the account.";
        if (r.Account!.Length > 100) return "The account is limited to 100 characters.";
        if ((r.DistReference?.Length ?? 0) > 100) return "The reference is limited to 100 characters.";
        if ((r.BankRecAccount?.Length ?? 0) > 50 || (r.Vendor?.Length ?? 0) > 50 || (r.DefaultAccountType?.Length ?? 0) > 50) return "Bank rec account, vendor and type are limited to 50 characters.";
        var d = r.Debit ?? 0m; var c = r.Credit ?? 0m;
        if (d < 0 || c < 0) return "Debit and credit cannot be negative. Use the other column instead.";
        if (d == 0 && c == 0) return "Enter a debit or a credit.";
        if (d != 0 && c != 0) return "Enter a debit or a credit, not both.";
        if (r.AccountClassId <= 0) return "Choose the class.";
        return null;
    }

    public static string? Check(SalesAdjustRow r)
    {
        if (r.BatchId <= 0) return "Choose the batch.";
        if (r.CreditAccountId <= 0) return "Choose the sales account.";
        if ((r.SalesAmount ?? 0m) == 0m) return "Enter the sales amount to add (use a minus sign to take sales away).";
        if (string.IsNullOrWhiteSpace(r.OffsetAccount)) return "Enter the offset account.";
        if (r.OffsetAccount!.Length > 100 || (r.OffsetDistReference?.Length ?? 0) > 100) return "The offset account and reference are limited to 100 characters.";
        if (r.OffsetAccountClassId <= 0) return "Choose the offset class.";
        return null;
    }
}

public sealed class GlAdjustmentException(string message) : Exception(message);

/// <summary>
/// Reads and changes the adjustments for the batches of a General Ledger Review run. Every change checks that the batch belongs to a store the person may see,
/// guards against someone else having changed the row meanwhile, and writes an audit row in the same transaction.
/// </summary>
public sealed class GlAdjustmentService
{
    private const string AuditArea = "GlAdjust";
    private const string Ids = "dbo.fn_CashAndSalesGLMultiStoreSelectBatchIDs(@stores, @start, @end)";

    private static HashSet<int>? Allowed(string? csv) =>
        csv is null ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(t => int.TryParse(t, out var n) ? n : -1).ToHashSet();

    private static void Bind(SqlCommand cmd, GlRequest req)
    {
        cmd.Parameters.Add("@stores", System.Data.SqlDbType.VarChar, 8000).Value = req.StoreListCsv ?? "";
        cmd.Parameters.Add("@start", System.Data.SqlDbType.DateTime).Value = req.Start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("@end", System.Data.SqlDbType.DateTime).Value = req.End.ToDateTime(TimeOnly.MinValue);
    }

    /// <param name="allowedStoresCsv">null = every store; otherwise only these stores' batches are shown and may be changed.</param>
    public async Task<AdjustData> LoadAsync(string cs, GlRequest req, string? allowedStoresCsv, CancellationToken ct = default)
    {
        var allowed = Allowed(allowedStoresCsv);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);

        var batches = new List<AdjustBatch>();
        await using (var cmd = new SqlCommand($@"
select b.BatchID, b.StoreID, isnull(s.StoreReportName, s.StoreName) as StoreName, b.PostingDate
from {Ids} ids
join dbo.tblCashAndSalesBatches b on b.BatchID = ids.BatchID
join dbo.tblStores s on s.StoreID = b.StoreID
order by b.PostingDate, s.StoreNumber, b.BatchID", cn) { CommandTimeout = 120 })
        {
            Bind(cmd, req);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var store = Convert.ToInt32(r["StoreID"]);
                if (allowed is null || allowed.Contains(store))
                    batches.Add(new AdjustBatch(Convert.ToInt32(r["BatchID"]), store, r["StoreName"] as string ?? "", (DateTime)r["PostingDate"]));
            }
        }
        var ok = batches.Select(b => b.BatchId).ToHashSet();

        var gl = new List<GlAdjustRow>();
        await using (var cmd = new SqlCommand($@"
select a.ItemID, a.BatchID, a.Account, a.Debit, a.Credit, a.DistReference, a.Sorter, a.BankRecAccount, a.AccountClassID, a.Vendor, a.DebitCreditFlag, a.DefaultAccountType
from dbo.tblCashAndSalesGeneralLedgerAdjustments a join {Ids} ids on ids.BatchID = a.BatchID
order by a.BatchID, a.Sorter, a.ItemID", cn) { CommandTimeout = 120 })
        {
            Bind(cmd, req);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(string c) => r[c] is DBNull ? null : r[c].ToString();
            decimal? M(string c) => r[c] is DBNull ? null : Convert.ToDecimal(r[c]);
            while (await r.ReadAsync(ct))
            {
                var batch = Convert.ToInt32(r["BatchID"]);
                if (!ok.Contains(batch)) continue;
                gl.Add(new GlAdjustRow(Convert.ToInt32(r["ItemID"]), batch, S("Account"), M("Debit"), M("Credit"), S("DistReference"),
                    r["Sorter"] is DBNull ? null : Convert.ToInt32(r["Sorter"]), S("BankRecAccount"), Convert.ToInt32(r["AccountClassID"]), S("Vendor"),
                    Convert.ToInt16(r["DebitCreditFlag"]), S("DefaultAccountType")));
            }
        }

        var sales = new List<SalesAdjustRow>();
        await using (var cmd = new SqlCommand($@"
select a.ItemID, a.BatchID, a.CreditAccountID, a.SalesAmount, a.OffsetAccount, a.OffsetAccountClassID, a.OffsetDistReference
from dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales a join {Ids} ids on ids.BatchID = a.BatchID
order by a.BatchID, a.ItemID", cn) { CommandTimeout = 120 })
        {
            Bind(cmd, req);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var batch = Convert.ToInt32(r["BatchID"]);
                if (!ok.Contains(batch)) continue;
                sales.Add(new SalesAdjustRow(Convert.ToInt32(r["ItemID"]), batch, Convert.ToInt32(r["CreditAccountID"]),
                    r["SalesAmount"] is DBNull ? null : Convert.ToDecimal(r["SalesAmount"]), r["OffsetAccount"] as string,
                    Convert.ToInt32(r["OffsetAccountClassID"]), r["OffsetDistReference"] as string));
            }
        }

        var classes = new List<NamedId>();
        await using (var cmd = new SqlCommand("select AccountClassID, AccountClass from dbo.tblAccountsClasses order by AccountClass", cn))
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) classes.Add(new NamedId(Convert.ToInt32(r["AccountClassID"]), (string)r["AccountClass"]));
        }

        var credits = new List<CreditAccountChoice>();
        var storeIds = batches.Select(b => b.StoreId).Distinct().ToList();
        if (storeIds.Count > 0)
        {
            await using var cmd = new SqlCommand($@"
select ac.CreditAccountID, ac.StoreID,
       rtrim(ac.DistReference) + ' (' + rtrim(ac.CreditAccount) + isnull(' - ' + rtrim(tt.TaxType), '') + ')' as Label
from dbo.tblAccountsCredit ac
left join dbo.tblTaxTypes tt on tt.TaxTypeID = ac.TaxTypeID
where ac.StoreID in ({string.Join(",", storeIds)})
order by rtrim(ac.DistReference), ac.CreditAccount", cn) { CommandTimeout = 60 };   // ids are integers from the database, not typed text
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) credits.Add(new CreditAccountChoice(Convert.ToInt32(r["CreditAccountID"]), Convert.ToInt32(r["StoreID"]), r["Label"] as string ?? ""));
        }
        return new AdjustData(batches, gl, sales, classes, credits);
    }

    // ---- changes ----

    public Task<int> AddAsync(string cs, SetupActor actor, string? allowedStoresCsv, GlAdjustRow row, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, row.BatchId, AdjustValidation.Check(row), "tblCashAndSalesGeneralLedgerAdjustments", "insert", null, row, async (cn, tx) =>
        {
            await using var cmd = new SqlCommand(@"
insert dbo.tblCashAndSalesGeneralLedgerAdjustments (BatchID, Account, Debit, Credit, DistReference, Sorter, BankRecAccount, AccountClassID, Vendor, DebitCreditFlag, DefaultAccountType)
output inserted.ItemID
values (@b, @a, @d, @c, @r, @s, @br, @cl, @v, @f, @t)", cn, tx);
            BindGl(cmd, row, "");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }, ct);

    public Task ChangeAsync(string cs, SetupActor actor, string? allowedStoresCsv, GlAdjustRow original, GlAdjustRow row, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, row.BatchId, AdjustValidation.Check(row), "tblCashAndSalesGeneralLedgerAdjustments", "update", original, row, async (cn, tx) =>
        {
            await EnsureBatchAllowedAsync(cn, tx, original.BatchId, allowedStoresCsv, ct);   // the row must be one the person could already see
            await using var cmd = new SqlCommand($@"
update dbo.tblCashAndSalesGeneralLedgerAdjustments
set BatchID = @b, Account = @a, Debit = @d, Credit = @c, DistReference = @r, Sorter = @s, BankRecAccount = @br, AccountClassID = @cl, Vendor = @v, DebitCreditFlag = @f, DefaultAccountType = @t
where ItemID = @id and {SameGl()}", cn, tx);
            BindGl(cmd, row, ""); BindGl(cmd, original, "o_");
            cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = original.ItemId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new GlAdjustmentException("Someone else changed or removed this adjustment. Reload and try again.");
            return original.ItemId;
        }, ct);

    public Task RemoveAsync(string cs, SetupActor actor, string? allowedStoresCsv, GlAdjustRow original, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, original.BatchId, null, "tblCashAndSalesGeneralLedgerAdjustments", "delete", original, null, async (cn, tx) =>
        {
            await using var cmd = new SqlCommand($"delete dbo.tblCashAndSalesGeneralLedgerAdjustments where ItemID = @id and {SameGl()}", cn, tx);
            BindGl(cmd, original, "o_");
            cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = original.ItemId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new GlAdjustmentException("Someone else changed or removed this adjustment. Reload and try again.");
            return original.ItemId;
        }, ct);

    public Task<int> AddAsync(string cs, SetupActor actor, string? allowedStoresCsv, SalesAdjustRow row, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, row.BatchId, AdjustValidation.Check(row), "tblCashAndSalesGeneralLedgerAdjustmentsSales", "insert", null, row, async (cn, tx) =>
        {
            await EnsureCreditAccountAsync(cn, tx, row, ct);
            await using var cmd = new SqlCommand(@"
insert dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales (BatchID, CreditAccountID, SalesAmount, OffsetAccount, OffsetAccountClassID, OffsetDistReference)
output inserted.ItemID
values (@b, @ca, @amt, @oa, @oc, @or)", cn, tx);
            BindSales(cmd, row, "");
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }, ct);

    public Task ChangeAsync(string cs, SetupActor actor, string? allowedStoresCsv, SalesAdjustRow original, SalesAdjustRow row, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, row.BatchId, AdjustValidation.Check(row), "tblCashAndSalesGeneralLedgerAdjustmentsSales", "update", original, row, async (cn, tx) =>
        {
            await EnsureBatchAllowedAsync(cn, tx, original.BatchId, allowedStoresCsv, ct);
            await EnsureCreditAccountAsync(cn, tx, row, ct);
            await using var cmd = new SqlCommand($@"
update dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales
set BatchID = @b, CreditAccountID = @ca, SalesAmount = @amt, OffsetAccount = @oa, OffsetAccountClassID = @oc, OffsetDistReference = @or
where ItemID = @id and {SameSales()}", cn, tx);
            BindSales(cmd, row, ""); BindSales(cmd, original, "o_");
            cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = original.ItemId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new GlAdjustmentException("Someone else changed or removed this adjustment. Reload and try again.");
            return original.ItemId;
        }, ct);

    public Task RemoveAsync(string cs, SetupActor actor, string? allowedStoresCsv, SalesAdjustRow original, CancellationToken ct = default) =>
        ChangeAsync(cs, actor, allowedStoresCsv, original.BatchId, null, "tblCashAndSalesGeneralLedgerAdjustmentsSales", "delete", original, null, async (cn, tx) =>
        {
            await using var cmd = new SqlCommand($"delete dbo.tblCashAndSalesGeneralLedgerAdjustmentsSales where ItemID = @id and {SameSales()}", cn, tx);
            BindSales(cmd, original, "o_");
            cmd.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = original.ItemId;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new GlAdjustmentException("Someone else changed or removed this adjustment. Reload and try again.");
            return original.ItemId;
        }, ct);

    private static async Task<int> ChangeAsync(string cs, SetupActor actor, string? allowedStoresCsv, int batchId, string? problem, string table, string action,
        object? before, object? after, Func<SqlConnection, SqlTransaction, Task<int>> work, CancellationToken ct)
    {
        if (problem is not null) throw new GlAdjustmentException(problem);
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        await EnsureBatchAllowedAsync(cn, tx, batchId, allowedStoresCsv, ct);
        var id = await work(cn, tx);
        try
        {
            await SetupAudit.WriteAsync(cn, tx, actor, AuditArea, table, id.ToString(), action,
                before is null ? null : JsonSerializer.Serialize(before, before.GetType()), after is null ? null : JsonSerializer.Serialize(after, after.GetType()), ct);
        }
        catch (SqlException ex) when (ex.Number is 208 or 229 or 230)
        {
            throw new GlAdjustmentException("The change log table is not usable in this database yet, so nothing was saved. An administrator can add it from the Database Updater.");
        }
        await tx.CommitAsync(ct);
        return id;
    }

    private static async Task EnsureBatchAllowedAsync(SqlConnection cn, SqlTransaction tx, int batchId, string? allowedStoresCsv, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("select StoreID from dbo.tblCashAndSalesBatches where BatchID = @b", cn, tx);
        cmd.Parameters.Add("@b", System.Data.SqlDbType.Int).Value = batchId;
        var o = await cmd.ExecuteScalarAsync(ct);
        if (o is null or DBNull) throw new GlAdjustmentException("That batch does not exist.");
        var allowed = Allowed(allowedStoresCsv);
        if (allowed is not null && !allowed.Contains(Convert.ToInt32(o))) throw new GlAdjustmentException("You are not authorized for that store.");
    }

    /// <summary>The sales account chosen must belong to the same store as the batch.</summary>
    private static async Task EnsureCreditAccountAsync(SqlConnection cn, SqlTransaction tx, SalesAdjustRow row, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
select count(*) from dbo.tblAccountsCredit ac join dbo.tblCashAndSalesBatches b on b.StoreID = ac.StoreID
where ac.CreditAccountID = @ca and b.BatchID = @b", cn, tx);
        cmd.Parameters.Add("@ca", System.Data.SqlDbType.Int).Value = row.CreditAccountId;
        cmd.Parameters.Add("@b", System.Data.SqlDbType.Int).Value = row.BatchId;
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 0) throw new GlAdjustmentException("That sales account does not belong to the batch's store.");
    }

    // The WHERE that makes an update or delete refuse when someone else has changed the row since it was read.
    private static string SameGl() => string.Join(" and ", new[]
    {
        "BatchID = @o_b", "(Account = @o_a or (Account is null and @o_a is null))", "(Debit = @o_d or (Debit is null and @o_d is null))", "(Credit = @o_c or (Credit is null and @o_c is null))",
        "(DistReference = @o_r or (DistReference is null and @o_r is null))", "(Sorter = @o_s or (Sorter is null and @o_s is null))",
        "(BankRecAccount = @o_br or (BankRecAccount is null and @o_br is null))", "AccountClassID = @o_cl", "(Vendor = @o_v or (Vendor is null and @o_v is null))",
        "DebitCreditFlag = @o_f", "(DefaultAccountType = @o_t or (DefaultAccountType is null and @o_t is null))",
    });

    private static string SameSales() => string.Join(" and ", new[]
    {
        "BatchID = @o_b", "CreditAccountID = @o_ca", "(SalesAmount = @o_amt or (SalesAmount is null and @o_amt is null))",
        "(OffsetAccount = @o_oa or (OffsetAccount is null and @o_oa is null))", "OffsetAccountClassID = @o_oc", "(OffsetDistReference = @o_or or (OffsetDistReference is null and @o_or is null))",
    });

    private static object Db(object? v) => v ?? DBNull.Value;
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    // Values saved are trimmed; the "as read" copy used to detect someone else's change is sent exactly as it was read.
    private static string? Clean(string p, string? s) => p.Length == 0 ? Nz(s) : s;

    private static void BindGl(SqlCommand c, GlAdjustRow r, string p)
    {
        c.Parameters.Add("@" + p + "b", System.Data.SqlDbType.Int).Value = r.BatchId;
        c.Parameters.Add("@" + p + "a", System.Data.SqlDbType.VarChar, 100).Value = Db(Clean(p, r.Account));
        c.Parameters.Add("@" + p + "d", System.Data.SqlDbType.Money).Value = Db(r.Debit);
        c.Parameters.Add("@" + p + "c", System.Data.SqlDbType.Money).Value = Db(r.Credit);
        c.Parameters.Add("@" + p + "r", System.Data.SqlDbType.VarChar, 100).Value = Db(Clean(p, r.DistReference));
        c.Parameters.Add("@" + p + "s", System.Data.SqlDbType.Int).Value = Db(r.Sorter);
        c.Parameters.Add("@" + p + "br", System.Data.SqlDbType.VarChar, 50).Value = Db(Clean(p, r.BankRecAccount));
        c.Parameters.Add("@" + p + "cl", System.Data.SqlDbType.Int).Value = r.AccountClassId;
        c.Parameters.Add("@" + p + "v", System.Data.SqlDbType.VarChar, 50).Value = Db(Clean(p, r.Vendor));
        c.Parameters.Add("@" + p + "f", System.Data.SqlDbType.SmallInt).Value = r.DebitCreditFlag;
        c.Parameters.Add("@" + p + "t", System.Data.SqlDbType.VarChar, 50).Value = Db(Clean(p, r.DefaultAccountType));
    }

    private static void BindSales(SqlCommand c, SalesAdjustRow r, string p)
    {
        c.Parameters.Add("@" + p + "b", System.Data.SqlDbType.Int).Value = r.BatchId;
        c.Parameters.Add("@" + p + "ca", System.Data.SqlDbType.Int).Value = r.CreditAccountId;
        c.Parameters.Add("@" + p + "amt", System.Data.SqlDbType.Money).Value = Db(r.SalesAmount);
        c.Parameters.Add("@" + p + "oa", System.Data.SqlDbType.VarChar, 100).Value = Db(Clean(p, r.OffsetAccount));
        c.Parameters.Add("@" + p + "oc", System.Data.SqlDbType.Int).Value = r.OffsetAccountClassId;
        c.Parameters.Add("@" + p + "or", System.Data.SqlDbType.VarChar, 100).Value = Db(Clean(p, r.OffsetDistReference));
    }
}
