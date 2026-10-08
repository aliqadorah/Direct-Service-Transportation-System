using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Identity;

// All identifiers are URL tokens (SEC-22); RowVersion is base64 for optimistic concurrency.

public sealed record SignUpRequest(string FullName, string Email, string Password, string OrganisationName, bool AcceptTerms, string? Language);
public sealed record EmailRequest(string Email);
public sealed record SignInRequest(string Email, string Password);

/// <summary>Next step after a correct password: "twoFactor" or "twoFactorSetup".</summary>
public sealed record SignInResponse(string Next);

public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);
public sealed record TwoFactorCodeRequest(string? Code, string? RecoveryCode);
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes, MeResponse Me);
public sealed record LinkTokenRequest(string User, string Token);
public sealed record PasswordResetConfirmRequest(string User, string Token, string NewPassword);
public sealed record AntiforgeryResponse(string HeaderName, string Token);

public sealed record CompanyAccess(string Id, string Name, string? NameEn, string Status, string Role, bool IsPresetRole, IReadOnlyList<string> Permissions);

public sealed record MeResponse(
    string Id, string FullName, string Email, string LanguageCode, string DateFormat, string? DefaultCompany,
    bool IsPlatformStaff, string? TenantName, string? TenantStatus, IReadOnlyList<CompanyAccess> Companies);

public sealed record PreferencesRequest(string LanguageCode, string? DefaultCompany, string DateFormat);
public sealed record SessionResponse(string Id, DateTime CreatedAt, DateTime LastSeenAt, string? IpAddress, string? UserAgent, bool IsCurrent);

// ---- users & roles (users.manage)

public sealed record UserListItem(
    string Id, string FullName, string Email, string Status, Ref? Role, bool IsLocked, bool TwoFactorEnabled,
    bool EmailConfirmed, DateTime? LastSignInAt, string RowVersion);

public sealed record AssignRoleRequest(string Role);
public sealed record ReasonRequest(string Reason);

public sealed record PermissionItem(string Key, string PrdName, string Group, string DescriptionAr, string DescriptionEn);
public sealed record RoleResponse(string Id, string Name, bool IsPreset, IReadOnlyList<string> Permissions, int UsersCount, string RowVersion);
public sealed record RoleRequest(string Name, IReadOnlyList<string> Permissions, string? RowVersion);

public sealed record InvitationResponse(string Id, string Email, Ref Role, DateTime ExpiresAt, DateTime? AcceptedAt, string Status);
public sealed record InviteRequest(string Email, string Role);
public sealed record InvitationInfoResponse(string CompanyName, string Email, bool IsValid, bool HasAccount);
public sealed record InvitationAcceptRequest(string Token, string? FullName, string? Password);
