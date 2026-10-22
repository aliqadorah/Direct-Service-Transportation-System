using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Platform staff in the provider console (owner feedback 13 Oct 2026, item 8b): the list (name, e-mail, state, last sign-in); invitations by
/// e-mail with a single-use link valid for 48 hours — the invitee sets a password and then the mandatory two-step set-up before reaching
/// the console; resend (the old link stops working) and cancel of pending invitations; deactivate (with a reason, sessions ended) and
/// reactivate — never one's own account nor the last active staff member. There is no public sign-up for staff. Every action is audited.
/// A pending invitation is a staff account without a password; cancelling it deletes it (soft), so the address can be invited again.
/// </summary>
public sealed class StaffService(IAppDbContext db, IRequestContext ctx, UserManager<User> users, MasterDataSupport support, IChangeContext changes, IDataScope scope,
    SessionService sessions, IEmailSender email, IUrlTokenService urlTokens, IOptions<AppOptions> options, IPlatformLock locks, TimeProvider clock)
{
    /// <summary>Identity token provider of staff invitations (registered with a 48-hour lifespan).</summary>
    public const string InvitationTokenProvider = "StaffInvitation";
    public const string InvitationPurpose = "StaffInvitation";
    public const int InvitationHours = 48;

    private long StaffId => ctx.IsPlatformStaff && ctx.UserId is long id ? id : throw Fail.NotFound();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Staff accounts (people: the platform's system account is excluded).</summary>
    private IQueryable<User> Staff => db.Set<User>().IgnoreQueryFilters()
        .Where(u => u.IsPlatformStaff && !u.IsDeleted && u.Email != TenantLifecycleService.SystemEmail);

    private static bool Pending(User u) => string.IsNullOrEmpty(u.PasswordHash);

    /// <summary>Active and set up (password and authenticator): the accounts that can sign in to the console.</summary>
    private static bool CanSignIn(User u) => u.Status == "Active" && !Pending(u) && u.TwoFactorEnabledAt is not null;

    private static DateTime InvitedAt(User u) => u.UpdatedAt ?? u.CreatedAt;

    private StaffResponse ToResponse(User u, long self)
    {
        var pending = Pending(u);
        var expires = pending ? InvitedAt(u).AddHours(InvitationHours) : (DateTime?)null;
        var state = pending ? (expires > Now ? StaffStates.Invited : StaffStates.Expired) : u.Status == "Active" ? StaffStates.Active : StaffStates.Disabled;
        return new StaffResponse(support.Token<User>(u.PublicId), u.FullName, u.Email ?? string.Empty, state, Utc(u.LastSignInAt), pending ? Utc(InvitedAt(u)) : null, Utc(expires),
            u.TwoFactorEnabledAt is not null, u.Id == self);
    }

    private static DateTime? Utc(DateTime? d) => d is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    public async Task<IReadOnlyList<StaffResponse>> ListAsync(CancellationToken ct)
    {
        var self = StaffId;
        var list = await Staff.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(u => ToResponse(u, self)).ToList();
    }

    /// <summary>Invites a staff member: the account (no password, no tenant) and an e-mail with the 48-hour link.</summary>
    public async Task<StaffResponse> InviteAsync(StaffInviteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var self = StaffId;
        var name = TextNormalizer.CleanText(r.FullName);
        var mail = r.Email?.Trim();
        new Validator().Require("fullName", name).MaxLength("fullName", name, 150).Require("email", mail).MaxLength("email", mail, 150)
            .Check(mail is null || AccountService.IsEmail(mail), "email", ErrorCodes.ValidationFormat).ThrowIfInvalid();
        if (await users.FindByEmailAsync(mail!).ConfigureAwait(false) is not null) throw Fail.Field("email", ErrorCodes.EmailInUse);
        var user = new User { FullName = name!, Email = mail!, LanguageCode = ctx.Language == "en" ? "en" : "ar", IsPlatformStaff = true, TenantId = null };
        using (scope.Use(null, null, self))
        {
            changes.SetAction(user, "InviteStaff");
            var created = await users.CreateAsync(user).ConfigureAwait(false);   // no password: set from the invitation link
            if (!created.Succeeded) throw Fail.Field("email", ErrorCodes.EmailInUse);
        }
        await SendInvitationAsync(user, ct).ConfigureAwait(false);
        return ToResponse(user, self);
    }

    /// <summary>A new link for a pending invitation; the previous link stops working (security stamp changed).</summary>
    public async Task<StaffResponse> ResendAsync(string token, CancellationToken ct)
    {
        var self = StaffId;
        var user = await FindAsync(token, ct).ConfigureAwait(false);
        if (!Pending(user)) throw await RefusedAsync(user, ErrorCodes.StaffNotPending, ct).ConfigureAwait(false);
        using (scope.Use(null, null, self))
        {
            changes.SetAction(user, "ResendStaffInvitation");
            user.UpdatedAt = Now;   // the new link's 48 hours count from now
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        }
        await SendInvitationAsync(user, ct).ConfigureAwait(false);
        return ToResponse(user, self);
    }

    /// <summary>Cancels a pending invitation: the account is deleted (soft) and its link no longer works.</summary>
    public async Task CancelAsync(string token, CancellationToken ct)
    {
        var self = StaffId;
        var user = await FindAsync(token, ct).ConfigureAwait(false);
        if (!Pending(user)) throw await RefusedAsync(user, ErrorCodes.StaffNotPending, ct).ConfigureAwait(false);
        using (scope.Use(null, null, self))
        {
            changes.SetAction(user, "CancelStaffInvitation");
            user.IsDeleted = true;
            user.DeletedAt = Now;
            user.DeletedBy = self;
            user.DeleteReason = "Staff invitation cancelled";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Deactivates a staff member (reason required): sessions end at once; never oneself nor the last staff member able to sign in.</summary>
    public async Task<StaffResponse> DeactivateAsync(string token, StaffStatusRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var self = StaffId;
        var why = MasterIds.Reason(r.Reason);
        var user = await FindAsync(token, ct).ConfigureAwait(false);
        // One status change at a time, so two staff members cannot deactivate each other at once and leave nobody (security review 13 Oct 2026).
        await using var held = await locks.TryAcquireAsync("platform-staff-status", ct).ConfigureAwait(false) ?? throw Fail.Conflict(ErrorCodes.SetupBusy);
        if (user.Id == self) throw await RefusedAsync(user, ErrorCodes.StaffSelf, ct).ConfigureAwait(false);
        if (user.Status != "Active") throw await RefusedAsync(user, ErrorCodes.StaffNotActive, ct).ConfigureAwait(false);
        if (CanSignIn(user))
        {
            var others = await Staff.Where(u => u.Id != user.Id && u.Status == "Active" && u.PasswordHash != "" && u.TwoFactorEnabledAt != null)
                .CountAsync(ct).ConfigureAwait(false);
            if (others == 0) throw await RefusedAsync(user, ErrorCodes.StaffLastActive, ct).ConfigureAwait(false);
        }
        using (scope.Use(null, null, self))
        {
            changes.SetAction(user, "DeactivateStaff", why);
            user.Status = "Disabled";
            await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        }
        await sessions.RevokeAllAsync(user.Id, "Staff account deactivated", null, ct).ConfigureAwait(false);
        return ToResponse(user, self);
    }

    public async Task<StaffResponse> ReactivateAsync(string token, CancellationToken ct)
    {
        var self = StaffId;
        var user = await FindAsync(token, ct).ConfigureAwait(false);
        if (user.Status == "Active") return ToResponse(user, self);
        using (scope.Use(null, null, self))
        {
            changes.SetAction(user, "ReactivateStaff");
            user.Status = "Active";
            user.FailedSignInCount = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return ToResponse(user, self);
    }

    // ------------------------------------------------------------ the invitation link (anonymous, /accept-staff-invitation)

    /// <summary>The pending staff account a link names when its token is valid (unused, not expired, not cancelled), else null.</summary>
    public async Task<User?> InvitationAsync(string? userToken, string? token, CancellationToken ct)
    {
        var pid = urlTokens.Decode<User>(userToken);
        if (pid is null || string.IsNullOrEmpty(token)) return null;
        var user = await Staff.SingleOrDefaultAsync(u => u.PublicId == pid, ct).ConfigureAwait(false);
        if (user is null || !Pending(user) || user.Status != "Active") return null;
        return await users.VerifyUserTokenAsync(user, InvitationTokenProvider, InvitationPurpose, token).ConfigureAwait(false) ? user : null;
    }

    /// <summary>
    /// Accepts the invitation: sets the password (platform policy) and confirms the e-mail; the link then stops working (the password
    /// changes the security stamp). The caller continues into the mandatory two-step set-up.
    /// </summary>
    public async Task<User> AcceptInvitationAsync(string? userToken, string? token, string? password, CancellationToken ct)
    {
        var user = await InvitationAsync(userToken, token, ct).ConfigureAwait(false);
        if (user is null)
        {
            await AuditAsync("StaffInvitationRefused", null, ErrorCodes.TokenInvalid, ct).ConfigureAwait(false);
            throw Fail.Rule(ErrorCodes.TokenInvalid);
        }
        var policy = PasswordPolicy.Default;
        if (!policy.IsSatisfiedBy(password) || password!.Length > 64) throw AccountService.PolicyError(policy);
        using (scope.Use(null, null, user.Id))
        {
            changes.SetAction(user, "AcceptStaffInvitation");
            user.EmailConfirmedAt = Now;
            var result = await users.AddPasswordAsync(user, password).ConfigureAwait(false);
            if (!result.Succeeded)
                throw result.Errors.Any(e => e.Code == TenantPasswordValidator.ErrorCode) ? AccountService.PolicyError(policy) : Fail.Rule(ErrorCodes.TokenInvalid);
        }
        return user;
    }

    /// <summary>A refused staff action, audited with its reason and the caller's address (security review 13 Oct 2026).</summary>
    private async Task<AppException> RefusedAsync(User target, string code, CancellationToken ct)
    {
        await AuditAsync("StaffActionRefused", target.Id, code, ct).ConfigureAwait(false);
        return Fail.Rule(code);
    }

    private async Task AuditAsync(string action, long? userId, string reason, CancellationToken ct)
    {
        db.Set<AuditLog>().Add(new AuditLog
        {
            OccurredAt = Now, Action = action, EntityType = "CORE.User", EntityId = userId, UserId = ctx.UserId, IpAddress = ctx.IpAddress,
            NewValues = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["_reason"] = reason }),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task SendInvitationAsync(User user, CancellationToken ct)
    {
        var inviter = await db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == ctx.UserId).Select(u => u.FullName).SingleAsync(ct).ConfigureAwait(false);
        var token = await users.GenerateUserTokenAsync(user, InvitationTokenProvider, InvitationPurpose).ConfigureAwait(false);
        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/accept-staff-invitation?u={Uri.EscapeDataString(urlTokens.Encode<User>(user.PublicId))}&t={Uri.EscapeDataString(token)}";
        await email.SendAsync(EmailTemplates.StaffInvitation(user.Email!, user.FullName, inviter, InvitationHours, link, ctx.Language), ct).ConfigureAwait(false);
    }

    private async Task<User> FindAsync(string token, CancellationToken ct)
    {
        var pid = support.Decode<User>(token) ?? throw Fail.NotFound();
        return await Staff.SingleOrDefaultAsync(u => u.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }
}
