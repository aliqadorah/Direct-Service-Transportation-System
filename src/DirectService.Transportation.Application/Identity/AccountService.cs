using System.Net.Mail;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Identity;

public sealed record SignUpResult(Guid TenantPublicId, Guid CompanyPublicId, Guid UserPublicId);

/// <summary>
/// Self-service sign-up (US-31, flow 9.1, P-17), email verification (SEC-04), password reset (FR-174,
/// SEC-03/04). Sign-up creates the tenant (Trial, 14 days), the Owner account, the first company with
/// its reference data, the 5 preset roles and the terms acceptances — in one transaction.
/// </summary>
public sealed class AccountService(
    IAppDbContext db,
    UserManager<User> users,
    IDataScope scope,
    IChangeContext changes,
    CompanyProvisioningService provisioning,
    SessionService sessions,
    IUrlTokenService urlTokens,
    IEmailSender email,
    TimeProvider clock,
    IOptions<AppOptions> options,
    IRequestContext ctx,
    SignInThrottle throttle)
{
    private static readonly string[] TenantFilter = ["Tenant"];

    public async Task<SignUpResult> SignUpAsync(SignUpRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var fullName = TextNormalizer.CleanText(r.FullName);
        var org = TextNormalizer.CleanText(r.OrganisationName);
        var mail = r.Email?.Trim();
        new Validator()
            .Require("fullName", fullName).MaxLength("fullName", fullName, 150)
            .Require("email", mail).Check(IsEmail(mail), "email").MaxLength("email", mail, 150)
            .Require("organisationName", org).MaxLength("organisationName", org, 200)
            .Require("password", r.Password)
            .Check(r.AcceptTerms, "acceptTerms", ErrorCodes.ValidationRequired)
            .ThrowIfInvalid();
        if (!PasswordPolicy.Default.IsSatisfiedBy(r.Password))
            throw PolicyError(PasswordPolicy.Default);
        if (await users.FindByEmailAsync(mail!).ConfigureAwait(false) is not null)
            throw Fail.Conflict(ErrorCodes.NotAvailable);   // DT-203: generic answer

        var now = clock.GetUtcNow().UtcDateTime;
        var lang = r.Language is "en" ? "en" : "ar";
        var result = await db.InTransactionAsync(async c =>
        {
            var plan = await db.Set<Plan>().SingleAsync(p => p.Code == "TRIAL", c).ConfigureAwait(false);
            var tenant = new Tenant
            {
                Name = org!, Status = "Trial", PlanId = plan.Id, BillingCycle = "Monthly",
                TrialEndsAt = now.AddDays(options.Value.TrialDays),
            };
            db.Set<Tenant>().Add(tenant);
            await db.SaveChangesAsync(c).ConfigureAwait(false);

            using var _ = scope.Use(tenant.Id, null, null);
            // Email = user name; CreateAsync fills NormalizedEmail (UserManager.Set* would persist an unsaved user).
            var user = new User { FullName = fullName!, LanguageCode = lang, TenantId = tenant.Id, Email = mail! };
            var created = await users.CreateAsync(user, r.Password).ConfigureAwait(false);
            if (!created.Succeeded) throw ToAppException(created, PasswordPolicy.Default);

            using var __ = scope.Use(tenant.Id, null, user.Id);
            var company = new Company { Name = org!, TenantId = tenant.Id };
            db.Set<Company>().Add(company);
            var roles = await CreatePresetRolesAsync(tenant.Id, c).ConfigureAwait(false);
            await db.SaveChangesAsync(c).ConfigureAwait(false);

            db.Set<UserCompanyRole>().Add(new UserCompanyRole { UserId = user.Id, CompanyId = company.Id, RoleId = roles[PresetRoles.Owner].Id });
            user.DefaultCompanyId = company.Id;
            db.Set<TermsAcceptance>().Add(new TermsAcceptance { UserId = user.Id, DocumentType = "Terms", DocumentVersion = options.Value.TermsVersion, AcceptedAt = now });
            db.Set<TermsAcceptance>().Add(new TermsAcceptance { UserId = user.Id, DocumentType = "Privacy", DocumentVersion = options.Value.TermsVersion, AcceptedAt = now });
            await db.SaveChangesAsync(c).ConfigureAwait(false);

            await provisioning.ProvisionAsync(company, user.Id, c).ConfigureAwait(false);
            return (tenant, company, user);
        }, ct).ConfigureAwait(false);

        await SendVerificationAsync(result.user, ct).ConfigureAwait(false);
        return new SignUpResult(result.tenant.PublicId, result.company.PublicId, result.user.PublicId);
    }

    /// <summary>Creates the 5 preset roles (PRD 3.5) of a new tenant with their permission keys (A-21).</summary>
    public async Task<Dictionary<string, Role>> CreatePresetRolesAsync(long tenantId, CancellationToken ct)
    {
        var permissions = await db.Set<Permission>().ToDictionaryAsync(p => p.PermissionKey, ct).ConfigureAwait(false);
        var roles = new Dictionary<string, Role>();
        foreach (var name in PresetRoles.All)
        {
            var role = new Role { TenantId = tenantId, Name = name, IsPreset = true };
            foreach (var key in PresetRoles.Permissions[name])
                role.RolePermissions.Add(new RolePermission { TenantId = tenantId, Permission = permissions[key] });
            db.Set<Role>().Add(role);
            roles[name] = role;
        }
        return roles;
    }

    // ---------------------------------------------------------------- email verification

    public async Task SendVerificationAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Email is null) return;   // a user created with a username and no email (revision 6)
        var token = await users.GenerateEmailConfirmationTokenAsync(user).ConfigureAwait(false);
        var link = Link("verify-email", urlTokens.Encode<User>(user.PublicId), token);
        await email.SendAsync(user.UserName is { } name
            ? EmailTemplates.VerifyAddedEmail(user.Email, user.FullName, name, link)
            : EmailTemplates.VerifyEmail(user.Email, user.FullName, link), ct).ConfigureAwait(false);
    }

    /// <summary>Anonymous resend; always succeeds silently (does not reveal whether the email exists).</summary>
    public async Task ResendVerificationAsync(string? address, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim()).ConfigureAwait(false);
        if (user is { EmailConfirmedAt: null, Status: "Active" } && !IsPendingStaff(user))
            await SendVerificationAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Confirms the email of the link. Owner decision 18 Oct 2026 (DT-203): an email entered by an administrator (a user created with a
    /// username) is confirmed only while that user is signed in — whoever receives a mistyped address cannot verify it and then reset the
    /// password. Self-registered and invited users chose their own address and sign in with it, so their link works before sign-in.
    /// </summary>
    public async Task VerifyEmailAsync(string userToken, string token, CancellationToken ct)
    {
        var user = await FindByTokenAsync(userToken, ct).ConfigureAwait(false) ?? throw Fail.Rule(ErrorCodes.TokenInvalid);
        if (user.EmailConfirmedAt is not null) return;
        var adminEntered = user.UserName is not null;
        if (adminEntered && ctx.UserId != user.Id) throw new AppException(ErrorCodes.VerifySignInRequired, 403);
        using var _ = scope.Use(user.TenantId, null, user.Id);
        changes.SetAction(user, "EmailVerified");
        var ok = await users.ConfirmEmailAsync(user, token ?? string.Empty).ConfigureAwait(false);
        if (!ok.Succeeded) throw Fail.Rule(ErrorCodes.TokenInvalid);
        // Single-use link (SEC-04). A signed-in user keeps their session: the confirmed email already makes the link a no-op.
        if (!adminEntered) await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- password reset

    /// <summary>Anonymous request; always succeeds silently.</summary>
    public async Task RequestPasswordResetAsync(string? address, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(address) ? null : await users.FindByEmailAsync(address.Trim()).ConfigureAwait(false);
        // Owner decision 17 Oct 2026: never to an email that is not verified yet (e.g. entered by an administrator) — only an
        // administrator's reset then. Platform staff keep their own flow (their invitation link completes the account).
        if (user is { Status: "Active" } && !IsPendingStaff(user) && (user.EmailConfirmedAt is not null || user.IsPlatformStaff))
            await SendPasswordResetAsync(user, ct).ConfigureAwait(false);
    }

    public async Task SendPasswordResetAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Email is null) return;   // no email: only an administrator can set a new password (revision 6)
        var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var link = Link("reset-password", urlTokens.Encode<User>(user.PublicId), token);
        await email.SendAsync(EmailTemplates.PasswordReset(user.Email, user.FullName, link), ct).ConfigureAwait(false);
    }

    public async Task ResetPasswordAsync(string userToken, string token, string newPassword, CancellationToken ct)
    {
        var user = await FindByTokenAsync(userToken, ct).ConfigureAwait(false) ?? throw Fail.Rule(ErrorCodes.TokenInvalid);
        var policy = await TenantPasswordValidator.GetPolicyAsync(db, user.TenantId, ct).ConfigureAwait(false);
        if (!policy.IsSatisfiedBy(newPassword)) throw PolicyError(policy);
        using var _ = scope.Use(user.TenantId, null, user.Id);
        changes.SetAction(user, "PasswordReset");
        var result = await users.ResetPasswordAsync(user, token ?? string.Empty, newPassword).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == TenantPasswordValidator.ErrorCode)) throw PolicyError(policy);
            throw Fail.Rule(ErrorCodes.TokenInvalid);
        }
        user.FailedSignInCount = 0;
        user.LockedUntil = null;
        throttle.ResetAll(user.Id);   // DT-204: every address's block too
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await sessions.RevokeAllAsync(user.Id, "Password reset", null, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// An invited platform staff member who has not used the invitation yet: completed only through the 48-hour invitation link, never
    /// through the anonymous reset or verification e-mails (security review 13 Oct 2026). The create-staff command sends its link itself.
    /// </summary>
    private static bool IsPendingStaff(User user) => user.IsPlatformStaff && string.IsNullOrEmpty(user.PasswordHash);

    private async Task<User?> FindByTokenAsync(string? userToken, CancellationToken ct)
    {
        var publicId = urlTokens.Decode<User>(userToken);
        if (publicId is null) return null;
        return await db.Set<User>().IgnoreQueryFilters(TenantFilter).SingleOrDefaultAsync(u => u.PublicId == publicId, ct).ConfigureAwait(false);
    }

    private string Link(string path, string userToken, string token) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{path}?u={Uri.EscapeDataString(userToken)}&t={Uri.EscapeDataString(token)}";

    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(' ', StringComparison.Ordinal)) return false;
        try
        {
            var a = new MailAddress(value);
            return a.Address == value && value.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static AppException PolicyError(PasswordPolicy p)
    {
        ArgumentNullException.ThrowIfNull(p);
        // DT-208: the message names exactly the rules of this organisation.
        var code = (p.RequireLetter, p.RequireDigit) switch
        {
            (true, true) => ErrorCodes.PasswordPolicy,
            (true, false) => ErrorCodes.PasswordPolicyLetter,
            (false, true) => ErrorCodes.PasswordPolicyDigit,
            _ => ErrorCodes.PasswordPolicyLength,
        };
        return Fail.Field("password", code, p.MinLength);
    }

    private static AppException ToAppException(IdentityResult result, PasswordPolicy policy) =>
        result.Errors.Any(e => e.Code == TenantPasswordValidator.ErrorCode) ? PolicyError(policy)
        : result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName") ? Fail.Conflict(ErrorCodes.NotAvailable)
        : Fail.Rule(ErrorCodes.ValidationFailed);
}
