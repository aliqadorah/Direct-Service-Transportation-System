namespace DirectService.Platform.Application.Notifications;

/// <summary>
/// The template names stored in <c>core.EmailOutbox.Template</c> (resend, per-recipient and test-mail throttles, the console filter). Pinned
/// literals (platform design §e, E-4): they equal the method names of <see cref="EmailTemplates"/> before the restructuring, but never follow
/// a method rename. Never change a value.
/// </summary>
public static class EmailTemplateNames
{
    public const string VerifyEmail = "VerifyEmail";
    public const string TwoFactorChanged = "TwoFactorChanged";
    public const string ConfirmEmailChange = "ConfirmEmailChange";
    public const string EmailChangeAsked = "EmailChangeAsked";
    public const string EmailChanged = "EmailChanged";
    public const string PasswordReset = "PasswordReset";
    public const string Invitation = "Invitation";
    public const string StaffInvitation = "StaffInvitation";
    public const string SupportDecision = "SupportDecision";
    public const string SupportEnded = "SupportEnded";
    public const string TenantDeleted = "TenantDeleted";
    public const string TestEmail = "TestEmail";
    public const string SystemEmailFailures = "SystemEmailFailures";
    public const string SecretExpiring = "SecretExpiring";
}
