using System.Security.Cryptography;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Identity;

/// <summary>
/// Invitations (FR-172, US-32): invite by email with a role in the current company; the link is
/// single-use and expires after 7 days (ASM-08); resend or cancel. Accepting creates the account
/// (email verified by the link) or, for an existing user of the same tenant, adds the company role.
/// One email = one account in one tenant (A-18): an email used in another tenant is refused.
/// </summary>
public sealed class InvitationService(
    IAppDbContext db, IRequestContext ctx, IDataScope scope, IChangeContext changes, UserManager<User> users,
    AccessService access, IUrlTokenService tokens, IEmailSender email, TimeProvider clock, IOptions<AppOptions> options, GrantGuard guard,
    Platform.PlanLimits? limits = null)
{
    private static readonly string[] TenantFilter = ["Tenant"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<InvitationResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<Invitation>().AsNoTracking()
            .Where(i => i.CompanyId == ctx.CompanyId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new { i.PublicId, i.Email, RolePublicId = i.Role.PublicId, RoleName = i.Role.Name, i.ExpiresAt, i.AcceptedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(i => new InvitationResponse(tokens.Encode<Invitation>(i.PublicId), i.Email,
            new Ref(tokens.Encode<Role>(i.RolePublicId), i.RoleName), i.ExpiresAt, i.AcceptedAt,
            i.AcceptedAt is not null ? "Accepted" : i.ExpiresAt <= Now ? "Expired" : "Pending")).ToList();
    }

    public async Task<InvitationResponse> InviteAsync(InviteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var address = r.Email?.Trim();
        new Validator().Require("email", address).Check(AccountService.IsEmail(address), "email").MaxLength("email", address, 150).ThrowIfInvalid();
        var rolePublicId = tokens.Decode<Role>(r.Role) ?? throw Fail.Field("role", ErrorCodes.ValidationFormat);
        var role = await db.Set<Role>().SingleOrDefaultAsync(x => x.PublicId == rolePublicId, ct).ConfigureAwait(false)
            ?? throw Fail.Field("role", ErrorCodes.ValidationFormat);
        guard.EnsureCanGrant(await guard.RoleKeysAsync(role.Id, ct).ConfigureAwait(false)); // SR-01

        var existing = await users.FindByEmailAsync(address!).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.TenantId != ctx.TenantId) throw Fail.Conflict(ErrorCodes.EmailInUse);
            var hasRole = await db.Set<UserCompanyRole>().AnyAsync(x => x.UserId == existing.Id && x.CompanyId == ctx.CompanyId, ct).ConfigureAwait(false);
            if (hasRole) throw Fail.Conflict(ErrorCodes.DuplicateKey, address!);
        }

        // A new invitation replaces any open one for the same email in this company.
        var open = await db.Set<Invitation>().Where(i => i.CompanyId == ctx.CompanyId && i.Email == address && i.AcceptedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var o in open)
        {
            o.DeleteReason = "Replaced by a new invitation";
            db.Set<Invitation>().Remove(o);
        }

        var token = NewToken();
        var inv = new Invitation
        {
            CompanyId = ctx.CompanyId!.Value, Email = address!, RoleId = role.Id, TokenHash = SessionService.HashToken(token),
            ExpiresAt = Now.AddDays(options.Value.InvitationDays), InvitedBy = ctx.UserId!.Value,
        };
        db.Set<Invitation>().Add(inv);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendAsync(inv, token, ct).ConfigureAwait(false);
        return (await ListAsync(ct).ConfigureAwait(false)).First(x => x.Id == tokens.Encode<Invitation>(inv.PublicId));
    }

    public async Task ResendAsync(string invitationToken, CancellationToken ct)
    {
        var inv = await FindAsync(invitationToken, ct).ConfigureAwait(false);
        if (inv.AcceptedAt is not null) throw Fail.Rule(ErrorCodes.TokenInvalid);
        var token = NewToken();
        inv.TokenHash = SessionService.HashToken(token);
        inv.ExpiresAt = Now.AddDays(options.Value.InvitationDays);
        changes.SetAction(inv, "Resend");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendAsync(inv, token, ct).ConfigureAwait(false);
    }

    public async Task CancelAsync(string invitationToken, CancellationToken ct)
    {
        var inv = await FindAsync(invitationToken, ct).ConfigureAwait(false);
        if (inv.AcceptedAt is not null) throw Fail.Rule(ErrorCodes.TokenInvalid);
        inv.DeleteReason = "Cancelled";
        db.Set<Invitation>().Remove(inv);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Anonymous: what the invitation link is for.</summary>
    public async Task<InvitationInfoResponse> InfoAsync(string token, CancellationToken ct)
    {
        var inv = await ByLinkTokenAsync(token, ct).ConfigureAwait(false);
        if (inv is null) return new InvitationInfoResponse(string.Empty, string.Empty, false, false);
        var company = await db.Set<Company>().IgnoreQueryFilters(TenantFilter).Where(c => c.Id == inv.CompanyId).Select(c => c.Name)
            .SingleAsync(ct).ConfigureAwait(false);
        var hasAccount = await users.FindByEmailAsync(inv.Email).ConfigureAwait(false) is not null;
        return new InvitationInfoResponse(company, inv.Email, IsUsable(inv), hasAccount);
    }

    /// <summary>Anonymous acceptance by a new user: creates the account (email verified by the link) and the role.</summary>
    public async Task AcceptNewAsync(InvitationAcceptRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var inv = await ByLinkTokenAsync(r.Token, ct).ConfigureAwait(false);
        if (inv is null || !IsUsable(inv)) throw Fail.Rule(ErrorCodes.TokenInvalid);
        if (await users.FindByEmailAsync(inv.Email).ConfigureAwait(false) is not null) throw Fail.Conflict(ErrorCodes.EmailInUse);
        var fullName = TextNormalizer.CleanText(r.FullName);
        new Validator().Require("fullName", fullName).MaxLength("fullName", fullName, 150).Require("password", r.Password).ThrowIfInvalid();
        var policy = await TenantPasswordValidator.GetPolicyAsync(db, inv.TenantId, ct).ConfigureAwait(false);
        if (!policy.IsSatisfiedBy(r.Password)) throw AccountService.PolicyError(policy);

        await db.InTransactionAsync(async c =>
        {
            using var _ = scope.Use(inv.TenantId, null, null);
            // The invitation link proves the email address.
            // One more active user must fit the plan (P-07).
            if (limits is not null) await limits.EnsureUserAsync(inv.TenantId, ct).ConfigureAwait(false);
            var user = new User { FullName = fullName!, TenantId = inv.TenantId, EmailConfirmedAt = Now, Email = inv.Email };
            var created = await users.CreateAsync(user, r.Password!).ConfigureAwait(false);
            if (!created.Succeeded) throw AccountService.PolicyError(policy);
            using var __ = scope.Use(inv.TenantId, null, user.Id);
            await CompleteAsync(inv, user, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Signed-in acceptance by an existing user of the same tenant.</summary>
    public async Task AcceptExistingAsync(string token, CancellationToken ct)
    {
        var inv = await ByLinkTokenAsync(token, ct).ConfigureAwait(false);
        if (inv is null || !IsUsable(inv)) throw Fail.Rule(ErrorCodes.TokenInvalid);
        var user = await db.Set<User>().IgnoreQueryFilters(TenantFilter).SingleAsync(u => u.Id == ctx.UserId, ct).ConfigureAwait(false);
        if (!string.Equals(user.NormalizedEmail, inv.Email.ToUpperInvariant(), StringComparison.Ordinal) || user.TenantId != inv.TenantId)
            throw Fail.Rule(ErrorCodes.InvitationEmailMismatch);
        await db.InTransactionAsync(c => CompleteAsync(inv, user, c), ct).ConfigureAwait(false);
    }

    private async Task CompleteAsync(Invitation inv, User user, CancellationToken ct)
    {
        var already = await db.Set<UserCompanyRole>().IgnoreQueryFilters(TenantFilter)
            .AnyAsync(x => x.UserId == user.Id && x.CompanyId == inv.CompanyId && !x.IsDeleted, ct).ConfigureAwait(false);
        if (!already)
            db.Set<UserCompanyRole>().Add(new UserCompanyRole { TenantId = inv.TenantId, UserId = user.Id, CompanyId = inv.CompanyId, RoleId = inv.RoleId });
        user.DefaultCompanyId ??= inv.CompanyId;
        inv.AcceptedAt = Now;
        changes.SetAction(inv, "Accept");
        // FR-190: the inviter is notified that the invitation was accepted.
        db.Set<Notification>().Add(new Notification
        {
            TenantId = inv.TenantId, UserId = inv.InvitedBy, CompanyId = inv.CompanyId, NotificationType = "CORE.InvitationAccepted",
            Message = $"{user.FullName} قبل الدعوة / accepted the invitation",
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        access.Invalidate(user.Id);
    }

    private async Task SendAsync(Invitation inv, string token, CancellationToken ct)
    {
        var company = await db.Set<Company>().IgnoreQueryFilters(TenantFilter).Where(c => c.Id == inv.CompanyId).Select(c => c.Name).SingleAsync(ct).ConfigureAwait(false);
        var inviter = ctx.UserName ?? string.Empty;
        var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/accept-invitation?t={Uri.EscapeDataString(token)}";
        await email.SendAsync(EmailTemplates.Invitation(inv.Email, company, inviter, options.Value.InvitationDays, link), ct).ConfigureAwait(false);
    }

    private bool IsUsable(Invitation inv) => inv.AcceptedAt is null && inv.ExpiresAt > Now;

    private async Task<Invitation?> ByLinkTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = SessionService.HashToken(token);
        return await db.Set<Invitation>().IgnoreQueryFilters(TenantFilter).SingleOrDefaultAsync(i => i.TokenHash == hash && !i.IsDeleted, ct).ConfigureAwait(false);
    }

    private async Task<Invitation> FindAsync(string invitationToken, CancellationToken ct)
    {
        var publicId = tokens.Decode<Invitation>(invitationToken) ?? throw Fail.NotFound();
        return await db.Set<Invitation>().SingleOrDefaultAsync(i => i.PublicId == publicId && i.CompanyId == ctx.CompanyId, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
