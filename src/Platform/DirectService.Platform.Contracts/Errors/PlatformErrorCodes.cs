namespace DirectService.Platform.Contracts.Errors;

/// <summary>
/// Problem codes (API-06) of the platform: sign-in, accounts and users, roles, staff and provider set-up, tenants, companies, support access,
/// subscriptions, refunds and system e-mail. Each value is defined here only: <c>ErrorCodes</c> of the module contracts refers to these constants
/// under the same names, and the Arabic and English messages stay in its error catalogue (Stage 2 session 13, the DT-310 pattern). A value never
/// changes — it is the "code" field clients receive.
/// </summary>
public static class PlatformErrorCodes
{
    public const string TenantReadOnly = "tenant.readOnly";
    public const string CompanyArchived = "company.archived";
    public const string SupportReadOnly = "support.readOnly";
    public const string PlanLimitReached = "plan.limitReached";
    public const string SignInFailed = "auth.signInFailed";
    public const string SignInLocked = "auth.locked";
    public const string EmailNotConfirmed = "auth.emailNotConfirmed";
    public const string TwoFactorInvalid = "auth.twoFactorInvalid";
    public const string TwoFactorRequired = "auth.twoFactorRequired";
    public const string TwoFactorRequiredByPolicy = "auth.twoFactorRequiredByPolicy";
    public const string TwoFactorRequiredForStaff = "auth.twoFactorRequiredForStaff";
    public const string TwoFactorChangeExpired = "auth.twoFactorChangeExpired";
    public const string TwoFactorStepUpCode = "auth.twoFactorStepUpCode";
    public const string StepUpFailed = "auth.stepUpFailed";
    public const string SystemEmailFreshCode = "email.freshCode";
    public const string SystemEmailNotFailed = "email.notFailed";
    public const string SystemEmailNotResendable = "email.notResendable";
    public const string PasswordPolicy = "auth.passwordPolicy";
    public const string PasswordPolicyLetter = "auth.passwordPolicyLetter";
    public const string PasswordPolicyDigit = "auth.passwordPolicyDigit";
    public const string PasswordPolicyLength = "auth.passwordPolicyLength";
    public const string TokenInvalid = "auth.tokenInvalid";
    public const string EmailInUse = "user.emailInUse";
    public const string UserNameInUse = "user.userNameInUse";
    public const string NotAvailable = "user.notAvailable";
    public const string EmailUnchanged = "user.emailUnchanged";
    public const string OwnerOrAdministratorOnly = "user.ownerOrAdministratorOnly";
    public const string EmailOwnInProfile = "user.emailOwnInProfile";
    public const string UserNameNotForEmailUser = "user.userNameNotForEmailUser";
    public const string VerifySignInRequired = "auth.verifySignIn";
    public const string UserNameFormat = "user.userNameFormat";
    public const string UserHasNoEmail = "user.noEmail";
    public const string UserEmailNotVerified = "user.emailNotVerified";
    public const string PasswordUnchanged = "auth.passwordUnchanged";
    public const string PasswordSetNotAllowed = "user.passwordSetNotAllowed";
    public const string PasswordWrong = "auth.passwordWrong";
    public const string SetupCodeInvalid = "setup.codeInvalid";
    public const string SetupLocked = "setup.locked";
    public const string SetupSessionExpired = "setup.sessionExpired";
    public const string SetupBusy = "setup.busy";
    public const string StaffSelf = "staff.self";
    public const string StaffLastActive = "staff.lastActive";
    public const string StaffNotPending = "staff.notPending";
    public const string StaffNotActive = "staff.notActive";
    public const string LastOwner = "user.lastOwner";
    public const string LastUserManager = "user.lastUserManager";
    public const string PresetRoleFixed = "role.presetFixed";
    public const string RoleInUse = "role.inUse";
    public const string LastActiveCompany = "company.lastActive";
    public const string SequenceBelowIssued = "numbering.belowIssued";
    public const string MigrationAlreadyComplete = "settings.migrationComplete";
    public const string ConfirmationRequired = "confirmation.required";
    public const string TenantUnavailable = "tenant.unavailable";
    public const string InvitationEmailMismatch = "invitation.emailMismatch";
    public const string Antiforgery = "auth.antiforgery";
    public const string EmailChangeLimitAdmin = "user.emailChangeLimitAdmin";
    public const string EmailChangeLimitUser = "user.emailChangeLimitUser";
    public const string SubscriptionNotUpgrade = "subscription.notUpgrade";
    public const string SubscriptionNotDowngrade = "subscription.notDowngrade";
    public const string SubscriptionFreePlan = "subscription.freePlan";
    public const string SubscriptionDowngradeExceeds = "subscription.downgradeExceeds";
    public const string SubscriptionClosed = "subscription.closed";
    public const string SubscriptionInvoiceNotDue = "subscription.invoiceNotDue";
    public const string SubscriptionNotAwaitingPayment = "subscription.notAwaitingPayment";
    public const string RefundNotDue = "refund.notDue";
    public const string RefundAlreadyMarked = "refund.alreadyMarked";
    public const string RefundBusy = "refund.busy";
    public const string RefundDate = "refund.date";
    public const string RefundReference = "refund.reference";
    public const string SupportWindow = "support.window";
    public const string SupportNotRequested = "support.notRequested";
    public const string SupportRequestNotFound = "support.requestNotFound";
    public const string SupportNotOpen = "support.notOpen";
    public const string SupportEndedBySubscriber = "support.endedBySubscriber";
    public const string SupportWindowExpired = "support.windowExpired";
    public const string SupportNotApproved = "support.notApproved";
}
