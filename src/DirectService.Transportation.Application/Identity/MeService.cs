using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>
/// The signed-in user's own data: profile with the companies for the switcher (5.1, US-34), preferences
/// (FR-178), and active sessions with sign-out of one or all others (FR-176).
/// </summary>
public sealed class MeService(IAppDbContext db, IRequestContext ctx, AccessService access, SessionService sessions, IUrlTokenService tokens, TimeProvider clock)
{
    private static readonly string[] AllowedDateFormats = ["dd/MM/yyyy", "yyyy-MM-dd", "MM/dd/yyyy"];

    public async Task<MeResponse> GetAsync(CancellationToken ct)
    {
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        var u = await db.Set<User>().IgnoreQueryFilters(["Tenant"]).AsNoTracking().SingleAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        var tenant = u.TenantId is null ? null : await db.Set<Tenant>().AsNoTracking()
            .Where(t => t.Id == u.TenantId).Select(t => new { t.Name, t.Status }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        var grants = await access.GrantsAsync(userId, ct).ConfigureAwait(false);
        var isOwner = grants.Any(g => g.Permissions.Contains(Contracts.Security.PermissionKeys.TenantManage));
        var visible = grants.Where(g => g.CompanyStatus == "Active" || isOwner).ToList();
        var ids = visible.Select(g => g.CompanyId).ToList();
        var currencies = await db.Set<Company>().AsNoTracking().IgnoreQueryFilters().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.CurrencyCode, c.CurrencyDecimals }).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var companies = visible
            .Select(g => new CompanyAccess(tokens.Encode<Company>(g.CompanyPublicId), g.CompanyName, g.CompanyNameEn, g.CompanyStatus,
                g.RoleName, g.IsPresetRole, g.Permissions.Order(StringComparer.Ordinal).ToList(),
                currencies.TryGetValue(g.CompanyId, out var cur) ? cur.CurrencyCode : "JOD", currencies.TryGetValue(g.CompanyId, out cur) ? cur.CurrencyDecimals : 3))
            .ToList();
        var defaultCompany = grants.FirstOrDefault(g => g.CompanyId == u.DefaultCompanyId && g.CompanyStatus == "Active")
            ?? grants.FirstOrDefault(g => g.CompanyStatus == "Active");
        return new MeResponse(tokens.Encode<User>(u.PublicId), u.FullName, u.Email, u.LanguageCode, u.DateFormat,
            defaultCompany is null ? null : tokens.Encode<Company>(defaultCompany.CompanyPublicId),
            u.IsPlatformStaff, tenant?.Name, tenant?.Status, companies, u.UserName, u.TwoFactorEnabledAt is not null, PendingEmail: u.PendingEmail);
    }

    public async Task UpdatePreferencesAsync(PreferencesRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        new Validator()
            .Check(r.LanguageCode is "ar" or "en", "languageCode")
            .Check(AllowedDateFormats.Contains(r.DateFormat), "dateFormat")
            .ThrowIfInvalid();
        var u = await db.Set<User>().IgnoreQueryFilters(["Tenant"]).SingleAsync(x => x.Id == userId, ct).ConfigureAwait(false);
        long? defaultCompanyId = null;
        if (!string.IsNullOrEmpty(r.DefaultCompany))
        {
            var publicId = tokens.Decode<Company>(r.DefaultCompany) ?? throw Fail.Field("defaultCompany", ErrorCodes.ValidationFormat);
            var grant = (await access.GrantsAsync(userId, ct).ConfigureAwait(false))
                .FirstOrDefault(g => g.CompanyPublicId == publicId && g.CompanyStatus == "Active")
                ?? throw Fail.Field("defaultCompany", ErrorCodes.ValidationFormat);
            defaultCompanyId = grant.CompanyId;
        }
        u.LanguageCode = r.LanguageCode;
        u.DateFormat = r.DateFormat;
        u.DefaultCompanyId = defaultCompanyId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        // The session caches the language: without this, server messages stayed in the old language after a change (found by E2E).
        if (ctx.SessionId is long session) await sessions.ForgetAsync(session, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionResponse>> SessionsAsync(CancellationToken ct)
    {
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        // DT-211: only sessions that are still valid — not revoked, within the absolute lifetime and not idle past the timeout.
        var now = clock.GetUtcNow().UtcDateTime;
        // The user's own tenant (staff working inside a tenant keep the staff timeout; security review 19 Oct 2026).
        var ownTenant = await db.Set<User>().IgnoreQueryFilters(["Tenant"]).Where(u => u.Id == userId).Select(u => u.TenantId).SingleAsync(ct).ConfigureAwait(false);
        var idleSince = now - await sessions.IdleTimeoutAsync(ownTenant, ct).ConfigureAwait(false);
        var rows = await db.Set<UserSession>().IgnoreQueryFilters(["Tenant"]).AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now && s.LastSeenAt > idleSince)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new { s.Id, s.PublicId, s.CreatedAt, s.LastSeenAt, s.IpAddress, s.UserAgent, s.ExpiresAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(s => new SessionResponse(tokens.Encode<UserSession>(s.PublicId), s.CreatedAt, s.LastSeenAt,
            s.IpAddress, s.UserAgent, s.Id == ctx.SessionId)).ToList();
    }

    public async Task RevokeSessionAsync(string sessionToken, CancellationToken ct)
    {
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        var publicId = tokens.Decode<UserSession>(sessionToken) ?? throw Fail.NotFound();
        var id = await db.Set<UserSession>().IgnoreQueryFilters(["Tenant"]).AsNoTracking()
            .Where(s => s.PublicId == publicId && s.UserId == userId).Select(s => (long?)s.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        await sessions.RevokeAsync(id, "Signed out by the user", ct).ConfigureAwait(false);
    }

    public Task<int> SignOutOthersAsync(CancellationToken ct)
    {
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        return sessions.RevokeAllAsync(userId, "Signed out everywhere by the user", ctx.SessionId, ct);
    }
}
