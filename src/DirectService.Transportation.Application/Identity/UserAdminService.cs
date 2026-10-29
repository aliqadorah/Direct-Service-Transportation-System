using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>
/// User administration in the current company (core.users.manage; FR-174, US-32): assign/remove the
/// company role, disable/enable, unlock, send password reset, archive, reset 2FA, sign out. Enforces
/// BR-001 (a tenant always keeps an active Owner), BR-002 (each company keeps an active user with
/// users.manage) and BR-003 (users are never deleted). Every action is audited with its reason.
/// </summary>
public sealed class UserAdminService(
    IAppDbContext db, IRequestContext ctx, IChangeContext changes, AccessService access, SessionService sessions,
    AccountService accounts, UserManager<User> users, IUrlTokenService tokens, GrantGuard guard, SignInThrottle throttle, Platform.PlanLimits? limits = null)
{
    private static readonly string[] TenantFilter = ["Tenant"];

    /// <summary>The tenant's password rules, for the "create user" and "set password" forms (DT-208).</summary>
    public async Task<PasswordPolicyResponse> PasswordPolicyAsync(CancellationToken ct)
    {
        var p = await TenantPasswordValidator.GetPolicyAsync(db, ctx.TenantId ?? throw Fail.Forbidden(), ct).ConfigureAwait(false);
        return new PasswordPolicyResponse(p.MinLength, p.RequireLetter, p.RequireDigit);
    }

    public async Task<PagedResult<UserListItem>> ListAsync(string? search, PageRequest page, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        var companyId = ctx.CompanyId!.Value;
        var q = db.Set<UserCompanyRole>().AsNoTracking().Where(r => r.CompanyId == companyId && !r.User.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.User.FullName.Contains(s) || r.User.Email!.Contains(s) || r.User.UserName!.Contains(s));
        }
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var rows = await q.OrderBy(r => r.User.FullName).Skip(page.Skip).Take(page.SafePageSize)
            .Select(r => new
            {
                r.User.PublicId, r.User.FullName, r.User.Email, r.User.Status, r.User.LockedUntil, r.User.TwoFactorEnabledAt,
                r.User.EmailConfirmedAt, r.User.LastSignInAt, r.User.RowVersion, RolePublicId = r.Role.PublicId, RoleName = r.Role.Name,
                r.User.UserName, r.User.MustChangePassword, r.User.PendingEmail,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var items = rows.Select(u => new UserListItem(tokens.Encode<User>(u.PublicId), u.FullName, u.Email, u.Status,
            new Ref(tokens.Encode<Role>(u.RolePublicId), u.RoleName), u.LockedUntil > now, u.TwoFactorEnabledAt is not null,
            u.EmailConfirmedAt is not null, u.LastSignInAt, Convert.ToBase64String(u.RowVersion), u.UserName, u.MustChangePassword, u.PendingEmail)).ToList();
        return new PagedResult<UserListItem>(items, page.SafePage, page.SafePageSize, total);
    }

    /// <summary>
    /// "Create with username and password" (revision 6, owner change request 15 Oct 2026): the administrator sets the username (unique
    /// platform-wide), an optional email and a password under the tenant policy; no invitation, no email verification, no 2FA required.
    /// The role follows DT-18 (only keys the caller holds) and the plan's user limit applies. Audited as "CreatedWithPassword".
    /// </summary>
    public async Task<UserListItem> CreateWithPasswordAsync(CreateUserRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var fullName = TextNormalizer.CleanText(r.FullName);
        var userName = r.UserName?.Trim();
        var mail = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim();
        new Validator()
            .Require("fullName", fullName).MaxLength("fullName", fullName, 150)
            .Require("userName", userName)
            .Check(mail is null || AccountService.IsEmail(mail), "email").MaxLength("email", mail, 150)
            .Require("password", r.Password)
            .Require("role", r.Role)
            .ThrowIfInvalid();
        if (!IsValidUserName(userName)) throw Fail.Field("userName", ErrorCodes.UserNameFormat);
        var role = await RoleByTokenAsync(r.Role, ct).ConfigureAwait(false);
        guard.EnsureCanGrant(role.RolePermissions.Select(p => p.Permission.PermissionKey)); // no escalation (SR-01)
        var tenantId = ctx.TenantId ?? throw Fail.Forbidden();
        var policy = await TenantPasswordValidator.GetPolicyAsync(db, tenantId, ct).ConfigureAwait(false);
        if (!policy.IsSatisfiedBy(r.Password)) throw AccountService.PolicyError(policy);
        // P-07 first: a tenant at its user limit must not be able to probe which usernames/emails exist without creating anyone (DT-202).
        if (limits is not null) await limits.EnsureUserAsync(tenantId, ct).ConfigureAwait(false);
        // Owner decision 18 Oct 2026 (DT-203): one generic "not available" answer for a taken username or email (any tenant), never which.
        if (await users.FindByNameAsync(userName!).ConfigureAwait(false) is not null
            || (mail is not null && await users.FindByEmailAsync(mail).ConfigureAwait(false) is not null))
            throw Fail.Conflict(ErrorCodes.NotAvailable);

        var user = await db.InTransactionAsync(async c =>
        {
            // UserManager fills NormalizedUserName / NormalizedEmail on create (Set* would persist an unsaved user).
            var u = new User
            {
                FullName = fullName!, UserName = userName, Email = mail, TenantId = tenantId, LanguageCode = ctx.Language is "en" ? "en" : "ar",
                DefaultCompanyId = ctx.CompanyId, MustChangePassword = r.MustChangePassword,
            };
            changes.SetAction(u, "CreatedWithPassword");
            var created = await users.CreateAsync(u, r.Password).ConfigureAwait(false);
            if (!created.Succeeded)
            {
                if (created.Errors.Any(e => e.Code == TenantPasswordValidator.ErrorCode)) throw AccountService.PolicyError(policy);
                if (created.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail")) throw Fail.Conflict(ErrorCodes.NotAvailable);
                throw Fail.Rule(ErrorCodes.ValidationFailed);
            }
            db.Set<UserCompanyRole>().Add(new UserCompanyRole { UserId = u.Id, CompanyId = ctx.CompanyId!.Value, RoleId = role.Id });
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return u;
        }, ct).ConfigureAwait(false);
        // Owner decision 17 Oct 2026: an email entered by the administrator is verified by its owner; until then no reset email goes to it.
        if (user.Email is not null) await accounts.SendVerificationAsync(user, ct).ConfigureAwait(false);
        return new UserListItem(tokens.Encode<User>(user.PublicId), user.FullName, user.Email, user.Status, new Ref(tokens.Encode<Role>(role.PublicId), role.Name),
            false, false, false, null, Convert.ToBase64String(user.RowVersion ?? []), user.UserName, user.MustChangePassword);
    }

    /// <summary>
    /// The administrator's password reset for a user created with a username — the only reset for a user without email (revision 6).
    /// Unlocks the account, ends every session; audited as "PasswordSetByAdmin". Users who sign in with their email keep the emailed link.
    /// </summary>
    public async Task SetPasswordAsync(string userToken, SetPasswordRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        if (user.UserName is null) throw Fail.Rule(ErrorCodes.PasswordSetNotAllowed);
        var policy = await TenantPasswordValidator.GetPolicyAsync(db, user.TenantId, ct).ConfigureAwait(false);
        if (!policy.IsSatisfiedBy(r.Password)) throw AccountService.PolicyError(policy);
        changes.SetAction(user, "PasswordSetByAdmin");
        user.MustChangePassword = r.MustChangePassword;
        user.LockedUntil = null;
        user.FailedSignInCount = 0;
        throttle.ResetAll(user.Id);   // DT-204: every address's block too
        var token = await users.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);
        var result = await users.ResetPasswordAsync(user, token, r.Password).ConfigureAwait(false);   // new security stamp
        if (!result.Succeeded) throw AccountService.PolicyError(policy);
        await AfterAccessChangeAsync(user.Id, "Password set by an administrator", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// DT-229: name and username are changed only by the Owner or an Administrator, in Users &amp; roles — for any user they may manage
    /// (GrantGuard: an Administrator never manages the Owner), themselves included. Username: same format and availability rules as at
    /// creation, one generic "not available" answer. A username is changed, never added to a user who signs in with their email nor cleared
    /// (security review 21 Oct 2026, F1/F2: "has a username" decides who may get an administrator's password and whose email needs its
    /// user signed in to be verified). Audited
    /// ("ProfileChangedByAdmin", old → new in the readable history).
    /// </summary>
    public async Task UpdateUserAsync(string userToken, UpdateUserRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var user = await AccountTargetAsync(userToken, ct).ConfigureAwait(false);
        var fullName = TextNormalizer.CleanText(r.FullName);
        var userName = string.IsNullOrWhiteSpace(r.UserName) ? null : r.UserName.Trim();
        new Validator()
            .Require("fullName", fullName).MaxLength("fullName", fullName, 150)
            .Check(userName is not null || user.UserName is null, "userName", ErrorCodes.ValidationRequired)
            .Check(userName is null || user.UserName is not null, "userName", ErrorCodes.UserNameNotForEmailUser)
            .Require("rowVersion", r.RowVersion)
            .ThrowIfInvalid();
        if (userName is not null && !IsValidUserName(userName)) throw Fail.Field("userName", ErrorCodes.UserNameFormat);
        db.ExpectRowVersion(user, r.RowVersion!);
        var normalized = userName is null ? null : users.NormalizeName(userName);
        if (normalized is not null && normalized != user.NormalizedUserName
            && (await users.FindByNameAsync(userName!).ConfigureAwait(false) is { } other && other.Id != user.Id))
            throw Fail.Conflict(ErrorCodes.NotAvailable);
        if (user.FullName == fullName && user.UserName == userName) return;
        changes.SetAction(user, "ProfileChangedByAdmin");
        user.FullName = fullName!;
        user.UserName = userName;
        user.NormalizedUserName = normalized;
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException e) when (normalized is not null && e is not DbUpdateConcurrencyException)
        {
            throw Fail.Conflict(ErrorCodes.NotAvailable);   // taken at the same moment (unique index)
        }
        access.Invalidate(user.Id);
    }

    /// <summary>DT-229: the caller holds the preset Owner or Administrator role in the current company; otherwise 403.</summary>
    public async Task EnsureOwnerOrAdministratorAsync(CancellationToken ct)
    {
        var ok = await db.Set<UserCompanyRole>().AsNoTracking().AnyAsync(r => r.UserId == ctx.UserId && r.CompanyId == ctx.CompanyId && r.Role.IsPreset
            && (r.Role.Name == PresetRoles.Owner || r.Role.Name == PresetRoles.Administrator), ct).ConfigureAwait(false);
        if (!ok) throw new AppException(ErrorCodes.OwnerOrAdministratorOnly, 403);
    }

    /// <summary>DT-229: a user whose name, username or email the caller (Owner / Administrator) may change.</summary>
    public async Task<User> AccountTargetAsync(string userToken, CancellationToken ct)
    {
        await EnsureOwnerOrAdministratorAsync(ct).ConfigureAwait(false);
        return (await TargetAsync(userToken, ct).ConfigureAwait(false)).User;
    }

    /// <summary>3–50 characters: ASCII letters, digits, dot, underscore, hyphen (never "@", so a username is never taken for an email).</summary>
    public static bool IsValidUserName(string? value) =>
        value is { Length: >= 3 and <= 50 } && value.All(ch => ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-');

    public async Task AssignRoleAsync(string userToken, string roleToken, CancellationToken ct)
    {
        var (user, link) = await TargetAsync(userToken, accountWide: false, ct).ConfigureAwait(false);
        var role = await RoleByTokenAsync(roleToken, ct).ConfigureAwait(false);
        if (link.RoleId == role.Id) return;
        guard.EnsureCanGrant(role.RolePermissions.Select(p => p.Permission.PermissionKey)); // no escalation (SR-01)
        await EnsureOwnerKeptAsync(user.Id, losingOwnerIn: link.CompanyId, ct, newRole: role).ConfigureAwait(false);
        if (!role.RolePermissions.Any(p => p.Permission.PermissionKey == PermissionKeys.UsersManage))
            await EnsureUserManagerKeptAsync(link.CompanyId, user.Id, ct).ConfigureAwait(false);
        link.RoleId = role.Id;
        changes.SetAction(link, "RoleChange");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await AfterAccessChangeAsync(user.Id, "Role changed", ct).ConfigureAwait(false);
    }

    public async Task RemoveFromCompanyAsync(string userToken, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var (user, link) = await TargetAsync(userToken, accountWide: false, ct).ConfigureAwait(false);
        await EnsureOwnerKeptAsync(user.Id, losingOwnerIn: link.CompanyId, ct).ConfigureAwait(false);
        await EnsureUserManagerKeptAsync(link.CompanyId, user.Id, ct).ConfigureAwait(false);
        link.DeleteReason = reason;
        db.Set<UserCompanyRole>().Remove(link);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await AfterAccessChangeAsync(user.Id, "Removed from company", ct).ConfigureAwait(false);
    }

    public Task DisableAsync(string userToken, string reason, CancellationToken ct) => SetStatusAsync(userToken, "Disabled", "Disable", reason, ct);

    public Task EnableAsync(string userToken, CancellationToken ct) => SetStatusAsync(userToken, "Active", "Enable", null, ct);

    /// <summary>BR-003: users are archived, never deleted.</summary>
    public Task ArchiveAsync(string userToken, string reason, CancellationToken ct) => SetStatusAsync(userToken, "Archived", "Archive", reason, ct);

    public async Task UnlockAsync(string userToken, CancellationToken ct)
    {
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        user.LockedUntil = null;
        user.FailedSignInCount = 0;
        throttle.ResetAll(user.Id);   // DT-204: every address's block too
        changes.SetAction(user, "Unlock");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SendPasswordResetAsync(string userToken, CancellationToken ct)
    {
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        if (user.Email is null) throw Fail.Rule(ErrorCodes.UserHasNoEmail);
        if (user.EmailConfirmedAt is null && !user.IsPlatformStaff) throw Fail.Rule(ErrorCodes.UserEmailNotVerified);
        await accounts.SendPasswordResetAsync(user, ct).ConfigureAwait(false);
    }

    /// <summary>FR-177: resetting a user's 2FA requires users.manage and is audited; the user sets it up again at next sign-in.</summary>
    public async Task ResetTwoFactorAsync(string userToken, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        changes.SetAction(user, "TwoFactorReset", reason);
        user.TwoFactorEnabledAt = null;
        user.TwoFactorSecret = null;
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0).ConfigureAwait(false); // soft-deletes the old codes
        await users.UpdateSecurityStampAsync(user).ConfigureAwait(false);
        await sessions.RevokeAllAsync(user.Id, "Two-factor authentication reset", null, ct).ConfigureAwait(false);
    }

    /// <summary>FR-176: administrators can sign out any user.</summary>
    public async Task SignOutAsync(string userToken, CancellationToken ct)
    {
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        await sessions.RevokeAllAsync(user.Id, "Signed out by an administrator", null, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ rules

    private async Task SetStatusAsync(string userToken, string status, string action, string? reason, CancellationToken ct)
    {
        if (status != "Active") RequireReason(reason);
        var (user, _) = await TargetAsync(userToken, ct).ConfigureAwait(false);
        if (user.Status == status) return;
        if (status == "Active" && limits is not null && user.TenantId is long tenant) await limits.EnsureUserAsync(tenant, ct).ConfigureAwait(false);   // P-07
        if (status != "Active")
        {
            await EnsureOwnerKeptAsync(user.Id, losingOwnerIn: null, ct).ConfigureAwait(false);
            var companies = await db.Set<UserCompanyRole>().AsNoTracking().Where(r => r.UserId == user.Id).Select(r => r.CompanyId)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var c in companies) await EnsureUserManagerKeptAsync(c, user.Id, ct).ConfigureAwait(false);
        }
        user.Status = status;
        changes.SetAction(user, action, reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (status != "Active") await AfterAccessChangeAsync(user.Id, action, ct).ConfigureAwait(false);
        else access.Invalidate(user.Id);
    }

    /// <summary>BR-001: after the change at least one other active user must hold the Owner role in an active company.</summary>
    private async Task EnsureOwnerKeptAsync(long userId, long? losingOwnerIn, CancellationToken ct, Role? newRole = null)
    {
        if (newRole is { IsPreset: true, Name: PresetRoles.Owner }) return;
        var ownerLinks = await db.Set<UserCompanyRole>().AsNoTracking()
            .Where(r => r.Role.IsPreset && r.Role.Name == PresetRoles.Owner && r.User.Status == "Active" && !r.User.IsDeleted && r.Company.Status == "Active")
            .Select(r => new { r.UserId, r.CompanyId }).ToListAsync(ct).ConfigureAwait(false);
        var isOwner = ownerLinks.Any(o => o.UserId == userId && (losingOwnerIn is null || o.CompanyId == losingOwnerIn));
        if (!isOwner) return;
        var remaining = ownerLinks.Where(o => !(o.UserId == userId && (losingOwnerIn is null || o.CompanyId == losingOwnerIn)));
        if (!remaining.Any()) throw Fail.Rule(ErrorCodes.LastOwner);
    }

    /// <summary>BR-002: the company must keep another active user whose role has users.manage.</summary>
    private async Task EnsureUserManagerKeptAsync(long companyId, long excludingUserId, CancellationToken ct)
    {
        var others = await db.Set<UserCompanyRole>().AsNoTracking()
            .AnyAsync(r => r.CompanyId == companyId && r.UserId != excludingUserId && r.User.Status == "Active" && !r.User.IsDeleted
                && r.Role.RolePermissions.Any(p => p.Permission.PermissionKey == PermissionKeys.UsersManage), ct).ConfigureAwait(false);
        if (!others) throw Fail.Rule(ErrorCodes.LastUserManager);
    }

    private async Task AfterAccessChangeAsync(long userId, string reason, CancellationToken ct)
    {
        access.Invalidate(userId);
        await sessions.RevokeAllAsync(userId, reason, null, ct).ConfigureAwait(false); // SEC-03
    }

    /// <summary>The target user must hold a role in the current company; otherwise 404.</summary>
    private Task<(User User, UserCompanyRole Link)> TargetAsync(string userToken, CancellationToken ct) => TargetAsync(userToken, true, ct);

    private async Task<(User User, UserCompanyRole Link)> TargetAsync(string userToken, bool accountWide, CancellationToken ct)
    {
        var publicId = tokens.Decode<User>(userToken) ?? throw Fail.NotFound();
        var link = await db.Set<UserCompanyRole>().Include(r => r.User)
            .SingleOrDefaultAsync(r => r.CompanyId == ctx.CompanyId && r.User.PublicId == publicId && !r.User.IsDeleted, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        await guard.EnsureCanManageUserAsync(link.UserId, accountWide, ct).ConfigureAwait(false); // never a user above the caller (SR-01, DT-202)
        return (link.User, link);
    }

    private async Task<Role> RoleByTokenAsync(string roleToken, CancellationToken ct)
    {
        var publicId = tokens.Decode<Role>(roleToken) ?? throw Fail.Field("role", ErrorCodes.ValidationFormat);
        return await db.Set<Role>().Include(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .SingleOrDefaultAsync(r => r.PublicId == publicId, ct).ConfigureAwait(false)
            ?? throw Fail.Field("role", ErrorCodes.ValidationFormat);
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        if (reason.Length > 500) throw Fail.Field("reason", ErrorCodes.ValidationRange, new FieldRef("reason"));
    }

    internal static void RequireReasonPublic(string? reason) => RequireReason(reason);
}
