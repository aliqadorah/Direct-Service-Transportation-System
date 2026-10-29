using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// Materials (FR-159): name unique within the company (archived included), unit (default ton), active/archived. Renaming changes
/// the name everywhere by identifier; issued documents keep the printed name (FR-163). Delete only while unreferenced (BR-040).
/// </summary>
public sealed class MaterialService(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks)
{
    public const string EntityType = "TRN.Material";

    private static readonly IReadOnlyDictionary<string, Expression<Func<Material, object?>>> SortFields = new Dictionary<string, Expression<Func<Material, object?>>>(StringComparer.Ordinal)
    {
        ["name"] = m => m.Name,
        ["unit"] = m => m.Unit,
        ["status"] = m => m.Status,
        ["createdAt"] = m => m.CreatedAt,
    };

    public async Task<PagedResult<MaterialResponse>> ListAsync(MasterListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        return await MasterDataSupport.PageAsync(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name"), q.Page, q.PageSize, ToResponse, ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<Material>> AllAsync(MasterListQuery q, CancellationToken ct) =>
        await MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name").ToListAsync(ct).ConfigureAwait(false);

    private IQueryable<Material> Filter(MasterListQuery q)
    {
        var query = MasterDataSupport.WithStatus(db.Set<Material>().AsNoTracking(), q.Status);
        return MasterDataSupport.SearchText(q.Q) is { } text ? query.Where(m => m.Name.Contains(text) || m.Unit.Contains(text)) : query;
    }

    public async Task<IReadOnlyList<MasterOption>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<Material>().AsNoTracking().Where(m => m.Status == MasterCodes.Active).OrderBy(m => m.Name)
            .Select(m => new { m.PublicId, m.Name }).ToListAsync(ct).ConfigureAwait(false))
        .Select(m => new MasterOption(support.Token<Material>(m.PublicId), m.Name)).ToList();

    public async Task<RecordDetails<MaterialResponse>> GetAsync(string token, CancellationToken ct)
    {
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        return new RecordDetails<MaterialResponse>(ToResponse(m), await ReferencesAsync(m.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, m.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(m.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, m.Id, ct).ConfigureAwait(false);
    }

    public async Task<(MaterialResponse Value, bool Created)> CreateAsync(MaterialRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Material>(clientId, ct).ConfigureAwait(false) is { } again) return (ToResponse(again), false);
        var m = new Material();
        if (clientId is Guid id) m.PublicId = id;
        await ApplyAsync(m, r, ct).ConfigureAwait(false);
        db.Set<Material>().Add(m);
        if (await support.SaveCreateAsync<Material>(clientId, ct).ConfigureAwait(false) is { } raced) return (ToResponse(raced), false);
        return (ToResponse(m), true);
    }

    public async Task<MaterialResponse> UpdateAsync(string token, MaterialRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, m.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(m, r.RowVersion);
        await ApplyAsync(m, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(m);
    }

    public async Task<MaterialResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, m.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(m, rowVersion);
        m.Status = active ? MasterCodes.Active : MasterCodes.Archived;
        changes.SetAction(m, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(m);
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var m = await support.FindAsync<Material>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, m.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(m.Id, ct).ConfigureAwait(false));
        m.DeleteReason = why;
        db.Set<Material>().Remove(m);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) => support.References(
        ("Trips", await db.Set<Trip>().CountAsync(t => t.MaterialId == id, ct).ConfigureAwait(false)),
        ("Tariffs", await db.Set<Tariff>().CountAsync(t => t.MaterialId == id, ct).ConfigureAwait(false)));

    internal async Task ApplyAsync(Material m, MaterialRequest r, CancellationToken ct)
    {
        var name = MasterDataSupport.Name(r.Name);
        var unit = MasterDataSupport.Name(r.Unit) ?? "ton";
        new Validator().Require("name", name).MaxLength("name", name, 100).MaxLength("unit", unit, 20).ThrowIfInvalid();
        if (await db.Set<Material>().AnyAsync(x => x.Name == name && x.Id != m.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        m.Name = name!;
        m.Unit = unit;
    }

    internal MaterialResponse ToResponse(Material m) =>
        new(support.Token<Material>(m.PublicId), m.Name, m.Unit, m.Status, Convert.ToBase64String(m.RowVersion));
}

/// <summary>
/// Loading sites and unloading sites — two separate lists with the same rules (FR-160): name unique in its list (archived
/// included), area/city, active/archived; delete only while unreferenced (BR-040).
/// </summary>
public abstract class SiteService<TSite>(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks)
    where TSite : AuditableEntity, ISite, new()
{
    public abstract string EntityType { get; }

    /// <summary>What refers to a site of this list (trips, tariffs and, for loading sites, monthly rates).</summary>
    protected abstract Task<IReadOnlyList<(string Type, int Count)>> CountReferencesAsync(long id, CancellationToken ct);

    private static readonly IReadOnlyDictionary<string, Expression<Func<TSite, object?>>> SortFields = new Dictionary<string, Expression<Func<TSite, object?>>>(StringComparer.Ordinal)
    {
        ["name"] = s => s.Name,
        ["area"] = s => s.Area,
        ["status"] = s => s.Status,
        ["createdAt"] = s => s.CreatedAt,
    };

    public async Task<PagedResult<SiteResponse>> ListAsync(MasterListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        return await MasterDataSupport.PageAsync(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name"), q.Page, q.PageSize, ToResponse, ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<TSite>> AllAsync(MasterListQuery q, CancellationToken ct) =>
        await MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name").ToListAsync(ct).ConfigureAwait(false);

    private IQueryable<TSite> Filter(MasterListQuery q)
    {
        var query = MasterDataSupport.WithStatus(db.Set<TSite>().AsNoTracking(), q.Status);
        return MasterDataSupport.SearchText(q.Q) is { } text ? query.Where(s => s.Name.Contains(text) || (s.Area != null && s.Area.Contains(text))) : query;
    }

    public async Task<IReadOnlyList<MasterOption>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<TSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active).OrderBy(s => s.Name)
            .Select(s => new { s.PublicId, s.Name }).ToListAsync(ct).ConfigureAwait(false))
        .Select(s => new MasterOption(support.Token<TSite>(s.PublicId), s.Name)).ToList();

    public async Task<RecordDetails<SiteResponse>> GetAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        return new RecordDetails<SiteResponse>(ToResponse(s), await ReferencesAsync(s.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, s.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(s.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, s.Id, ct).ConfigureAwait(false);
    }

    public async Task<(SiteResponse Value, bool Created)> CreateAsync(SiteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<TSite>(clientId, ct).ConfigureAwait(false) is { } again) return (ToResponse(again), false);
        var s = new TSite();
        if (clientId is Guid id) s.PublicId = id;
        await ApplyAsync(s, r, ct).ConfigureAwait(false);
        db.Set<TSite>().Add(s);
        if (await support.SaveCreateAsync<TSite>(clientId, ct).ConfigureAwait(false) is { } raced) return (ToResponse(raced), false);
        return (ToResponse(s), true);
    }

    public async Task<SiteResponse> UpdateAsync(string token, SiteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, s.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(s, r.RowVersion);
        await ApplyAsync(s, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(s);
    }

    public async Task<SiteResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, s.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(s, rowVersion);
        s.Status = active ? MasterCodes.Active : MasterCodes.Archived;
        changes.SetAction(s, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(s);
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var s = await support.FindAsync<TSite>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, s.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(s.Id, ct).ConfigureAwait(false));
        s.DeleteReason = why;
        db.Set<TSite>().Remove(s);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) =>
        support.References([.. await CountReferencesAsync(id, ct).ConfigureAwait(false)]);

    internal async Task ApplyAsync(TSite s, SiteRequest r, CancellationToken ct)
    {
        var name = MasterDataSupport.Name(r.Name);
        var area = MasterDataSupport.Name(r.Area);
        new Validator().Require("name", name).MaxLength("name", name, 100).MaxLength("area", area, 100).ThrowIfInvalid();
        if (await db.Set<TSite>().AnyAsync(x => x.Name == name && x.Id != s.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        s.Name = name!;
        s.Area = area;
    }

    internal SiteResponse ToResponse(TSite s) =>
        new(support.Token<TSite>(s.PublicId), s.Name, s.Area, s.Status, Convert.ToBase64String(s.RowVersion));
}

public sealed class LoadingSiteService(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks)
    : SiteService<LoadingSite>(db, support, changes, locks)
{
    private readonly IAppDbContext _db = db;

    public override string EntityType => "TRN.LoadingSite";

    protected override async Task<IReadOnlyList<(string Type, int Count)>> CountReferencesAsync(long id, CancellationToken ct) =>
    [
        ("Trips", await _db.Set<Trip>().CountAsync(t => t.LoadingSiteId == id, ct).ConfigureAwait(false)),
        ("Tariffs", await _db.Set<Tariff>().CountAsync(t => t.LoadingSiteId == id, ct).ConfigureAwait(false)),
        ("MonthlySiteRates", await _db.Set<MonthlySiteRate>().CountAsync(t => t.LoadingSiteId == id, ct).ConfigureAwait(false)),
    ];
}

public sealed class UnloadingSiteService(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks)
    : SiteService<UnloadingSite>(db, support, changes, locks)
{
    private readonly IAppDbContext _db = db;

    public override string EntityType => "TRN.UnloadingSite";

    protected override async Task<IReadOnlyList<(string Type, int Count)>> CountReferencesAsync(long id, CancellationToken ct) =>
    [
        ("Trips", await _db.Set<Trip>().CountAsync(t => t.UnloadingSiteId == id, ct).ConfigureAwait(false)),
        ("Tariffs", await _db.Set<Tariff>().CountAsync(t => t.UnloadingSiteId == id, ct).ConfigureAwait(false)),
    ];
}

/// <summary>
/// Ownership types (FR-161): each classified own fleet or external fleet (BR-029). The built-in "Company" type (own fleet) is
/// created with the company and cannot be renamed, reclassified or deleted (design rule + FR-161, DT-38). The table has no
/// status, so types are not archived; a type is deleted (soft) only while no driver uses it (BR-040).
/// </summary>
public sealed class OwnershipTypeService(IAppDbContext db, MasterDataSupport support, IRecordLockGuard locks, IRequestContext ctx)
{
    public const string EntityType = "TRN.OwnershipType";

    public async Task<IReadOnlyList<OwnershipTypeResponse>> ListAsync(CancellationToken ct) =>
        (await db.Set<OwnershipType>().AsNoTracking().OrderByDescending(t => t.IsBuiltIn).ThenBy(t => t.Name).ToListAsync(ct).ConfigureAwait(false))
        .Select(ToResponse).ToList();

    public async Task<RecordDetails<OwnershipTypeResponse>> GetAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<OwnershipType>(token, ct).ConfigureAwait(false);
        return new RecordDetails<OwnershipTypeResponse>(ToResponse(t), await ReferencesAsync(t.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<OwnershipType>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(t.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0 && !t.IsBuiltIn, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<OwnershipType>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false);
    }

    public async Task<(OwnershipTypeResponse Value, bool Created)> CreateAsync(OwnershipTypeRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<OwnershipType>(clientId, ct).ConfigureAwait(false) is { } again) return (ToResponse(again), false);
        var t = new OwnershipType();
        if (clientId is Guid id) t.PublicId = id;
        await ApplyAsync(t, r, ct).ConfigureAwait(false);
        db.Set<OwnershipType>().Add(t);
        if (await support.SaveCreateAsync<OwnershipType>(clientId, ct).ConfigureAwait(false) is { } raced) return (ToResponse(raced), false);
        return (ToResponse(t), true);
    }

    public async Task<OwnershipTypeResponse> UpdateAsync(string token, OwnershipTypeRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var t = await support.FindAsync<OwnershipType>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, r.RowVersion);
        await ApplyAsync(t, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(t);
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var t = await support.FindAsync<OwnershipType>(token, ct).ConfigureAwait(false);
        if (t.IsBuiltIn) throw Fail.Rule(ErrorCodes.BuiltInFixed, t.Name);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(t.Id, ct).ConfigureAwait(false));
        t.DeleteReason = why;
        db.Set<OwnershipType>().Remove(t);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) =>
        support.References(("Drivers", await db.Set<Driver>().CountAsync(d => d.OwnershipTypeId == id, ct).ConfigureAwait(false)));

    internal async Task ApplyAsync(OwnershipType t, OwnershipTypeRequest r, CancellationToken ct)
    {
        var name = MasterDataSupport.Name(r.Name);
        // The built-in type comes back with its shown name (either language): it stays the stored code.
        if (t.IsBuiltIn && OwnershipTypeNames.IsBuiltIn(name)) name = t.Name;
        var fleet = MasterCodes.FleetClasses.FirstOrDefault(f => string.Equals(f, r.FleetClass?.Trim(), StringComparison.OrdinalIgnoreCase));
        new Validator().Require("name", name).MaxLength("name", name, 100)
            .Require("fleetClass", r.FleetClass).Check(r.FleetClass is null || fleet is not null, "fleetClass")
            .ThrowIfInvalid();
        if (t.IsBuiltIn && (name != t.Name || fleet != t.FleetClass)) throw Fail.Rule(ErrorCodes.BuiltInFixed, t.Name);
        if (!t.IsBuiltIn && OwnershipTypeNames.IsBuiltIn(name))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        if (await db.Set<OwnershipType>().AnyAsync(x => x.Name == name && x.Id != t.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        if (t.Id != 0 && fleet == MasterCodes.Own && t.FleetClass != MasterCodes.Own)
        {
            // A partner's truck is never own fleet (BR-029, DT-48): refuse while drivers of this type are linked to partners (review M2).
            var linked = await db.Set<Driver>().CountAsync(d => d.OwnershipTypeId == t.Id && d.PartnerId != null, ct).ConfigureAwait(false);
            if (linked > 0) throw Fail.RuleOn("fleetClass", ErrorCodes.OwnershipFleetInUse, linked);
        }
        t.Name = name!;
        t.FleetClass = fleet!;
    }

    internal OwnershipTypeResponse ToResponse(OwnershipType t) =>
        new(support.Token<OwnershipType>(t.PublicId), OwnershipTypeNames.Display(t.Name, ctx.Language), t.FleetClass, t.IsBuiltIn, Convert.ToBase64String(t.RowVersion));
}
