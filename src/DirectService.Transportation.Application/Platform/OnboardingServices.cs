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
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The setup checklist (5.4, P-08, G-10): eight steps in any order, each Not started / In progress / Done worked out from the company's data —
/// account (done at sign-up), company profile, market settings, chart of accounts, account mapping, master data, prices (tariffs and the current
/// month's rates), users — with explicit "Done" marks kept in cfg.CompanySettings (CORE.Setup.&lt;step&gt;) for steps the data cannot prove.
/// </summary>
public sealed class SetupChecklistService(IAppDbContext db, IRequestContext ctx, PostingEngine posting, MasterDataSupport support, IChangeContext changes)
{
    public static readonly IReadOnlyList<string> Steps = ["account", "profile", "market", "chart", "mapping", "masters", "prices", "users"];

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
        void Add(string key, string ar, string en, string status, string? detail, string link) =>
            steps.Add(new SetupStep(key, En ? en : ar, Marked(key) ? Done : status, Marked(key) ? null : detail, link, Marked(key)));

        Add("account", "إنشاء الحساب", "Create account", Done, null, "/me");

        var profile = new[] { company.TaxNumber, company.Address, company.Phone }.Count(v => !string.IsNullOrWhiteSpace(v)) + (company.LogoFile is { Length: > 0 } ? 1 : 0);
        Add("profile", "ملف الشركة", "Company profile", profile == 4 ? Done : profile > 0 ? InProgress : NotStarted,
            profile == 4 ? null : En ? "Tax number, address, phone and logo" : "الرقم الضريبي والعنوان والهاتف والشعار", "/settings/profile");

        var einv = await db.Set<EInvoiceSettings>().AsNoTracking().Select(e => e.IsEnabled).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Add("market", "إعدادات السوق", "Market settings", einv ? Done : InProgress,
            einv ? null : En ? "Currency and tax are set; e-invoicing credentials are optional at this stage" : "العملة والضريبة مضبوطتان؛ بيانات الفوترة الإلكترونية اختيارية الآن", "/settings/e-invoicing");

        var accounts = await db.Set<Account>().AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        Add("chart", "دليل الحسابات", "Chart of accounts", accounts > 0 ? Done : NotStarted, null, "/accounting/accounts");

        var missing = await posting.MissingKeysAsync(ct).ConfigureAwait(false);
        var mapped = await db.Set<PostingKeyMapping>().AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        Add("mapping", "ربط الحسابات", "Account mapping", missing.Count == 0 ? Done : mapped > 0 ? InProgress : NotStarted,
            missing.Count == 0 ? null : string.Join(", ", missing), "/accounting/posting-keys");

