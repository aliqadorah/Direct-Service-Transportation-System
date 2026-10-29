namespace DirectService.Shared.Infrastructure.Security;

/// <summary>
/// Data Protection purposes of the values stored encrypted (platform design §e, E-23). A purpose is part of the key derivation: changing one
/// makes every stored value of that kind unreadable (second-factor secrets, e-invoicing credentials, the Microsoft 365 secret, outbox
/// payloads). Pinned literals; never change a value.
/// </summary>
public static class DataProtectionPurposes
{
    public const string TwoFactorSecret = "DST.Users.TwoFactorSecret.v1";
    public const string EInvoiceCredentials = "DST.EInvoice.Credentials.v1";
    public const string SystemEmailClientSecret = "DST.SystemEmail.ClientSecret.v1";
    public const string SystemEmailOutbox = "DST.SystemEmail.Outbox.v1";
}
