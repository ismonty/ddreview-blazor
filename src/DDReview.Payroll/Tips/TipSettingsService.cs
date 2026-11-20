using System.Data;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll.Tips;

public sealed record TipDepartment(int Id, string Name, bool Excluded);
/// <summary>A receipt type and whether a service charge paid that way carries the card fee. Override is null when the default (ReceiptClass 2 only) applies.</summary>
public sealed record TipReceiptType(int Id, string Name, int ReceiptClass, bool? Override)
{
    public bool ChargesFee => Override ?? ReceiptClass == 2;
}
public sealed record TipSettings(int StoreId, decimal CardFeePercent, IReadOnlyList<TipDepartment> Departments, IReadOnlyList<TipReceiptType> ReceiptTypes);

/// <summary>Per-store settings for dbo.fn_TipsByEmployee (script 020): the card fee rate, departments left out, and which receipt types carry the fee.</summary>
public sealed class TipSettingsService
{
    public const decimal DefaultCardFeePercent = 1.75m;

    public static string? Validate(decimal cardFeePercent) =>
        cardFeePercent < 0 || cardFeePercent > 100 ? "The card fee percent must be between 0 and 100." : null;

    public async Task<TipSettings> LoadAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, int storeId, CancellationToken ct = default)
    {
        Require(authorizedStoreIds, storeId);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        decimal fee = DefaultCardFeePercent;
        await using (var c = new SqlCommand("select CardFeePercent from dbo.tblTipPayrollSettings where StoreID = @s", cn))
        {
            c.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            var o = await c.ExecuteScalarAsync(ct);
            if (o is decimal d) fee = d;
        }
        var depts = new List<TipDepartment>();
        await using (var c = new SqlCommand(@"
select sd.StaffDepartmentID, sd.StaffDepartmentName, case when ex.StaffDepartmentID is null then 0 else 1 end
from dbo.tblStaffDepartments sd
left outer join dbo.tblTipPayrollExcludedDepartments ex on ex.StoreID = sd.StoreID and ex.StaffDepartmentID = sd.StaffDepartmentID
where sd.StoreID = @s order by sd.StaffDepartmentName", cn))
        {
            c.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            await using var r = await c.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) depts.Add(new TipDepartment(r.GetInt32(0), r.GetString(1), r.GetInt32(2) == 1));
        }
        var types = new List<TipReceiptType>();
        await using (var c = new SqlCommand(@"
select rt.ReceiptTypeID, rt.ReceiptType, rt.ReceiptClass, rf.ChargesFee
from dbo.tblReceiptTypes rt
left outer join dbo.tblTipPayrollReceiptTypeFee rf on rf.StoreID = rt.StoreID and rf.ReceiptTypeID = rt.ReceiptTypeID
where rt.StoreID = @s and rt.Enabled <> 0 order by rt.Sorter, rt.ReceiptType", cn))
        {
            c.Parameters.Add("@s", SqlDbType.Int).Value = storeId;
            await using var r = await c.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) types.Add(new TipReceiptType(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetBoolean(3)));
        }
        return new TipSettings(storeId, fee, depts, types);
    }

    /// <summary>Replaces the store's settings. Only departments and receipt types of that store are accepted; overrides equal to the default are not stored.</summary>
    public async Task SaveAsync(string connectionString, IReadOnlySet<int> authorizedStoreIds, TipSettings s, CancellationToken ct = default)
    {
        Require(authorizedStoreIds, s.StoreId);
        if (Validate(s.CardFeePercent) is { } bad) throw new ArgumentException(bad);
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        async Task Exec(string sql, Action<SqlParameterCollection>? p = null)
        {
            await using var c = new SqlCommand(sql, cn, tx);
            c.Parameters.Add("@s", SqlDbType.Int).Value = s.StoreId;
            p?.Invoke(c.Parameters);
            await c.ExecuteNonQueryAsync(ct);
        }
        await Exec(@"
update dbo.tblTipPayrollSettings set CardFeePercent = @f where StoreID = @s;
if @@rowcount = 0 insert dbo.tblTipPayrollSettings (StoreID, CardFeePercent) values (@s, @f);", p => p.Add("@f", SqlDbType.Decimal).Value = s.CardFeePercent);
        await Exec("delete from dbo.tblTipPayrollExcludedDepartments where StoreID = @s");
        foreach (var d in s.Departments.Where(d => d.Excluded))
            await Exec(@"insert dbo.tblTipPayrollExcludedDepartments (StoreID, StaffDepartmentID)
select StoreID, StaffDepartmentID from dbo.tblStaffDepartments where StoreID = @s and StaffDepartmentID = @d", p => p.Add("@d", SqlDbType.Int).Value = d.Id);
        await Exec("delete from dbo.tblTipPayrollReceiptTypeFee where StoreID = @s");
        foreach (var t in s.ReceiptTypes.Where(t => t.Override is not null && t.Override != (t.ReceiptClass == 2)))
            await Exec(@"insert dbo.tblTipPayrollReceiptTypeFee (StoreID, ReceiptTypeID, ChargesFee)
select StoreID, ReceiptTypeID, @c from dbo.tblReceiptTypes where StoreID = @s and ReceiptTypeID = @t", p =>
            { p.Add("@t", SqlDbType.Int).Value = t.Id; p.Add("@c", SqlDbType.Bit).Value = t.Override; });
        await tx.CommitAsync(ct);
    }

    static void Require(IReadOnlySet<int> authorized, int storeId)
    {
        if (!authorized.Contains(storeId)) throw new UnauthorizedAccessException("You are not authorized for that store.");
    }
}
