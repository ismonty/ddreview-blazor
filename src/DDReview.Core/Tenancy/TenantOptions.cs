namespace DDReview.Core.Tenancy;

public sealed class TenantOptions
{
    public const string Section = "Tenancy";

    /// <summary>Connection string to the DDReview-CentralMaster database (keep in user-secrets / environment, never in source).</summary>
    public string CentralConnectionString { get; set; } = "";

    /// <summary>
    /// TEST ONLY: when set, every login uses this client database instead of the connection string stored in central.
    /// Lets the new app run against a restored copy without touching live client servers.
    /// </summary>
    public string? ClientConnectionStringOverride { get; set; }

    /// <summary>Set Encrypt=true on client connections (recommended). Old on-site servers may need TrustServerCertificate until their certificates are fixed.</summary>
    public bool EncryptClientConnections { get; set; } = true;
    public bool TrustClientServerCertificate { get; set; } = false;

    /// <summary>When true and dbo.tblWebUserCredential exists in central, hashes are used and legacy passwords are upgraded on first login.</summary>
    public bool UseCredentialTable { get; set; } = false;
}
