using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Tenant-level administration (core.tenant.manage): tenant security settings (FR-175/176, rev. 5),
/// companies — create within the plan limit (5.3, Q-06: only Active, non-demo companies count),
/// archive and restore (D-09). Every change is audited with its reason.
/// </summary>
public sealed class TenantService(
    IAppDbContext db, IRequestContext ctx, IChangeContext changes, CompanyProvisioningService provisioning,
    AccessService access, IUrlTokenService tokens, SessionService? sessions = null)
{
    public async Task<TenantResponse> GetAsync(CancellationToken ct)
    {
        var t = await db.Set<Tenant>().AsNoTracking().Where(x => x.Id == ctx.TenantId)
            .Select(x => new { x.Name, x.Status, x.Plan.Code, x.Plan.NameAr, x.Plan.NameEn, x.TrialEndsAt, x.CurrentPeriodEnd,
                x.PasswordMinLength, x.PasswordRequireLetter, x.PasswordRequireDigit, x.SessionIdleTimeoutMinutes, x.RowVersion, x.RequireTwoFactor })
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        return new TenantResponse(t.Name, t.Status, t.Code, ctx.Language == "en" ? t.NameEn : t.NameAr, t.TrialEndsAt, t.CurrentPeriodEnd,
            t.PasswordMinLength, t.PasswordRequireLetter, t.PasswordRequireDigit, t.SessionIdleTimeoutMinutes, Convert.ToBase64String(t.RowVersion), t.RequireTwoFactor);
    }

    public async Task<TenantResponse> UpdateSecurityAsync(TenantSecurityRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        new Validator()
            .Check(r.PasswordMinLength is >= 8 and <= 64, "passwordMinLength", ErrorCodes.ValidationRange)
            .Check(r.SessionIdleTimeoutMinutes is >= 5 and <= 1440, "sessionIdleTimeoutMinutes", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        var t = await db.Set<Tenant>().SingleAsync(x => x.Id == ctx.TenantId, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, r.RowVersion);
        t.PasswordMinLength = (byte)r.PasswordMinLength;
        t.PasswordRequireLetter = r.PasswordRequireLetter;
        t.PasswordRequireDigit = r.PasswordRequireDigit;
        t.SessionIdleTimeoutMinutes = (short)r.SessionIdleTimeoutMinutes;
        var turnedOn = r.RequireTwoFactor && !t.RequireTwoFactor;
        t.RequireTwoFactor = r.RequireTwoFactor;   // users without 2FA set it up at their next sign-in (revision 6)
        changes.SetAction(t, "SettingChange");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (turnedOn && sessions is not null)
        {
            // Security review (revision 6): "at their next sign-in" starts now — sessions of users without 2FA end (except this one).
            var without = await db.Set<User>().Where(u => u.TenantId == t.Id && u.TwoFactorEnabledAt == null && !u.IsDeleted).Select(u => u.Id)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var id in without) await sessions.RevokeAllAsync(id, "Two-step verification required for all users", ctx.SessionId, ct).ConfigureAwait(false);
        }
        return await GetAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CompanyResponse>> CompaniesAsync(CancellationToken ct)
    {
        var demo = await DemoCompanyIdsAsync(ct).ConfigureAwait(false);
        var rows = await db.Set<Company>().AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.PublicId, c.Name, c.NameEn, c.Status, c.CreatedAt, c.RowVersion }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(c => new CompanyResponse(tokens.Encode<Company>(c.PublicId), c.Name, c.NameEn, c.Status, demo.Contains(c.Id),
            c.CreatedAt, Convert.ToBase64String(c.RowVersion))).ToList();
    }

    /// <summary>Creates a company; the creator receives the Owner role in it (BR-002 is satisfied by the creator).</summary>
    public async Task<CompanyResponse> CreateCompanyAsync(CreateCompanyRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = TextNormalizer.CleanText(r.Name);
        var nameEn = TextNormalizer.CleanText(r.NameEn);
        new Validator().Require("name", name).MaxLength("name", name, 200).MaxLength("nameEn", nameEn, 200).ThrowIfInvalid();
        if (await db.Set<Company>().AnyAsync(c => c.Name == name, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        await EnsureCompanyLimitAsync(ct).ConfigureAwait(false);

        var company = await db.InTransactionAsync(async c =>
        {
            var co = new Company { Name = name!, NameEn = nameEn };
            db.Set<Company>().Add(co);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            var ownerRole = await db.Set<Role>().SingleAsync(x => x.IsPreset && x.Name == PresetRoles.Owner, c).ConfigureAwait(false);
            db.Set<UserCompanyRole>().Add(new UserCompanyRole { UserId = ctx.UserId!.Value, CompanyId = co.Id, RoleId = ownerRole.Id });
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await provisioning.ProvisionAsync(co, ctx.UserId, c).ConfigureAwait(false);
            return co;
        }, ct).ConfigureAwait(false);
        access.Invalidate(ctx.UserId!.Value);
        return (await CompaniesAsync(ct).ConfigureAwait(false)).Single(x => x.Id == tokens.Encode<Company>(company.PublicId));
    }

    public Task ArchiveCompanyAsync(string companyToken, string reason, CancellationToken ct) => SetCompanyStatusAsync(companyToken, "Archived", "Archive", reason, ct);

    public Task RestoreCompanyAsync(string companyToken, string reason, CancellationToken ct) => SetCompanyStatusAsync(companyToken, "Active", "Restore", reason, ct);

    private async Task SetCompanyStatusAsync(string companyToken, string status, string action, string reason, CancellationToken ct)
    {
        UserAdminService.RequireReasonPublic(reason);
        var publicId = tokens.Decode<Company>(companyToken) ?? throw Fail.NotFound();
        var co = await db.Set<Company>().SingleOrDefaultAsync(c => c.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        if (co.Status == status) return;
        if (status == "Archived")
        {
            var otherActive = await db.Set<Company>().AnyAsync(c => c.Id != co.Id && c.Status == "Active", ct).ConfigureAwait(false);
            if (!otherActive) throw Fail.Rule(ErrorCodes.LastActiveCompany);
        }
        else
        {
            await EnsureCompanyLimitAsync(ct).ConfigureAwait(false);
        }
        co.Status = status;
        changes.SetAction(co, action, reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var users = await db.Set<UserCompanyRole>().AsNoTracking().Where(u => u.CompanyId == co.Id).Select(u => u.UserId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var u in users) access.Invalidate(u);
    }

    /// <summary>5.3 / BR-013: creating beyond the plan limit is blocked with an upgrade message; only Active, non-demo companies count (Q-06, G-11).</summary>
    private async Task EnsureCompanyLimitAsync(CancellationToken ct)
    {
        var max = await db.Set<Tenant>().Where(t => t.Id == ctx.TenantId).Select(t => t.Plan.MaxCompanies).SingleAsync(ct).ConfigureAwait(false);
        var demo = await DemoCompanyIdsAsync(ct).ConfigureAwait(false);
        var active = await db.Set<Company>().Where(c => c.Status == "Active").Select(c => c.Id).ToListAsync(ct).ConfigureAwait(false);
        if (active.Count(id => !demo.Contains(id)) >= max)
            throw Fail.Conflict(ErrorCodes.PlanLimitReached, max);
    }

    private async Task<HashSet<long>> DemoCompanyIdsAsync(CancellationToken ct) =>
        (await db.Set<CompanySetting>().IgnoreQueryFilters(["Company"])
            .Where(s => s.SettingKey == SettingKeys.IsDemo && s.SettingValue == "true")
            .Select(s => s.CompanyId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
}
