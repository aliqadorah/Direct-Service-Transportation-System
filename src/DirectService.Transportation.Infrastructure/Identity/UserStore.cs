using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity store over the design's own tables (build prompt: no AspNet* tables):
/// core.Users and core.UserRecoveryCodes. Mapping (A-16): user id = PublicId; user name = email;
/// EmailConfirmed = EmailConfirmedAt IS NOT NULL; lockout = FailedSignInCount / LockedUntil (always on);
/// TwoFactorEnabled = TwoFactorEnabledAt IS NOT NULL; authenticator key encrypted into TwoFactorSecret;
/// recovery codes hashed in core.UserRecoveryCodes. Look-ups by email/id are platform-wide
/// (email is unique platform-wide) and therefore bypass only the Tenant filter, never SoftDelete.
/// </summary>
public sealed class UserStore(MasterDbContext db, IDataProtectionProvider protection, IPasswordHasher<User> hasher, TimeProvider clock, IDataScope scope) :
    IUserPasswordStore<User>,
    IUserEmailStore<User>,
    IUserSecurityStampStore<User>,
    IUserLockoutStore<User>,
    IUserTwoFactorStore<User>,
    IUserAuthenticatorKeyStore<User>,
    IUserTwoFactorRecoveryCodeStore<User>
{
    private readonly IDataProtector _secretProtector = protection.CreateProtector("DST.Users.TwoFactorSecret.v1");
    private static readonly string[] TenantFilter = [QueryFilters.Tenant];

    private IQueryable<User> Users => db.Set<User>().IgnoreQueryFilters(TenantFilter);

    public void Dispose() { }

    // ---------------- IUserStore

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PublicId.ToString("N"));

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.Email);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        user.Email = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        db.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        if (db.Entry(user).State == EntityState.Detached) db.Attach(user).State = EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }
        return IdentityResult.Success;
    }

    /// <summary>Users are never deleted (BR-003); archive instead.</summary>
    public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Users are never deleted (BR-003). Disable or archive the user instead.");

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(userId, "N", out var publicId)) return null;
        return await Users.SingleOrDefaultAsync(u => u.PublicId == publicId, cancellationToken).ConfigureAwait(false);
    }

    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedUserName, cancellationToken);

    // ---------------- password

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(string.IsNullOrEmpty(user.PasswordHash) ? null : user.PasswordHash);

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // ---------------- email

    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.Email);

    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmedAt is not null);

    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmedAt = confirmed ? user.EmailConfirmedAt ?? clock.GetUtcNow().UtcDateTime : null;
        return Task.CompletedTask;
    }

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---------------- security stamp

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SecurityStamp);

    // ---------------- lockout (FR-175: 5 failures → 15 minutes; configured in IdentityOptions)

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.LockedUntil is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : (DateTimeOffset?)null);

    public Task SetLockoutEndDateAsync(User user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.LockedUntil = lockoutEnd?.UtcDateTime;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(++user.FailedSignInCount);

    public Task ResetAccessFailedCountAsync(User user, CancellationToken cancellationToken)
    {
        user.FailedSignInCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.FailedSignInCount);

    public Task<bool> GetLockoutEnabledAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task SetLockoutEnabledAsync(User user, bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;

    // ---------------- two-factor (FR-177: mandatory, cannot be disabled by the user)

    public Task SetTwoFactorEnabledAsync(User user, bool enabled, CancellationToken cancellationToken)
    {
        user.TwoFactorEnabledAt = enabled ? user.TwoFactorEnabledAt ?? clock.GetUtcNow().UtcDateTime : null;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.TwoFactorEnabledAt is not null);

    public Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken)
    {
        user.TwoFactorSecret = _secretProtector.Protect(System.Text.Encoding.UTF8.GetBytes(key));
        return Task.CompletedTask;
    }

    public Task<string?> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken)
    {
        if (user.TwoFactorSecret is null || user.TwoFactorSecret.Length == 0) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(_secretProtector.Unprotect(user.TwoFactorSecret)));
    }

    // ---------------- recovery codes (hashed; regenerating soft-deletes the previous codes)

    public async Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        var existing = await db.Set<UserRecoveryCode>().IgnoreQueryFilters(TenantFilter)
            .Where(c => c.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        using (scope.Use(user.TenantId, scope.CompanyId, scope.UserId ?? user.Id))
        {
            foreach (var c in existing)
            {
                c.DeleteReason = "Recovery codes regenerated";
                db.Remove(c);
            }
            foreach (var code in recoveryCodes)
            {
                db.Add(new UserRecoveryCode { UserId = user.Id, TenantId = user.TenantId, CodeHash = hasher.HashPassword(user, code) });
            }
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        var codes = await db.Set<UserRecoveryCode>().IgnoreQueryFilters(TenantFilter)
            .Where(c => c.UserId == user.Id && c.UsedAt == null).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var c in codes)
        {
            if (hasher.VerifyHashedPassword(user, c.CodeHash, code) == PasswordVerificationResult.Failed) continue;
            c.UsedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    public Task<int> CountCodesAsync(User user, CancellationToken cancellationToken) =>
        db.Set<UserRecoveryCode>().IgnoreQueryFilters(TenantFilter)
            .CountAsync(c => c.UserId == user.Id && c.UsedAt == null, cancellationToken);
}
