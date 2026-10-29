using System.Globalization;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Contracts.Modules;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The setup checklist (5.4, P-08, G-10): eight steps in any order, each Not started / In progress / Done worked out from the company's data —
/// account (done at sign-up), company profile, market settings, chart of accounts, account mapping, master data, prices (tariffs and the current
/// month's rates), users — with explicit "Done" marks kept in cfg.CompanySettings (CORE.Setup.&lt;step&gt;) for steps the data cannot prove.
/// </summary>
public sealed class SetupChecklistService(IAppDbContext db, IRequestContext ctx, IChangeContext changes, IEnumerable<ISetupStepProvider> stepProviders)
{
    private static readonly string[] First = ["account", "profile"];
    private static readonly string[] Last = ["users"];

    /// <summary>Step keys in the order shown: the platform's first ones, the modules' (<see cref="ISetupStepProvider"/>, DT-346), the platform's last one.</summary>
    public IReadOnlyList<string> Steps => [.. First, .. stepProviders.SelectMany(p => p.Steps), .. Last];

    private const string Done = "Done";
    private const string InProgress = "InProgress";
    private const string NotStarted = "NotStarted";

    private bool En => ctx.Language == "en";

    public async Task<SetupChecklistResponse> GetAsync(CancellationToken ct)
    {
        var marks = await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey.StartsWith(SettingKeys.SetupPrefix) && s.SettingValue == "done")
            .Select(s => s.SettingKey).ToListAsync(ct).ConfigureAwait(false);
        bool Marked(string step) => marks.Contains(SettingKeys.SetupPrefix + step);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
        var steps = new List<SetupStep>();
        void Add(string key, string ar, string en, string status, string? detail, string link, IReadOnlyList<string>? items = null) =>
            steps.Add(new SetupStep(key, En ? en : ar, Marked(key) ? Done : status, Marked(key) ? null : detail, link, Marked(key), Marked(key) ? null : items));

        Add("account", "إنشاء الحساب", "Create account", Done, null, "/me");

        var profile = new[] { company.TaxNumber, company.Address, company.Phone }.Count(v => !string.IsNullOrWhiteSpace(v)) + (company.LogoFile is { Length: > 0 } ? 1 : 0);
        Add("profile", "ملف الشركة", "Company profile", profile == 4 ? Done : profile > 0 ? InProgress : NotStarted,
            profile == 4 ? null : En ? "Tax number, address, phone and logo" : "الرقم الضريبي والعنوان والهاتف والشعار", "/settings/profile");

        foreach (var provider in stepProviders)
            foreach (var s in await provider.StatesAsync(ctx.Language, ct).ConfigureAwait(false))
                Add(s.Key, s.TitleAr, s.TitleEn, s.Status, s.Detail, s.Link, s.Items);

