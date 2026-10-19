using System.Security.Cryptography;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Identity;

/// <summary>Result of validating a session on a request.</summary>
public sealed record SessionPrincipal(
    long SessionId, long UserId, Guid UserPublicId, long? TenantId, bool IsPlatformStaff,
    string FullName, string LanguageCode, string SecurityStamp);

/// <summary>
/// Server-side sessions (FR-176, SEC-03, ADR-05): one core.UserSessions row per sign-in; only the
/// hash of the session token is stored. A request is accepted only while the session is not
/// revoked, not past its absolute expiry (12 h, Q-04) and not idle longer than the tenant's
/// SessionIdleTimeoutMinutes (rev. 5), and while the user's security stamp is unchanged.
/// </summary>
public sealed class SessionService(IAppDbContext db, IDataScope scope, TimeProvider clock, IMemoryCache cache, IOptions<AppOptions> options)
{
    private static readonly string[] TenantFilter = ["Tenant"];
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creates a session and returns the raw token (stored only in the encrypted auth cookie).</summary>
    public async Task<(string Token, long SessionId)> CreateAsync(User user, string? ip, string? userAgent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow().UtcDateTime;
        var session = new UserSession
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = HashToken(token),
            LastSeenAt = now,
            ExpiresAt = now.AddHours(options.Value.AbsoluteSessionHours),
            IpAddress = Truncate(ip, 45),
            UserAgent = Truncate(userAgent, 300),
        };
        using (scope.Use(user.TenantId, null, user.Id))
        {
            db.Set<UserSession>().Add(session);
            user.LastSignInAt = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return (token, session.Id);
    }

    /// <summary>Validates a session token; null when the session must be rejected.</summary>
    public async Task<SessionPrincipal?> ValidateAsync(string token, string securityStampFromCookie, CancellationToken ct)
    {
        var hash = HashToken(token);
        var now = clock.GetUtcNow().UtcDateTime;
        if (cache.TryGetValue<SessionPrincipal>(CacheKey(hash), out var cached) && cached is not null
            && cached.SecurityStamp == securityStampFromCookie)
        {
            return cached;
        }

        // Before any tenant is known, by the unique token hash; every filter is lifted on purpose (the tenant sub-query lifted them for the whole
        // query anyway, DT-217) and the row's own RevokedAt, expiry and user status decide below.
        var row = await db.Set<UserSession>().IgnoreQueryFilters()
            .Where(s => s.TokenHash == hash)
            .Select(s => new
            {
                Session = s,
                s.UserId,
                UserPublicId = db.Set<User>().IgnoreQueryFilters(TenantFilter).Where(u => u.Id == s.UserId).Select(u => u.PublicId).First(),
                User = db.Set<User>().IgnoreQueryFilters(TenantFilter).Where(u => u.Id == s.UserId)
                    .Select(u => new { u.TenantId, u.IsPlatformStaff, u.FullName, u.LanguageCode, u.SecurityStamp, u.Status, u.TwoFactorEnabledAt }).First(),
                IdleMinutes = db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == s.TenantId).Select(t => (int?)t.SessionIdleTimeoutMinutes).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false);

        if (row is null) return null;
        var s = row.Session;
        var idle = TimeSpan.FromMinutes(row.IdleMinutes ?? options.Value.PlatformStaffIdleMinutes);
        if (s.RevokedAt is not null || s.ExpiresAt <= now || s.LastSeenAt + idle <= now) return null;
        // Revision 6: 2FA is no longer required of every tenant user (the sign-in flow decides); platform staff always need it.
        if (row.User.Status != "Active" || (row.User.IsPlatformStaff && row.User.TwoFactorEnabledAt is null)) return null;
        if (!string.Equals(row.User.SecurityStamp, securityStampFromCookie, StringComparison.Ordinal)) return null;

        if (now - s.LastSeenAt >= TouchInterval)
        {
            s.LastSeenAt = now;
            using (scope.Use(row.User.TenantId, null, row.UserId))
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var principal = new SessionPrincipal(s.Id, row.UserId, row.UserPublicId, row.User.TenantId, row.User.IsPlatformStaff,
            row.User.FullName, row.User.LanguageCode, row.User.SecurityStamp);
        // DT-211: the short cache never outlives the session's own limits (12-hour lifetime, idle timeout).
        var until = Min(s.ExpiresAt, s.LastSeenAt + idle) - now;
        cache.Set(CacheKey(hash), principal, until < CacheFor ? until : CacheFor);
        return principal;
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>
    /// Ends every session past its absolute lifetime or idle longer than its tenant's timeout (platform staff: their own timeout) — owner
    /// testing 19 Oct 2026 (DT-211): such sessions were already refused, but stayed listed as "active". Revocation = RevokedAt + soft
    /// delete with the reason, like a sign-out. Run by the session clean-up job; returns the number ended.
    /// </summary>
    public async Task<int> EndExpiredAsync(int batch, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var staffIdle = options.Value.PlatformStaffIdleMinutes;
        var stale = await db.Set<UserSession>().IgnoreQueryFilters()
            .Where(s => s.RevokedAt == null && (s.ExpiresAt <= now
                || s.LastSeenAt.AddMinutes(db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == s.TenantId).Select(t => (int?)t.SessionIdleTimeoutMinutes).FirstOrDefault() ?? staffIdle) <= now))
            .OrderBy(s => s.Id).Take(batch)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in stale)
        {
            Revoke(s, s.ExpiresAt <= now ? "Expired: absolute session lifetime" : "Expired: idle timeout");
            cache.Remove(CacheKey(s.TokenHash));
            using (scope.Use(s.TenantId, null, s.UserId))
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return stale.Count;
    }

    /// <summary>The idle timeout of a user's sessions: the tenant's setting, or the platform staff timeout.</summary>
    public async Task<TimeSpan> IdleTimeoutAsync(long? tenantId, CancellationToken ct)
    {
        var minutes = tenantId is null ? null : await db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == tenantId)
            .Select(t => (int?)t.SessionIdleTimeoutMinutes).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return TimeSpan.FromMinutes(minutes ?? options.Value.PlatformStaffIdleMinutes);
    }

