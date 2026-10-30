using DirectService.Platform.Contracts.Modules;
using DirectService.Platform.Domain;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Modules;

// What Transportation (with accounting until Stage 3) gives the Platform services (platform design a.3, Stage 2 session 12, DT-346).
// The code is today's, moved out of the Platform services unchanged.

/// <summary>Trips saved since a date, against the plan's MaxTripsPerMonth (<see cref="PlanLimits"/>).</summary>
public sealed class TripUsageMeter(IAppDbContext db) : IUsageMeter
{
    private static readonly string[] AllFilters = ["Tenant", "Company"];

    public string Measure => UsageMeasures.Trips;

    /// <summary>
    /// Trips of the demo company count only beyond its sample trips (G-11): practising is free, running real work in it is not (review R7).
    /// </summary>
    public async Task<int> CountAsync(long tenantId, DateTime fromUtc, CancellationToken ct)
    {
        var demo = db.Set<CompanySetting>().AsNoTracking().IgnoreQueryFilters(AllFilters)
            .Where(s => s.TenantId == tenantId && s.SettingKey == SettingKeys.IsDemo && s.SettingValue == "true").Select(s => s.CompanyId);
        var trips = db.Set<Trip>().AsNoTracking().IgnoreQueryFilters(AllFilters).Where(t => t.TenantId == tenantId && !t.IsDeleted && t.CreatedAt >= fromUtc);
        var real = await trips.CountAsync(t => !demo.Contains(t.CompanyId), ct).ConfigureAwait(false);
        var practice = await trips.CountAsync(t => demo.Contains(t.CompanyId), ct).ConfigureAwait(false);
        return real + Math.Max(0, practice - TransportationDemoData.SampleTrips);
    }
}

/// <summary>
/// SEC-19 for the module's rows of a deleted tenant (<see cref="TenantLifecycleService"/>): drivers (name, national ID, licence, phones, address,
/// bank details, notes, custom fields) and the contact details of customers, partners and AP/AR parties.
/// </summary>
public sealed class TransportationDataEraser(IAppDbContext db) : ITenantDataEraser
{
    public async Task AnonymiseAsync(long tenantId, DateTime now, CancellationToken ct)
    {
        string[] none = ["Tenant", "Company", "SoftDelete"];
        await db.Set<Driver>().IgnoreQueryFilters(none).Where(d => d.TenantId == tenantId)
            // Placeholders from the public id: internal ids never become data (review R11).
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.FullName, "Deleted driver").SetProperty(x => x.NationalId, x => ("X" + x.PublicId.ToString()).Substring(0, 9))
                .SetProperty(x => x.LicenceNo, x => ("X" + x.PublicId.ToString()).Substring(0, 9)).SetProperty(x => x.Notes, (string?)null).SetProperty(x => x.CustomValues, (string?)null)
                .SetProperty(x => x.Phone1, "0000000000").SetProperty(x => x.Phone2, (string?)null).SetProperty(x => x.Address, (string?)null)
                .SetProperty(x => x.Iban, (string?)null).SetProperty(x => x.CliqAlias, (string?)null).SetProperty(x => x.BankName, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Customer>().IgnoreQueryFilters(none).Where(c => c.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactPerson, (string?)null).SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.Email, (string?)null)
                .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
        await db.Set<Partner>().IgnoreQueryFilters(none).Where(p => p.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactPerson, (string?)null).SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Domain.Acc.ApArParty>().IgnoreQueryFilters(none).Where(p => p.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.Address, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// The permission keys and preset roles of <see cref="PermissionKeys"/> / <see cref="PresetRoles"/> (values unchanged). Until the platform
/// and accounting keys move to their modules this catalogue holds every key.
/// </summary>
public sealed class TransportationPermissions : IPermissionCatalog
{
    public IReadOnlyList<string> Keys => PermissionKeys.All;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> PresetRoles => Contracts.Security.PresetRoles.Permissions;

    /// <summary>Read-only permissions of a support session (G-09: all view permissions of the company, nothing that creates or changes;
    /// <c>SupportAccessService.SessionPermissions</c> before Stage 2 session 13).</summary>
    public static readonly IReadOnlyList<string> SupportSession =
        [PermissionKeys.TripsView, PermissionKeys.InvView, PermissionKeys.DrvView, PermissionKeys.JeView, PermissionKeys.AcctView, PermissionKeys.ReportsView];

    public IReadOnlyList<string> SupportSessionKeys => SupportSession;
}

/// <summary>
/// The module's figures on the provider's health page (P-15; the queries of <c>ConsoleService.HealthAsync</c> before Stage 2 session 13): trips
/// saved this month, postings waiting, e-invoices queued or rejected — over every tenant, filters ignored.
/// </summary>
public sealed class TransportationHealthFigures(IAppDbContext db) : IHealthFigureSource
{
    public async Task<IReadOnlyDictionary<string, int>> CountAsync(DateTime monthStartUtc, CancellationToken ct)
    {
        string[] all = ["Tenant", "Company", "SoftDelete"];
        var month = monthStartUtc;
        var trips = await db.Set<Trip>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(t => !t.IsDeleted && t.CreatedAt >= month, ct).ConfigureAwait(false);
        var pending = await db.Set<Domain.Acc.PendingPosting>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(p => p.ResolvedAt == null && !p.IsDeleted, ct).ConfigureAwait(false);
        var einv = await db.Set<EInvoiceSubmission>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(s => !s.IsDeleted && (s.Status == "Queued" || s.Status == "Rejected"), ct)
            .ConfigureAwait(false)
            + await db.Set<Domain.Acc.ApArEInvoiceSubmission>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(s => !s.IsDeleted && (s.Status == "Queued" || s.Status == "Rejected"), ct)
                .ConfigureAwait(false);
        return new Dictionary<string, int>(StringComparer.Ordinal) { [HealthMeasures.Trips] = trips, [HealthMeasures.PendingPostings] = pending, [HealthMeasures.EInvoices] = einv };
    }
}