        var users = await db.Set<UserCompanyRole>().AsNoTracking().CountAsync(r => r.CompanyId == ctx.CompanyId, ct).ConfigureAwait(false);
        var invitations = await db.Set<Invitation>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false);
        Add("users", "المستخدمون", "Users", users > 1 ? Done : invitations ? InProgress : NotStarted, null, "/users");

        return new SetupChecklistResponse(steps, steps.Count(s => s.Status == Done), steps.Count);
    }

    /// <summary>Marks a step done (or removes the mark) — settings.manage.</summary>
    public async Task<SetupChecklistResponse> MarkAsync(string step, bool done, CancellationToken ct)
    {
        if (!Steps.Contains(step)) throw Fail.NotFound();
        var key = SettingKeys.SetupPrefix + step;
        var row = await db.Set<CompanySetting>().SingleOrDefaultAsync(s => s.SettingKey == key, ct).ConfigureAwait(false);
        if (row is null)
        {
            if (!done) return await GetAsync(ct).ConfigureAwait(false);
            row = new CompanySetting { SettingKey = key, SettingValue = "done" };
            db.Set<CompanySetting>().Add(row);
        }
        else
        {
            row.SettingValue = done ? "done" : "open";
        }
        changes.SetAction(row, done ? "SetupDone" : "SetupReopened", step);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// The demo company (5.4, P-09, G-11): one per tenant, created by the Owner (tenant.manage), marked CORE.IsDemo so it never counts toward the
/// plan's companies, and filled through the normal services with sample data — the approved chart and mapping, customers, a partner, materials,
/// sites, own-fleet and partner drivers, tariffs, this month's driver rates, trips, an approved invoice and a receipt — without touching real data.
/// </summary>
public sealed class DemoCompanyService(IAppDbContext db, RequestContext ctx, DataScope scope, AccessService access, IUrlTokenService tokens, IEnumerable<ICompanyProvisioner> provisioning,
    IEnumerable<IDemoDataContributor> demoData, IEnumerable<IPermissionCatalog> catalogs, IPlatformLock? locks = null)
{
    public const string DemoName = "شركة تجريبية (للتدريب)";

    /// <summary>Sample trips of the demo company (not counted toward the monthly trip limit).</summary>
    public const int SampleTrips = 6;

    /// <summary>
    /// Creates and fills the demo company in one transaction (a failure leaves nothing half-filled), one request per tenant at a time
    /// (review R6).
    /// </summary>
    public async Task<DemoCompanyResponse> CreateAsync(CancellationToken ct)
    {
        var userId = ctx.UserId ?? throw Fail.Forbidden();
        var tenantId = ctx.TenantId ?? throw Fail.Forbidden();
        await using var held = locks is null ? null : await locks.TryAcquireAsync("demo-company:" + tenantId.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        if (locks is not null && held is null) throw Fail.Conflict(ErrorCodes.ConcurrencyConflict);
        var existing = await db.Set<CompanySetting>().AsNoTracking().IgnoreQueryFilters(["Company"])
            .Where(s => s.SettingKey == SettingKeys.IsDemo && s.SettingValue == "true").Select(s => s.CompanyId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existing != 0)
        {
            var c = await db.Set<Company>().AsNoTracking().SingleAsync(x => x.Id == existing, ct).ConfigureAwait(false);
            return new DemoCompanyResponse(tokens.Encode<Company>(c.PublicId), c.Name, false);
        }
        var company = await db.InTransactionAsync(async c =>
        {
            var co = new Company { Name = DemoName, NameEn = "Demo company (training)" };
            db.Set<Company>().Add(co);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            var ownerRole = await db.Set<Role>().SingleAsync(x => x.IsPreset && x.Name == PresetRoles.Owner, c).ConfigureAwait(false);
            db.Set<UserCompanyRole>().Add(new UserCompanyRole { UserId = userId, CompanyId = co.Id, RoleId = ownerRole.Id });
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            foreach (var p in provisioning) await p.ProvisionAsync(co.TenantId, co.Id, userId, c).ConfigureAwait(false);
            using (scope.Use(tenantId, co.Id, userId))
            {
                db.Set<CompanySetting>().Add(new CompanySetting { TenantId = tenantId, CompanyId = co.Id, SettingKey = SettingKeys.IsDemo, SettingValue = "true" });
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }
            await SeedAsync(co, userId, c).ConfigureAwait(false);
            return co;
        }, ct).ConfigureAwait(false);
        access.Invalidate(userId);
        return new DemoCompanyResponse(tokens.Encode<Company>(company.PublicId), company.Name, true);
    }

    /// <summary>Sample data through the business services, inside the demo company's scope and context.</summary>
    private async Task SeedAsync(Company company, long userId, CancellationToken ct)
    {
        var (oldCompany, oldPublicId, oldStatus, oldPermissions) = (ctx.CompanyId, ctx.CompanyPublicId, ctx.CompanyStatus, ctx.Permissions);
        using var _ = scope.Use(company.TenantId, company.Id, userId);
        try
        {
            // The caller is the demo company's Owner (tenant.manage checked on the route); the whole context is restored on every path (review R9).
            ctx.CompanyId = company.Id;
            ctx.CompanyPublicId = company.PublicId;
            ctx.CompanyStatus = "Active";
            ctx.Permissions = new HashSet<string>(catalogs.SelectMany(c => c.Keys), StringComparer.Ordinal);
            foreach (var contributor in demoData) await contributor.AddDemoDataAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            ctx.CompanyId = oldCompany;
            ctx.CompanyPublicId = oldPublicId;
            ctx.CompanyStatus = oldStatus;
            ctx.Permissions = oldPermissions;
        }
    }
}
