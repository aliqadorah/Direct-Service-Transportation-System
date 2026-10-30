using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Contracts.Errors;
using DirectService.Platform.Contracts.Security;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Email address changes (owner testing 21 Oct 2026, DT-229). The user asks in the profile with the current password (and a current code
/// or a recovery code when two-step verification is on); an Owner or Administrator may start a change for another user in Users &amp;
/// roles. Either way the new address is only <c>PendingEmail</c>: a link goes to it, and the change takes effect when the account's user,
/// signed in, confirms it — until then sign-in and password reset keep the current address. On confirmation the user's other sessions end
/// and the previous address gets a notice. The new address must be free (generic "not available"). Every step is audited.
/// </summary>
public sealed partial class EmailChangeService(
    IAppDbContext db,
    UserManager<User> users,
    IDataScope scope,
    IChangeContext changes,
    AuthService auth,
    UserAdminService admin,
    SessionService sessions,
    IUrlTokenService urlTokens,
    IEmailSender email,
    TimeProvider clock,
    IOptions<AppOptions> options,
    IRequestContext ctx,
    ILogger<EmailChangeService> log)
{
    private static readonly string[] TenantFilter = ["Tenant"];

    /// <summary>The signed-in user's own request (profile).</summary>
    public async Task<EmailChangeResponse> RequestOwnAsync(ChangeEmailRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var user = await SelfAsync(ct).ConfigureAwait(false);
        var mail = Address(r.NewEmail);
        // The user's own address reveals nothing: refused before a recovery code could be used up (security review 21 Oct 2026, F6).
        if (string.Equals(user.Email, mail, StringComparison.OrdinalIgnoreCase)) throw Fail.Field("newEmail", PlatformErrorCodes.EmailUnchanged);
        // Re-authentication next: without it nobody learns whether an address is taken.
        await auth.VerifyReauthenticationAsync(user, r.CurrentPassword, r.Code, r.RecoveryCode, ct).ConfigureAwait(false);
        await StartAsync(user, mail, byAdministrator: false, ct).ConfigureAwait(false);
        return new EmailChangeResponse(user.Email, user.PendingEmail);
    }

    /// <summary>An Owner / Administrator starts a change for another user (their own address is changed in their profile, with the password).</summary>
    public async Task<EmailChangeResponse> StartForUserAsync(string userToken, StartEmailChangeRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var user = await admin.AccountTargetAsync(userToken, ct).ConfigureAwait(false);
        if (user.Id == ctx.UserId) throw Fail.Rule(PlatformErrorCodes.EmailOwnInProfile);
        var mail = Address(r.NewEmail);
        await EnsureDailyLimitsAsync(user.Id, ct).ConfigureAwait(false);
        await StartAsync(user, mail, byAdministrator: true, ct).ConfigureAwait(false);
        return new EmailChangeResponse(user.Email, user.PendingEmail);
    }

    /// <summary>Owner decision 21 Oct 2026 (DT-230, security review F9): administrator-started changes per 24 hours, by the administrator and by the user.</summary>
    public const int DailyLimitPerAdministrator = 5;
    public const int DailyLimitPerUser = 3;

    private async Task EnsureDailyLimitsAsync(long targetId, CancellationToken ct)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        var started = db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.Action == "EmailChangeStarted" && a.EntityType == "CORE.User" && a.OccurredAt >= since);
        if (await started.CountAsync(a => a.UserId == ctx.UserId, ct).ConfigureAwait(false) >= DailyLimitPerAdministrator)
            throw new AppException(PlatformErrorCodes.EmailChangeLimitAdmin, 429, null, DailyLimitPerAdministrator);
        if (await started.CountAsync(a => a.EntityId == targetId, ct).ConfigureAwait(false) >= DailyLimitPerUser)
            throw new AppException(PlatformErrorCodes.EmailChangeLimitUser, 429, null, DailyLimitPerUser);
    }

    public async Task<EmailChangeResponse> CancelOwnAsync(CancellationToken ct) => await CancelAsync(await SelfAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    public async Task<EmailChangeResponse> CancelForUserAsync(string userToken, CancellationToken ct) =>
        await CancelAsync(await admin.AccountTargetAsync(userToken, ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <summary>
    /// The address a valid link would set, shown on the confirmation page before the button — only to the account's user, signed in
    /// (security review 21 Oct 2026, F5); null otherwise.
    /// </summary>
    public async Task<string?> PendingForAsync(string? userToken, string? token, CancellationToken ct)
    {
        var user = await LinkUserAsync(userToken, token, ct).ConfigureAwait(false);
        return user is not null && ctx.UserId == user.Id ? user.PendingEmail : null;
    }

    /// <summary>
    /// The link from the new address: only for the account's user, signed in (whoever controls a mistyped or administrator-entered address
    /// cannot take the account over); the link carries no address and is tied to this request (a new request or a cancel voids it). The token
    /// is checked first, so the answer reveals nothing about a user without a valid link (F10). On success the security stamp changes: every
    /// earlier link of the account (e.g. a password reset sent to the old address) stops working (F3); the other sessions end and the caller
    /// re-issues this session's cookie with the returned user.
    /// </summary>
    public async Task<User> ConfirmAsync(string? userToken, string? token, CancellationToken ct)
    {
        var user = await LinkUserAsync(userToken, token, ct).ConfigureAwait(false) ?? throw Fail.Rule(PlatformErrorCodes.TokenInvalid);
        if (ctx.UserId != user.Id) throw new AppException(PlatformErrorCodes.VerifySignInRequired, 403);
        var mail = user.PendingEmail!;
        if (await users.FindByEmailAsync(mail).ConfigureAwait(false) is { } other && other.Id != user.Id) throw Fail.Conflict(PlatformErrorCodes.NotAvailable);
        var previous = user.EmailConfirmedAt is null ? null : user.Email;
        var now = clock.GetUtcNow().UtcDateTime;
        using (scope.Use(user.TenantId, null, user.Id))
        {
            changes.SetAction(user, "EmailChanged");
            user.Email = mail;
            user.NormalizedEmail = users.NormalizeEmail(mail);
            user.EmailConfirmedAt = now;
            user.PendingEmail = null;
            user.PendingEmailAt = null;
            user.SecurityStamp = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(20));
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
            {
                // Taken at the same moment (unique index) — any other failure is not hidden behind "not available" (F7).
                db.ChangeTracker.Clear();
                if (await users.FindByEmailAsync(mail).ConfigureAwait(false) is { } taken && taken.Id != user.Id) throw Fail.Conflict(PlatformErrorCodes.NotAvailable);
                throw;
            }
        }
        // Like a password change: the account's other sessions end (this one stays).
        await sessions.RevokeAllAsync(user.Id, "Email changed", ctx.SessionId, ct).ConfigureAwait(false);
        if (previous is not null && !string.Equals(previous, mail, StringComparison.OrdinalIgnoreCase))
            await SendQuietlyAsync(EmailTemplates.EmailChanged(previous, user.FullName, Mask(mail), now, Base() + "/sign-in", user.LanguageCode), user.Id, ct).ConfigureAwait(false);
        return user;
    }

    /// <summary>The user of a link whose token is valid for the user's current pending change; null otherwise.</summary>
    private async Task<User?> LinkUserAsync(string? userToken, string? token, CancellationToken ct)
    {
        var publicId = urlTokens.Decode<User>(userToken);
        if (publicId is null || string.IsNullOrEmpty(token)) return null;
        var user = await db.Set<User>().IgnoreQueryFilters(TenantFilter).SingleOrDefaultAsync(u => u.PublicId == publicId && !u.IsDeleted, ct).ConfigureAwait(false);
        if (user?.PendingEmail is null || user.PendingEmailAt is null) return null;
        return await users.VerifyUserTokenAsync(user, users.Options.Tokens.EmailConfirmationTokenProvider, Purpose(user), token).ConfigureAwait(false) ? user : null;
    }

    private async Task StartAsync(User user, string mail, bool byAdministrator, CancellationToken ct)
    {
        if (string.Equals(user.Email, mail, StringComparison.OrdinalIgnoreCase)) throw Fail.Field("newEmail", PlatformErrorCodes.EmailUnchanged);
        // Owner decision 18 Oct 2026 (DT-203): one generic answer for a taken address, never which account has it.
        if (await users.FindByEmailAsync(mail).ConfigureAwait(false) is { } other && other.Id != user.Id) throw Fail.Conflict(PlatformErrorCodes.NotAvailable);
        changes.SetAction(user, byAdministrator ? "EmailChangeStarted" : "EmailChangeRequested");
        user.PendingEmail = mail;
        user.PendingEmailAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var token = await users.GenerateUserTokenAsync(user, users.Options.Tokens.EmailConfirmationTokenProvider, Purpose(user)).ConfigureAwait(false);
        var link = $"{Base()}/confirm-email-change?u={Uri.EscapeDataString(urlTokens.Encode<User>(user.PublicId))}&t={Uri.EscapeDataString(token)}";
        await email.SendAsync(EmailTemplates.ConfirmEmailChange(mail, user.FullName, byAdministrator, link, user.LanguageCode), ct).ConfigureAwait(false);
        // F8: the current (verified) address hears of the request at once, not only after the change.
        if (user.Email is { } current && user.EmailConfirmedAt is not null)
            await SendQuietlyAsync(EmailTemplates.EmailChangeAsked(current, user.FullName, Mask(mail), byAdministrator, Base() + "/sign-in", user.LanguageCode), user.Id, ct).ConfigureAwait(false);
    }

    private async Task<EmailChangeResponse> CancelAsync(User user, CancellationToken ct)
    {
        if (user.PendingEmail is not null)
        {
            changes.SetAction(user, "EmailChangeCancelled");
            user.PendingEmail = null;
            user.PendingEmailAt = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return new EmailChangeResponse(user.Email, null);
    }

    /// <summary>Tied to the address and the moment of the request: an earlier link stops working after a new request.</summary>
    private static string Purpose(User user) =>
        "ChangeEmail:" + user.PendingEmail!.ToUpperInvariant() + ":" + user.PendingEmailAt!.Value.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Address(string? value)
    {
        var mail = value?.Trim();
        new Validator().Require("newEmail", mail).Check(string.IsNullOrEmpty(mail) || AccountService.IsEmail(mail), "newEmail").MaxLength("newEmail", mail, 150).ThrowIfInvalid();
        return mail!;
    }

    private async Task<User> SelfAsync(CancellationToken ct) =>
        await db.Set<User>().IgnoreQueryFilters(TenantFilter).SingleOrDefaultAsync(u => u.Id == ctx.UserId && !u.IsDeleted, ct).ConfigureAwait(false)
        ?? throw Fail.Forbidden();

    private string Base() => options.Value.PublicBaseUrl.TrimEnd('/');

    /// <summary>"a•••@example.com" — the notice to the old address does not hand the new one to whoever reads the old mailbox.</summary>
    internal static string Mask(string mail)
    {
        var at = mail.LastIndexOf('@');
        return at <= 0 ? "•••" : mail[0] + "•••" + mail[at..];
    }

    private async Task SendQuietlyAsync(EmailMessage message, long userId, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(message, ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // the change is saved and audited; a mail failure must not undo it or fail the confirmation
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            NoticeFailed(log, ex, userId);
        }
    }

    [LoggerMessage(EventId = 9202, Level = LogLevel.Warning, Message = "The email-change notice to user {UserId} could not be sent.")]
    private static partial void NoticeFailed(ILogger logger, Exception ex, long userId);
}
