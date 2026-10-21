using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Identity;

// All identifiers are URL tokens (SEC-22); RowVersion is base64 for optimistic concurrency.

public sealed record SignUpRequest(string FullName, string Email, string Password, string OrganisationName, bool AcceptTerms, string? Language);
public sealed record EmailRequest(string Email);
/// <summary>Sign-in: <paramref name="Login"/> is the email or the username (revision 6); <paramref name="Email"/> is still accepted for older clients.</summary>
public sealed record SignInRequest(string? Email, string Password, string? Login = null);

/// <summary>
/// Next step after a correct password: "twoFactor", "twoFactorSetup" (required), "twoFactorOffer" (first sign-in; set up or POST /auth/2fa/skip),
/// "changePassword" or "done" (signed in; <paramref name="Me"/> set).
/// </summary>
public sealed record SignInResponse(string Next, MeResponse? Me = null);

/// <summary>The forced new password at sign-in (stage "changePassword").</summary>
public sealed record NewPasswordRequest(string NewPassword);

/// <summary>A signed-in user's own password change.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Authenticator set-up; <paramref name="QrSvg"/> is the QR code as SVG markup (profile set-up, revision 6).</summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri, string? QrSvg = null);
public sealed record TwoFactorCodeRequest(string? Code, string? RecoveryCode);
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes, MeResponse Me);

/// <summary>
/// The signed-in user's two-step verification (owner testing 19 Oct 2026, DT-210): on/off, whether it is required (the organisation's
/// "Require 2FA for all users", or platform staff — then it cannot be turned off) and how many unused recovery codes are left.
/// </summary>
public sealed record TwoFactorStatusResponse(bool Enabled, bool Required, bool RequiredForStaff, int RecoveryCodesLeft, DateTime? EnabledAt);

/// <summary>Proof for turning 2FA off, changing the authenticator app or new recovery codes: the current password and a current code or a recovery code.</summary>
public sealed record TwoFactorStepUpRequest(string? CurrentPassword, string? Code, string? RecoveryCode);
public sealed record LinkTokenRequest(string User, string Token);
public sealed record PasswordResetConfirmRequest(string User, string Token, string NewPassword);
public sealed record AntiforgeryResponse(string HeaderName, string Token);

/// <summary>A company of the user with role and permissions, and its currency (code and decimals) for amounts and defaults on every screen.</summary>
public sealed record CompanyAccess(string Id, string Name, string? NameEn, string Status, string Role, bool IsPresetRole, IReadOnlyList<string> Permissions,
    string CurrencyCode = "JOD", int CurrencyDecimals = 3);

public sealed record MeResponse(
    string Id, string FullName, string? Email, string LanguageCode, string DateFormat, string? DefaultCompany,
    bool IsPlatformStaff, string? TenantName, string? TenantStatus, IReadOnlyList<CompanyAccess> Companies,
    string? UserName = null, bool TwoFactorEnabled = false, string? Environment = null, string? PendingEmail = null);

public sealed record PreferencesRequest(string LanguageCode, string? DefaultCompany, string DateFormat);
public sealed record SessionResponse(string Id, DateTime CreatedAt, DateTime LastSeenAt, string? IpAddress, string? UserAgent, bool IsCurrent);

// ---- users & roles (users.manage)

public sealed record UserListItem(
    string Id, string FullName, string? Email, string Status, Ref? Role, bool IsLocked, bool TwoFactorEnabled,
    bool EmailConfirmed, DateTime? LastSignInAt, string RowVersion, string? UserName = null, bool MustChangePassword = false, string? PendingEmail = null);

/// <summary>
/// "Add user" → "Create with username and password" (revision 6): username unique platform-wide (letters, digits, . _ -), optional email,
/// a password that meets the tenant policy, the company role, and whether the user must choose a new password at first sign-in.
/// </summary>
public sealed record CreateUserRequest(string FullName, string UserName, string? Email, string Password, string Role, bool MustChangePassword = true);

/// <summary>The tenant's password rules (FR-175), shown under password fields and checked while typing (owner feedback 18 Oct 2026, DT-208).</summary>
public sealed record PasswordPolicyResponse(int MinLength, bool RequireLetter, bool RequireDigit);

/// <summary>An administrator sets a new password for a user created with a username (the only reset for users without email).</summary>
public sealed record SetPasswordRequest(string Password, bool MustChangePassword = true);

public sealed record AssignRoleRequest(string Role);

/// <summary>
/// DT-229: an Owner or Administrator changes a user's name and username in Users & roles (also their own). Username: same rules as at
/// creation, the generic "not available" answer; it may be cleared only when the user has an email to sign in with.
/// </summary>
public sealed record UpdateUserRequest(string? FullName, string? UserName, string? RowVersion);

/// <summary>
/// DT-229: the user asks to change their own email: the new address, the current password and, with two-step verification on, a current
/// code or a recovery code. A link goes to the new address; nothing changes until it is confirmed.
/// </summary>
public sealed record ChangeEmailRequest(string? NewEmail, string? CurrentPassword, string? Code = null, string? RecoveryCode = null);

/// <summary>DT-229: an Owner or Administrator starts an email change for a user; it takes effect only when that user confirms the link.</summary>
public sealed record StartEmailChangeRequest(string? NewEmail);

/// <summary>DT-229: the address in use and the one waiting for confirmation.</summary>
public sealed record EmailChangeResponse(string? Email, string? PendingEmail);
public sealed record ReasonRequest(string Reason);

public sealed record PermissionItem(string Key, string PrdName, string Group, string DescriptionAr, string DescriptionEn);
public sealed record RoleResponse(string Id, string Name, bool IsPreset, IReadOnlyList<string> Permissions, int UsersCount, string RowVersion);
public sealed record RoleRequest(string Name, IReadOnlyList<string> Permissions, string? RowVersion);

public sealed record InvitationResponse(string Id, string Email, Ref Role, DateTime ExpiresAt, DateTime? AcceptedAt, string Status);
public sealed record InviteRequest(string Email, string Role);
public sealed record InvitationInfoResponse(string CompanyName, string Email, bool IsValid, bool HasAccount);
public sealed record InvitationAcceptRequest(string Token, string? FullName, string? Password);
