using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>What the client must do after a correct password (mandatory 2FA, FR-177).</summary>
public enum SignInNextStep
{
    /// <summary>The user has 2FA: enter the authenticator code (or a recovery code).</summary>
    TwoFactor,

    /// <summary>First sign-in: set up the authenticator app before anything else.</summary>
    TwoFactorSetup,
}

/// <summary>Authenticator set-up data shown once to the user.</summary>
public sealed record AuthenticatorSetup(string SharedKey, string AuthenticatorUri);

/// <summary>
/// Sign-in with email + password, then mandatory two-factor authentication (SEC-01, SEC-02, FR-175,
/// FR-177). Lockout: 5 failed attempts (password or code) → 15 minutes. Every attempt is audited
/// (FR-188: sign-in and failed sign-in). The API layer turns the results into cookies.
/// </summary>
public sealed class AuthService(UserManager<User> users, IAppDbContext db, IChangeContext changes, IDataScope scope, TimeProvider clock)
{
    public const string Issuer = "Direct Service Transportation";

    /// <summary>Step 1: password. Returns the user and the next step, or throws a generic failure.</summary>
    public async Task<(User User, SignInNextStep Next)> CheckPasswordAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email ?? string.Empty).ConfigureAwait(false);
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
        if (user.EmailConfirmedAt is null)
            throw new AppException(ErrorCodes.EmailNotConfirmed, 403);
        await EnsureTenantAllowsSignInAsync(user, ct).ConfigureAwait(false);

        return (user, user.TwoFactorEnabledAt is null ? SignInNextStep.TwoFactorSetup : SignInNextStep.TwoFactor);
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
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
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
