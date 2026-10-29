namespace DirectService.Platform.Domain;

/// <summary>
/// core.SystemEmailSettings — Platform-wide (revision 6, system email, DT-250). How the platform sends its emails: the development folder or
/// Microsoft 365 (Microsoft Graph, an Entra app with a client secret). At most one row is Active (used for sending) and one Pending (saved by
/// platform staff, used only once a test email with it has succeeded); an activated row replaces the Active one, which becomes Retired.
/// The client secret is stored encrypted (Data Protection) and never returned or logged.
/// </summary>
[StoredName("CORE.SystemEmailSetting")]
[TokenLabel("SystemEmailSetting")]
public class SystemEmailSetting : AuditableEntity
{
    /// <summary>Active / Pending / Retired.</summary>
    public string State { get; set; } = "Pending";
    /// <summary>Development (save to folder) / Microsoft365; another provider is a new mode.</summary>
    public string Mode { get; set; } = "Development";
    /// <summary>Microsoft 365: the directory (tenant) ID of the Entra app.</summary>
    public string? EntraTenantId { get; set; }
    /// <summary>Microsoft 365: the application (client) ID of the Entra app.</summary>
    public string? EntraClientId { get; set; }
    /// <summary>Microsoft 365: the client secret, encrypted with Data Protection; never returned by the API.</summary>
    public byte[]? ClientSecretEncrypted { get; set; }
    /// <summary>When the current secret was saved.</summary>
    public DateTime? SecretSavedAt { get; set; }
    /// <summary>The secret's expiry date as entered by staff (Entra shows it); warnings from 30 days before.</summary>
    public DateOnly? SecretExpiresOn { get; set; }
    /// <summary>The mailbox the emails are sent as (any domain of the Microsoft 365 tenant).</summary>
    public string? SenderMailbox { get; set; }
    /// <summary>Sender name shown to recipients.</summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Where replies go (optional).</summary>
    public string? ReplyTo { get; set; }
    /// <summary>When the row became Active.</summary>
    public DateTime? ActivatedAt { get; set; }
    /// <summary>Last test email with these settings: when, whether it was sent and, if not, the reason (plain words, no secrets).</summary>
    public DateTime? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestError { get; set; }
    /// <summary>The expiry warning last sent to platform staff (30, then 7 days before); NULL = none yet for this secret.</summary>
    public int? ExpiryWarnedDays { get; set; }
}
