namespace DDReview.Core.Tenancy;

/// <summary>One row of dbo.tblWebUserSecurity in the central master database (only the columns the web app needs).</summary>
public sealed record CentralAccount(
    int WebUserId,
    string UserName,
    string? LegacyPassword,
    string? PasswordHash,
    string? ClientDsn,
    string? ClientUser,
    string? ClientPassword,
    int MajorVersionNumber,
    DateTime? LicensedStartDate,
    DateTime? LicensedEndDate,
    bool VoidsActivated,
    bool DdReviewSubscriber,
    bool MontySoftConsultingCustomer,
    bool PayPalEnabledCustomer,
    string? RedirectUrl);
