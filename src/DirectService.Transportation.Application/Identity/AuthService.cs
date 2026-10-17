using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>What the client must do after a correct password (FR-177; revision 6 adds the forced password change and sign-in without 2FA).</summary>
public enum SignInNextStep
{
    /// <summary>The user has 2FA: enter the authenticator code (or a recovery code).</summary>
    TwoFactor,

    /// <summary>2FA is required and not set up yet: set up the authenticator app before anything else.</summary>
    TwoFactorSetup,

    /// <summary>First sign-in without 2FA where it is not required (revision 6): set-up offered with "Skip for now".</summary>
    TwoFactorOffer,

    /// <summary>The administrator asked for a new password (created or reset by the administrator): choose one first.</summary>
    ChangePassword,

    /// <summary>No second step: the user is signed in.</summary>
    None,
}

/// <summary>Authenticator set-up data shown once to the user.</summary>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

/// <summary>
/// Sign-in with email or username + password, then two-factor authentication (SEC-01, SEC-02, FR-175, FR-177). Lockout: 5 failed
/// attempts (password or code) → 15 minutes, the same for every user. Every attempt is audited (FR-188: sign-in and failed sign-in).
/// Revision 6 (owner change requests 15 Oct 2026): 2FA is required of platform staff (always) and of every user of a tenant with
/// "Require 2FA for all users"; others are offered the set-up at their first sign-in ("Skip for now") and may turn it on later. A user
/// created by an administrator may have to choose a new password first. The API layer turns the results into cookies.
/// </summary>
public sealed class AuthService(UserManager<User> users, IAppDbContext db, IChangeContext changes, IDataScope scope, TimeProvider clock)
{
    public const string Issuer = "Direct Service Transportation";

    /// <summary>Step 1: password. <paramref name="login"/> is the email or the username. Returns the user and the next step, or throws a generic failure.</summary>
    public async Task<(User User, SignInNextStep Next)> CheckPasswordAsync(string login, string password, CancellationToken ct)
    {
        var user = string.IsNullOrWhiteSpace(login) ? null : await users.FindByNameAsync(login.Trim()).ConfigureAwait(false);
        if (user is null)
        {
            // Same cost as a real check so the response time does not reveal whether the email exists.
            new PasswordHasher<User>().HashPassword(new User(), password ?? string.Empty);
            throw new AppException(ErrorCodes.SignInFailed, 401);
        }

        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
            throw new AppException(ErrorCodes.SignInLocked, 423, null, MinutesLeft(user));

        if (!await users.CheckPasswordAsync(user, password ?? string.Empty).ConfigureAwait(false))
        {
            await RecordFailureAsync(user, "SignInFailed", ct).ConfigureAwait(false);
            if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
                throw new AppException(ErrorCodes.SignInLocked, 423, null, MinutesLeft(user));
            throw new AppException(ErrorCodes.SignInFailed, 401);
        }

        if (user.Status != "Active")
            throw new AppException(ErrorCodes.SignInFailed, 401);
        // A user created by an administrator with a username needs no email verification: the administrator vouches for the account.
        if (user.UserName is null && user.EmailConfirmedAt is null)
            throw new AppException(ErrorCodes.EmailNotConfirmed, 403);
        await EnsureTenantAllowsSignInAsync(user, ct).ConfigureAwait(false);

        return (user, user.MustChangePassword ? SignInNextStep.ChangePassword : await SecondStepAsync(user, ct).ConfigureAwait(false));
    }

