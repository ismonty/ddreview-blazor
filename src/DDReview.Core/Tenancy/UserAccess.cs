using Microsoft.Data.SqlClient;

namespace DDReview.Core.Tenancy;

public enum ReportKind { Sales, Receipts, Costs, Payroll, ServerReport, Marketing, Flash, DailyRecap, MonthlyRecap, DebitCredit }

/// <summary>Which stores a user may see for each report (client database: tblWebUserSecurity + tblWebUserSecurityStores).</summary>
public sealed class UserAccess
{
    public int ClientWebUserId { get; init; }
    public string UserName { get; init; } = "";
    public bool Administrator { get; init; }
    public Dictionary<ReportKind, HashSet<int>> Stores { get; } = Enum.GetValues<ReportKind>().ToDictionary(k => k, _ => new HashSet<int>());

    public bool CanSee(ReportKind kind) => Stores[kind].Count > 0;
    public string StoreListCsv(ReportKind kind) => string.Join(",", Stores[kind].OrderBy(x => x));
}

public sealed class ClientAccessService
{
    /// <summary>
    /// Mirrors the old second login step: the central record supplies a client-side user name and password,
    /// which is looked up in the client database. Parameterized (the old version concatenated strings).
    /// </summary>
    public async Task<UserAccess?> LoadAsync(string clientConnectionString, string clientUser, string clientPassword, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(clientConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
select wus.WebUserID, wus.UserName, wus.Administrator, wuss.StoreID,
       wuss.AuthorizedSales, wuss.AuthorizedReceipts, wuss.AuthorizedCosts, wuss.AuthorizedPayroll, wuss.AuthorizedServerReport,
       wuss.AuthorizedMarketing, wuss.AuthorizedFlash, wuss.AuthorizedDailyRecap, wuss.AuthorizedMonthlyRecap, wuss.AuthorizedDebitCreditReport
from dbo.tblWebUserSecurity wus
left join dbo.tblWebUserSecurityStores wuss on wuss.WebUserID = wus.WebUserID
where upper(wus.UserName) = upper(@u) and upper(wus.[Password]) = upper(@p)
order by wuss.StoreID", cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@u", System.Data.SqlDbType.VarChar, 50).Value = clientUser;
        cmd.Parameters.Add("@p", System.Data.SqlDbType.VarChar, 50).Value = clientPassword;

        UserAccess? access = null;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            access ??= new UserAccess { ClientWebUserId = (int)r["WebUserID"], UserName = (string)r["UserName"], Administrator = (bool)r["Administrator"] };
            if (r["StoreID"] is DBNull) continue;
            var id = (int)r["StoreID"];
            void Add(ReportKind k, string col) { if ((bool)r[col]) access.Stores[k].Add(id); }
            Add(ReportKind.Sales, "AuthorizedSales"); Add(ReportKind.Receipts, "AuthorizedReceipts");
            Add(ReportKind.Costs, "AuthorizedCosts"); Add(ReportKind.Payroll, "AuthorizedPayroll");
            Add(ReportKind.ServerReport, "AuthorizedServerReport"); Add(ReportKind.Marketing, "AuthorizedMarketing");
            Add(ReportKind.Flash, "AuthorizedFlash"); Add(ReportKind.DailyRecap, "AuthorizedDailyRecap");
            Add(ReportKind.MonthlyRecap, "AuthorizedMonthlyRecap"); Add(ReportKind.DebitCredit, "AuthorizedDebitCreditReport");
        }
        return access;
    }
}