    /// <summary>Drops a session's cached user data (language, name), so the next request reads them again — e.g. after a language change (FR-178).</summary>
    public async Task ForgetAsync(long sessionId, CancellationToken ct)
    {
        var hash = await db.Set<UserSession>().IgnoreQueryFilters(TenantFilter).Where(s => s.Id == sessionId).Select(s => s.TokenHash).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (hash is not null) cache.Remove(CacheKey(hash));
    }

    /// <summary>Revokes one session (sign-out). Revocation = RevokedAt + soft delete (design §7).</summary>
    public async Task RevokeAsync(long sessionId, string reason, CancellationToken ct)
    {
        var s = await db.Set<UserSession>().IgnoreQueryFilters(TenantFilter).SingleOrDefaultAsync(x => x.Id == sessionId, ct).ConfigureAwait(false);
        if (s is null) return;
        Revoke(s, reason);
        using (scope.Use(s.TenantId, null, scope.UserId ?? s.UserId))
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.Remove(CacheKey(s.TokenHash));
    }

    /// <summary>Revokes every session of a user, optionally keeping one (SEC-03: password reset, role change, sign out everywhere).</summary>
    public async Task<int> RevokeAllAsync(long userId, string reason, long? exceptSessionId, CancellationToken ct)
    {
        var sessions = await db.Set<UserSession>().IgnoreQueryFilters(TenantFilter)
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.Id != exceptSessionId)
            .ToListAsync(ct).ConfigureAwait(false);
        if (sessions.Count == 0) return 0;
        foreach (var s in sessions)
        {
            Revoke(s, reason);
            cache.Remove(CacheKey(s.TokenHash));
        }
        using (scope.Use(sessions[0].TenantId, null, scope.UserId ?? userId))
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sessions.Count;
    }

    private void Revoke(UserSession s, string reason)
    {
        s.RevokedAt = clock.GetUtcNow().UtcDateTime;
        s.DeleteReason = reason;
        db.Set<UserSession>().Remove(s);
    }

    private static string CacheKey(string hash) => "session:" + hash;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