    /// <summary>The step after the password (and after a forced password change): code, required set-up, offered set-up, or none.</summary>
    public async Task<SignInNextStep> SecondStepAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabledAt is not null) return SignInNextStep.TwoFactor;
        if (await RequiresTwoFactorAsync(user, ct).ConfigureAwait(false)) return SignInNextStep.TwoFactorSetup;
        // Offered once, at the first sign-in of a user who signs in with their email (sign-up, invitation); a user created by an
        // administrator with a username is not asked (the administrator chose; the profile recommends it).
        return user.LastSignInAt is null && user.UserName is null ? SignInNextStep.TwoFactorOffer : SignInNextStep.None;
    }

    /// <summary>Platform staff always need 2FA, without exception; tenant users when their tenant requires it for all users.</summary>
    public async Task<bool> RequiresTwoFactorAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (RequiresTwoFactor(user, tenantRequires: false)) return true;
        var required = await db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == user.TenantId).Select(t => t.RequireTwoFactor)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return RequiresTwoFactor(user, required);
    }

    /// <summary>The rule itself: platform staff, or the tenant's "Require 2FA for all users".</summary>
    public static bool RequiresTwoFactor(User user, bool tenantRequires)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsPlatformStaff || user.TenantId is null || tenantRequires;
    }

    /// <summary>A sign-in that ends without a code (no 2FA required, or the offered set-up skipped): audited, failure counter cleared.</summary>
    public async Task CompleteWithoutSecondFactorAsync(User user, bool skippedOffer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabledAt is not null || await RequiresTwoFactorAsync(user, ct).ConfigureAwait(false))
            throw new AppException(ErrorCodes.TwoFactorRequired, 401);   // checked again at the last moment (the tenant may require it now)
        if (skippedOffer)
        {
            using (scope.Use(user.TenantId, null, user.Id))
            {
                changes.SetAction(user, "TwoFactorSkipped");
                user.UpdatedAt = clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        await RecordSuccessAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The forced password change after the password step (<see cref="User.MustChangePassword"/>): the tenant policy applies, the new
    /// password must differ from the current one; the security stamp changes (other sessions end). Audited as "PasswordChanged".
    /// </summary>
    public async Task ChangeRequiredPasswordAsync(User user, string? newPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.MustChangePassword) throw new AppException(ErrorCodes.Forbidden, 403);
        await SetOwnPasswordAsync(user, newPassword, ct).ConfigureAwait(false);
    }

    /// <summary>A signed-in user changes their own password with the current one (users without an email have no reset link).</summary>
    public async Task ChangePasswordAsync(User user, string? currentPassword, string? newPassword, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
            throw new AppException(ErrorCodes.SignInLocked, 423, null, MinutesLeft(user));
        if (!await users.CheckPasswordAsync(user, currentPassword ?? string.Empty).ConfigureAwait(false))
        {
            await RecordFailureAsync(user, "SignInFailed", ct).ConfigureAwait(false);
            throw Fail.Field("currentPassword", ErrorCodes.PasswordWrong);
        }
        await SetOwnPasswordAsync(user, newPassword, ct).ConfigureAwait(false);
    }

    private async Task SetOwnPasswordAsync(User user, string? newPassword, CancellationToken ct)
    {
        var policy = await TenantPasswordValidator.GetPolicyAsync(db, user.TenantId, ct).ConfigureAwait(false);
        if (!policy.IsSatisfiedBy(newPassword)) throw AccountService.PolicyError(policy);
        if (await users.CheckPasswordAsync(user, newPassword!).ConfigureAwait(false)) throw Fail.Field("password", ErrorCodes.PasswordUnchanged);
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "PasswordChanged");
            var wasRequired = user.MustChangePassword;
            user.MustChangePassword = false;
            var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
            var result = await users.ResetPasswordAsync(user, token, newPassword!).ConfigureAwait(false);   // validators + new security stamp
            if (!result.Succeeded)
            {
                user.MustChangePassword = wasRequired;
                throw AccountService.PolicyError(policy);
            }
        }
    }

    /// <summary>Step 2 (enrolled users): authenticator code or one-time recovery code.</summary>
    public async Task VerifySecondFactorAsync(User user, string? code, string? recoveryCode, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
            throw new AppException(ErrorCodes.SignInLocked, 423, null, MinutesLeft(user));

        bool ok;
        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            ok = (await users.RedeemTwoFactorRecoveryCodeAsync(user, Common.TextNormalizer.ToAsciiDigits(recoveryCode).Replace(" ", string.Empty, StringComparison.Ordinal).Trim())
                .ConfigureAwait(false)).Succeeded;
        }
        else
        {
            ok = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(code))
                .ConfigureAwait(false);
        }

        if (!ok)
        {
            await RecordFailureAsync(user, "SignInFailed", ct).ConfigureAwait(false);
            if (await users.IsLockedOutAsync(user).ConfigureAwait(false))
                throw new AppException(ErrorCodes.SignInLocked, 423, null, MinutesLeft(user));
            throw new AppException(ErrorCodes.TwoFactorInvalid, 401, "code");
        }
        await RecordSuccessAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>First sign-in: creates (or returns) the authenticator key to scan.</summary>
    public async Task<AuthenticatorSetup> GetAuthenticatorSetupAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabledAt is not null) throw new AppException(ErrorCodes.Forbidden, 403);
        var key = await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
            key = await users.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);
        }
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email ?? user.UserName ?? string.Empty)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
        return new AuthenticatorSetup(key!, uri);
    }

    /// <summary>Completes the set-up: verifies the first code, enables 2FA and issues 10 recovery codes (shown once).</summary>
    public async Task<IReadOnlyList<string>> CompleteAuthenticatorSetupAsync(User user, string code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabledAt is not null) throw new AppException(ErrorCodes.Forbidden, 403);
        var ok = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(code)).ConfigureAwait(false);
        if (!ok)
        {
            await RecordFailureAsync(user, "SignInFailed", ct).ConfigureAwait(false);
            throw new AppException(ErrorCodes.TwoFactorInvalid, 401, "code");
        }
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "TwoFactorEnabled");
            await users.SetTwoFactorEnabledAsync(user, true).ConfigureAwait(false);
            var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10).ConfigureAwait(false);
            await RecordSuccessAsync(user, ct).ConfigureAwait(false);
            return codes?.ToList() ?? [];
        }
    }

    private async Task RecordFailureAsync(User user, string action, CancellationToken ct)
    {
        using (scope.Use(user.TenantId, null, null))
        {
            changes.SetAction(user, action);
            await users.AccessFailedAsync(user).ConfigureAwait(false);
        }
        _ = ct;
    }

    private async Task RecordSuccessAsync(User user, CancellationToken ct)
    {
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "SignIn");
            user.FailedSignInCount = 0;
            user.LockedUntil = null;
            user.LastSignInAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureTenantAllowsSignInAsync(User user, CancellationToken ct)
    {
        if (user.TenantId is null) return; // provider staff
        var status = await db.Set<Tenant>().IgnoreQueryFilters()
            .Where(t => t.Id == user.TenantId && !t.IsDeleted).Select(t => t.Status).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        switch (status)
        {
            case null or "Deleted":
                throw new AppException(ErrorCodes.TenantUnavailable, 403);
            case "Cancelled":
                // 5.2: during the retention period only the Owner may sign in, for data export.
                var isOwner = await db.Set<UserCompanyRole>().IgnoreQueryFilters()
                    .AnyAsync(r => r.UserId == user.Id && !r.IsDeleted && r.Role.Name == Contracts.Security.PresetRoles.Owner && r.Role.IsPreset, ct)
                    .ConfigureAwait(false);
                if (!isOwner) throw new AppException(ErrorCodes.TenantUnavailable, 403);
                break;
        }
    }

    private int MinutesLeft(User user) =>
        user.LockedUntil is { } until ? Math.Max(1, (int)Math.Ceiling((until - clock.GetUtcNow().UtcDateTime).TotalMinutes)) : 15;

    private static string Clean(string? code) => Common.TextNormalizer.DigitsOnly(code);
}