        var masters = new (string Ar, string En, bool Has)[]
        {
            ("السائقون", "drivers", await db.Set<Driver>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("العملاء", "customers", await db.Set<Customer>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("المواد", "materials", await db.Set<Material>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("مواقع التحميل", "loading sites", await db.Set<LoadingSite>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
            ("مواقع التنزيل", "unloading sites", await db.Set<UnloadingSite>().AsNoTracking().AnyAsync(ct).ConfigureAwait(false)),
        };
        var lacking = masters.Where(m => !m.Has).Select(m => En ? m.En : m.Ar).ToList();
        Add("masters", "البيانات الرئيسية", "Master data", lacking.Count == 0 ? Done : lacking.Count < masters.Length ? InProgress : NotStarted,
            lacking.Count == 0 ? null : string.Join("، ", lacking), "/drivers");

        var tariffs = await db.Set<Tariff>().AsNoTracking().AnyAsync(t => t.PartyType == "Customer", ct).ConfigureAwait(false);
        var month = (await support.TodayAsync(ct).ConfigureAwait(false)).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var activeSites = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        var rated = await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => r.RateMonth == month).Select(r => r.LoadingSiteId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var ratesComplete = activeSites.Count > 0 && activeSites.All(rated.Contains);
        Add("prices", "الأسعار", "Prices", tariffs && ratesComplete ? Done : tariffs || rated.Count > 0 ? InProgress : NotStarted,
            tariffs && ratesComplete ? null : En ? "Customer tariffs and this month's driver rates for every loading site" : "تعرفة العملاء وأسعار السائقين لهذا الشهر لكل موقع تحميل",
            "/monthly-rates");

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
public sealed class DemoCompanyService(IAppDbContext db, RequestContext ctx, DataScope scope, AccessService access, IUrlTokenService tokens, CompanyProvisioningService provisioning,
    ChartOfAccountsService chart, CustomerService customers, PartnerService partners, MaterialService materials, LoadingSiteService loadingSites,
    UnloadingSiteService unloadingSites, OwnershipTypeService ownershipTypes, DriverService drivers, TariffService tariffs, MonthlyRateService rates, TripService trips,
    InvoiceService invoices, ReceiptService receipts, MasterDataSupport support, IPlatformLock? locks = null)
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
            await provisioning.ProvisionAsync(co, userId, c).ConfigureAwait(false);
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
            ctx.Permissions = new HashSet<string>(PermissionKeys.All, StringComparer.Ordinal);
            await chart.ApplyTemplateAsync(ct).ConfigureAwait(false);
            var today = await support.TodayAsync(ct).ConfigureAwait(false);
            var potash = (await customers.CreateAsync(new CustomerRequest("شركة البوتاس (تجريبي)", "100200300", null, "0781234567", null, "العقبة", "العقبة", null, 30, null, null, null, null, null), ct)
                .ConfigureAwait(false)).Value;
            var phosphate = (await customers.CreateAsync(new CustomerRequest("مناجم الفوسفات (تجريبي)", "100200301", null, "0781234568", null, "عمّان", "عمّان", null, 45, null, null, null, null, null), ct)
                .ConfigureAwait(false)).Value;
            var partner = (await partners.CreateAsync(new PartnerRequest("شركة النور للنقل (تجريبي)", null, null, "0791234567", null, null, null, null, null, null, null), ct).ConfigureAwait(false)).Value;
            var material = (await materials.CreateAsync(new MaterialRequest("بوتاس", "Potash", null, null), ct).ConfigureAwait(false)).Value;
            var loading = (await loadingSites.CreateAsync(new SiteRequest("غور الصافي", "الأغوار", null, null), ct).ConfigureAwait(false)).Value;
            var unloading = (await unloadingSites.CreateAsync(new SiteRequest("ميناء العقبة", "العقبة", null, null), ct).ConfigureAwait(false)).Value;
            var types = await ownershipTypes.ListAsync(ct).ConfigureAwait(false);
            var rented = (await ownershipTypes.CreateAsync(new OwnershipTypeRequest("مستأجرة", "External", null, null), ct).ConfigureAwait(false)).Value;
            DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId) =>
                new(name, nid, "0791234567", null, null, "L-" + nid[^3..], "C", today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                    today.AddYears(1), today.AddYears(1), today.AddYears(1), null, null, null, null);
            var own1 = (await drivers.CreateAsync(Driver("أحمد صالح (تجريبي)", "9000000001", "10-90001", types.Single(t => t.IsBuiltIn).Id, null), ct).ConfigureAwait(false)).Value;
            var own2 = (await drivers.CreateAsync(Driver("محمود الحويطات (تجريبي)", "9000000002", "10-90002", types.Single(t => t.IsBuiltIn).Id, null), ct).ConfigureAwait(false)).Value;
            var ext = (await drivers.CreateAsync(Driver("سامي العبادي (تجريبي)", "9000000003", "10-90003", rented.Id, partner.Id), ct).ConfigureAwait(false)).Value;
            await tariffs.CreateAsync(new TariffRequest("Customer", potash.Id, material.Id, null, null, 6.000m, null, null, null, null, null), ct).ConfigureAwait(false);
            await tariffs.CreateAsync(new TariffRequest("Customer", phosphate.Id, null, null, null, 5.250m, null, null, null, null, null), ct).ConfigureAwait(false);
            await tariffs.CreateAsync(new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null), ct).ConfigureAwait(false);
            await rates.SaveAsync(today.ToString("yyyy-MM", CultureInfo.InvariantCulture), new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]), ct)
                .ConfigureAwait(false);
            var tripIds = new List<string>();
            var plan = new (DriverResponse Driver, string? Partner, CustomerResponse Customer, decimal Loaded, decimal Unloaded)[]
            {
                (own1, null, potash, 32.000m, 31.850m), (own2, null, potash, 31.500m, 31.400m), (ext, partner.Id, potash, 32.000m, 31.900m),
                (own1, null, phosphate, 30.000m, 29.950m), (own2, null, phosphate, 30.500m, 30.100m), (ext, partner.Id, phosphate, 31.000m, 31.000m),
            };
            var n = 0;
            foreach (var (driver, partnerId, customer, loaded, unloaded) in plan)
            {
                var saved = await trips.CreateAsync(new TripRequest("DEMO-" + (++n).ToString("D3", CultureInfo.InvariantCulture), today, null, null, driver.Id, customer.Id, loading.Id,
                    unloading.Id, material.Id, partnerId, loaded, unloaded, null, null, null, null, null, null, null), ct).ConfigureAwait(false);
                if (customer.Id == potash.Id) tripIds.Add(saved.Value.Trip.Id);
            }
            var invoice = (await invoices.CreateAsync(new InvoiceRequest(potash.Id, "Trip", today, null, tripIds, null, 0, 0, null, null, null, true, null, null), ct)
                .ConfigureAwait(false)).Value;
            var box = await db.Set<CashBox>().AsNoTracking().Select(b => b.PublicId).FirstAsync(ct).ConfigureAwait(false);
            await receipts.CreateAsync(new ReceiptRequest(potash.Id, today, invoice.Invoice.Id, Math.Round(invoice.Invoice.Total / 2, 3), "Cash", tokens.Encode<CashBox>(box), null, null, 0,
                null, null), ct).ConfigureAwait(false);
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
