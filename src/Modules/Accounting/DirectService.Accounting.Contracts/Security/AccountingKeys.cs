namespace DirectService.Accounting.Contracts.Security;

/// <summary>
/// The accounting permission keys the posting engine uses (Stage 3 session 4a). Each value is defined here only; <c>PermissionKeys</c> of the
/// Transportation contracts names it under the same name (values pinned, design e.2). The other acc.* keys join with their services (session 11).
/// </summary>
public static class AccountingPermissionKeys
{
    /// <summary>Export journal entries and retry pending postings; its holders learn that an entry waits (FR-190).</summary>
    public const string JeExport = "acc.je.export";
}

/// <summary>
/// The accounting notification types the posting engine raises (Stage 3 session 4a); the words and links are still given by the transport
/// module's notification provider until session 11 (stored value, design e.2).
/// </summary>
public static class AccountingNotificationTypes
{
    public const string PostingFailed = "ACC.PostingFailed";
}
