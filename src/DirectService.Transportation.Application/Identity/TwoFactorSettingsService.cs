using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Identity;

/// <summary>
/// The signed-in user manages their own two-step verification from the profile (owner testing 19 Oct 2026, DT-210):
/// <list type="bullet">
/// <item>turn on — the existing set-up (QR code + key, first code, recovery codes shown once; <see cref="AuthService.CompleteAuthenticatorSetupAsync"/>);</item>
/// <item>turn off, change the authenticator app, new recovery codes — each needs the current password AND a current code or a recovery
/// code (<see cref="AuthService.VerifyStepUpAsync"/>);</item>
/// <item>turn off is refused while the organisation requires 2FA for all users, and always for platform staff;</item>
/// <item>a new app's key waits (10 minutes, server memory) until a code from the new app confirms it, so an abandoned change never locks the
/// user out; the confirmed key replaces the old one;</item>
/// <item>turning off and changing the app change the security stamp (every other session ends; the caller re-issues this session's
/// cookie); every change is audited and, with a verified email, announced by email.</item>
/// </list>
/// </summary>
public sealed partial class TwoFactorSettingsService(
    UserManager<User> users, IUserStore<User> store, AuthService auth, SessionService sessions, IChangeContext changes, IDataScope scope, IMemoryCache cache,
    IEmailSender email, IRequestContext ctx, TimeProvider clock, IOptions<AppOptions> options, ILogger<TwoFactorSettingsService> log)
{
    public static readonly TimeSpan ChangeWindow = TimeSpan.FromMinutes(10);
    public const int RecoveryCodeCount = 10;

    private const int MaxWrongCodes = 5;

    private sealed class PendingChange(string key, string stamp, long? sessionId)
    {
        public string Key { get; } = key;
        public string Stamp { get; } = stamp;
        public long? SessionId { get; } = sessionId;
        public int WrongCodes;
    }

    private static string PendingKey(long userId) => $"2fa-change:{userId}";

    public async Task<TwoFactorStatusResponse> StatusAsync(User user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var required = await auth.RequiresTwoFactorAsync(user, ct).ConfigureAwait(false);
        var left = user.TwoFactorEnabledAt is null ? 0 : await users.CountRecoveryCodesAsync(user).ConfigureAwait(false);
        return new TwoFactorStatusResponse(user.TwoFactorEnabledAt is not null, required, IsStaff(user), left, user.TwoFactorEnabledAt);
    }

    /// <summary>Turns 2FA off (key and recovery codes discarded). Refused when required; needs password + code.</summary>
    public async Task DisableAsync(User user, TwoFactorStepUpRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(r);
        if (IsStaff(user)) throw Fail.Rule(ErrorCodes.TwoFactorRequiredForStaff);
        if (await auth.RequiresTwoFactorAsync(user, ct).ConfigureAwait(false)) throw Fail.Rule(ErrorCodes.TwoFactorRequiredByPolicy);
        await auth.VerifyStepUpAsync(user, r.CurrentPassword, r.Code, r.RecoveryCode, ct).ConfigureAwait(false);
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "TwoFactorDisabled");
            await ((IUserTwoFactorStore<User>)store).SetTwoFactorEnabledAsync(user, false, ct).ConfigureAwait(false);
            await ((IUserAuthenticatorKeyStore<User>)store).SetAuthenticatorKeyAsync(user, users.GenerateNewAuthenticatorKey(), ct).ConfigureAwait(false);
            await ((IUserTwoFactorRecoveryCodeStore<User>)store).ReplaceCodesAsync(user, [], ct).ConfigureAwait(false);
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);   // saves; other sessions end
        }
        await sessions.RevokeAllAsync(user.Id, "Two-step verification turned off", ctx.SessionId, ct).ConfigureAwait(false);   // at once, not after the 30-s cache
        cache.Remove(PendingKey(user.Id));
        await NotifyAsync(user, "تم إيقاف التحقق بخطوتين", "Two-step verification was turned off", ct).ConfigureAwait(false);
    }

    /// <summary>Starts "Change authenticator app": password + code, then a new key to scan (not stored until confirmed).</summary>
    public async Task<AuthenticatorSetup> StartChangeAsync(User user, TwoFactorStepUpRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(r);
        await auth.VerifyStepUpAsync(user, r.CurrentPassword, r.Code, r.RecoveryCode, ct).ConfigureAwait(false);
        var key = users.GenerateNewAuthenticatorKey();
        cache.Set(PendingKey(user.Id), new PendingChange(key, user.SecurityStamp ?? string.Empty, ctx.SessionId), ChangeWindow);
        var label = Uri.EscapeDataString(AuthService.Issuer) + ":" + Uri.EscapeDataString(user.Email ?? user.UserName ?? string.Empty);
        return new AuthenticatorSetup(key, $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(AuthService.Issuer)}&digits=6");
    }

    /// <summary>Confirms the new app with one of its codes; the new key replaces the old one.</summary>
    public async Task ConfirmChangeAsync(User user, string? code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TwoFactorEnabledAt is null) throw new AppException(ErrorCodes.Forbidden, 403);
        // The pending key belongs to the session that started the change (security review 19 Oct 2026) and allows a few wrong codes only.
        if (!cache.TryGetValue<PendingChange>(PendingKey(user.Id), out var pending) || pending is null
            || !string.Equals(pending.Stamp, user.SecurityStamp, StringComparison.Ordinal) || pending.SessionId != ctx.SessionId)
            throw Fail.Rule(ErrorCodes.TwoFactorChangeExpired);
        var step = TotpCode.MatchStep(pending.Key, TextNormalizer.DigitsOnly(code), clock.GetUtcNow());
        if (step is null)
        {
            if (Interlocked.Increment(ref pending.WrongCodes) >= MaxWrongCodes) cache.Remove(PendingKey(user.Id));
            await auth.RecordStepUpFailureAsync(user, ct).ConfigureAwait(false);   // counts towards the lockout like any wrong code
            throw Fail.Field("code", ErrorCodes.TwoFactorInvalid);
        }
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "TwoFactorAppChanged");
            await ((IUserAuthenticatorKeyStore<User>)store).SetAuthenticatorKeyAsync(user, pending.Key, ct).ConfigureAwait(false);
            user.TwoFactorLastStep = step;   // DT-212: after the new key (which starts fresh): the confirming code cannot be used again
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);   // saves; other sessions end
        }
        await sessions.RevokeAllAsync(user.Id, "Authenticator app changed", ctx.SessionId, ct).ConfigureAwait(false);   // at once, not after the 30-s cache
        cache.Remove(PendingKey(user.Id));
        await NotifyAsync(user, "تم تغيير تطبيق المصادقة", "The authenticator app was changed", ct).ConfigureAwait(false);
    }

    /// <summary>New recovery codes (the old ones stop working); password + code. Returned once.</summary>
    public async Task<IReadOnlyList<string>> NewRecoveryCodesAsync(User user, TwoFactorStepUpRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(r);
        await auth.VerifyStepUpAsync(user, r.CurrentPassword, r.Code, r.RecoveryCode, ct).ConfigureAwait(false);
        IReadOnlyList<string> codes;
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "RecoveryCodesRenewed");
            user.UpdatedAt = clock.GetUtcNow().UtcDateTime;   // the codes live in their own table: touch the user so the change is audited
            codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false))?.ToList() ?? [];
        }
        await NotifyAsync(user, "تم إصدار رموز استرداد جديدة وإلغاء القديمة", "New recovery codes were issued and the old ones cancelled", ct).ConfigureAwait(false);
        return codes;
    }

    /// <summary>The notice after turning 2FA on (the set-up itself lives in <see cref="AuthService"/>).</summary>
    public Task NotifyEnabledAsync(User user, CancellationToken ct) =>
        NotifyAsync(user, "تم تفعيل التحقق بخطوتين", "Two-step verification was turned on", ct);

    private async Task NotifyAsync(User user, string whatAr, string whatEn, CancellationToken ct)
    {
        if (user.Email is null || user.EmailConfirmedAt is null) return;   // only a verified address (DT-203)
        var link = options.Value.PublicBaseUrl.TrimEnd('/') + "/sign-in";
        try
        {
            await email.SendAsync(EmailTemplates.TwoFactorChanged(user.Email, user.FullName, whatAr, whatEn, clock.GetUtcNow().UtcDateTime, ctx.IpAddress, link, user.LanguageCode), ct)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // the change is saved and audited; a mail failure must not cost the user their session or the codes shown once
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            NoticeFailed(log, ex, user.Id);
        }
    }

    [LoggerMessage(EventId = 9201, Level = LogLevel.Warning, Message = "The two-step verification notice to user {UserId} could not be sent.")]
    private static partial void NoticeFailed(ILogger logger, Exception ex, long userId);

    private static bool IsStaff(User user) => user.IsPlatformStaff || user.TenantId is null;
}
