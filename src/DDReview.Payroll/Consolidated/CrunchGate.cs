using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace DDReview.Payroll.Consolidated;

/// <summary>
/// Each crunch procedure empties and refills ONE shared work table that a function then reads, so two runs on the same table
/// (in one database) could read each other's rows. Runs are queued per database and work table inside this program.
/// This does not protect against a second web server, or against the Access program running at the same moment.
/// </summary>
internal static class CrunchGate
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static SemaphoreSlim For(string connectionString, string workTable)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        return Gates.GetOrAdd($"{b.DataSource}|{b.InitialCatalog}|{workTable}".ToLowerInvariant(), _ => new SemaphoreSlim(1, 1));
    }
}
