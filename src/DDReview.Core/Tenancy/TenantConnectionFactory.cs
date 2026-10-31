using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace DDReview.Core.Tenancy;

public interface ITenantConnectionFactory
{
    /// <summary>Builds the (not yet opened) connection string for the signed-in user's client database.</summary>
    Task<string> GetClientConnectionStringAsync(int webUserId, CancellationToken ct = default);
}

public sealed class TenantConnectionFactory(ICentralDirectory central, IOptions<TenantOptions> options) : ITenantConnectionFactory
{
    private readonly TenantOptions _o = options.Value;

    public async Task<string> GetClientConnectionStringAsync(int webUserId, CancellationToken ct = default)
    {
        string raw;
        if (!string.IsNullOrWhiteSpace(_o.ClientConnectionStringOverride))
            raw = _o.ClientConnectionStringOverride!;
        else
        {
            var account = await central.GetByIdAsync(webUserId, ct)
                ?? throw new InvalidOperationException("Unknown account.");
            raw = account.ClientDsn ?? throw new InvalidOperationException("Account has no client database.");
        }
        return Harden(raw);
    }

    /// <summary>Applies app-wide safety settings without changing where the connection points.</summary>
    public string Harden(string raw)
    {
        var b = new SqlConnectionStringBuilder(raw)
        {
            ApplicationName = "DDReview.Blazor",
            MultipleActiveResultSets = false,
        };
        if (b.ConnectTimeout is 0 or > 30) b.ConnectTimeout = 15;
        if (!_o.EncryptClientConnections) b.Encrypt = SqlConnectionEncryptOption.Optional;
        if (_o.TrustClientServerCertificate) b.TrustServerCertificate = true;
        return b.ConnectionString;
    }
}
