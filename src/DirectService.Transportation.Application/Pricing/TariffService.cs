using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>
/// Customer and partner tariffs (FR-019 … FR-026, PRD 7.2.1): lines keyed by party × material × loading site × unloading site,
/// each key a specific record or All; price per ton &gt; 0 (D-03) with the company currency's decimals; optional validity dates.
/// Add, edit, archive, restore; every change is written to trn.TRN_RateHistory (FR-026). Lines are unique per keys and start date
/// within the company, archived lines included (design unique index). Changes never alter saved trips (trips store their prices).
/// </summary>
public sealed class TariffService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks,
    RateHistoryLog history)
{
    public const string EntityType = "TRN.Tariff";

    /// <summary>A tariff line with the names of its keys (null = All).</summary>
    internal sealed record Row(
        Tariff T, Guid? CustomerPid, string? CustomerName, Guid? PartnerPid, string? PartnerName, Guid? MaterialPid, string? MaterialName,
        Guid? LoadingPid, string? LoadingName, Guid? UnloadingPid, string? UnloadingName)
    {
        public string? PartyName => CustomerName ?? PartnerName;
    }

    private static readonly IReadOnlyDictionary<string, Expression<Func<Tariff, object?>>> SortFields = new Dictionary<string, Expression<Func<Tariff, object?>>>(StringComparer.Ordinal)
    {
        ["partyType"] = t => t.PartyType,
        ["party"] = t => t.Customer != null ? t.Customer.Name : t.Partner != null ? t.Partner.Name : null,
        ["material"] = t => t.Material != null ? t.Material.Name : null,
        ["loadingSite"] = t => t.LoadingSite != null ? t.LoadingSite.Name : null,
        ["unloadingSite"] = t => t.UnloadingSite != null ? t.UnloadingSite.Name : null,
        ["price"] = t => t.Price,
        ["specificity"] = t => t.Specificity,
        ["validFrom"] = t => t.ValidFrom,
        ["validTo"] = t => t.ValidTo,
        ["status"] = t => t.Status,
        ["createdAt"] = t => t.CreatedAt,
    };

    internal static IQueryable<Row> Rows(IQueryable<Tariff> q) => q.Select(t => new Row(t,
        t.Customer != null ? t.Customer.PublicId : null, t.Customer != null ? t.Customer.Name : null,
        t.Partner != null ? t.Partner.PublicId : null, t.Partner != null ? t.Partner.Name : null,
        t.Material != null ? t.Material.PublicId : null, t.Material != null ? t.Material.Name : null,
        t.LoadingSite != null ? t.LoadingSite.PublicId : null, t.LoadingSite != null ? t.LoadingSite.Name : null,
        t.UnloadingSite != null ? t.UnloadingSite.PublicId : null, t.UnloadingSite != null ? t.UnloadingSite.Name : null));

    // ------------------------------------------------------------ list (FR-019, FR-165)

    public async Task<PagedResult<TariffResponse>> ListAsync(TariffListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var sorted = MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "-specificity").ThenBy(t => t.Id);
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await sorted.CountAsync(ct).ConfigureAwait(false);
        var rows = await Rows(sorted.Skip(paging.Skip).Take(paging.SafePageSize)).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<TariffResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>Every line of the filtered list in list order (export).</summary>
    internal async Task<IReadOnlyList<Row>> AllAsync(TariffListQuery q, CancellationToken ct) =>
        await Rows(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "-specificity").ThenBy(t => t.Id)).ToListAsync(ct).ConfigureAwait(false);

    private IQueryable<Tariff> Filter(TariffListQuery q)
    {
        var query = MasterDataSupport.WithStatus(db.Set<Tariff>().AsNoTracking(), q.Status);
        var partyType = PartyTypeOrNull(q.PartyType, "partyType");
        if (partyType is not null) query = query.Where(t => t.PartyType == partyType);
        if (IsAll(q.Party)) query = query.Where(t => t.CustomerId == null && t.PartnerId == null);
        else if (!string.IsNullOrWhiteSpace(q.Party))
        {
            if (support.Decode<Customer>(q.Party) is Guid customer) query = query.Where(t => t.Customer != null && t.Customer.PublicId == customer);
            else if (support.Decode<Partner>(q.Party) is Guid partner) query = query.Where(t => t.Partner != null && t.Partner.PublicId == partner);
            else throw Fail.Field("party", ErrorCodes.ValidationFormat, "party");
        }
        if (IsAll(q.Material)) query = query.Where(t => t.MaterialId == null);
        else if (!string.IsNullOrWhiteSpace(q.Material))
        {
            var g = support.Decode<Material>(q.Material) ?? throw Fail.Field("material", ErrorCodes.ValidationFormat, "material");
            query = query.Where(t => t.Material != null && t.Material.PublicId == g);
        }
        if (IsAll(q.LoadingSite)) query = query.Where(t => t.LoadingSiteId == null);
        else if (!string.IsNullOrWhiteSpace(q.LoadingSite))
        {
            var g = support.Decode<LoadingSite>(q.LoadingSite) ?? throw Fail.Field("loadingSite", ErrorCodes.ValidationFormat, "loadingSite");
            query = query.Where(t => t.LoadingSite != null && t.LoadingSite.PublicId == g);
        }
        if (IsAll(q.UnloadingSite)) query = query.Where(t => t.UnloadingSiteId == null);
        else if (!string.IsNullOrWhiteSpace(q.UnloadingSite))
        {
            var g = support.Decode<UnloadingSite>(q.UnloadingSite) ?? throw Fail.Field("unloadingSite", ErrorCodes.ValidationFormat, "unloadingSite");
            query = query.Where(t => t.UnloadingSite != null && t.UnloadingSite.PublicId == g);
        }
        if (q.ValidOn is DateOnly on) query = query.Where(t => (t.ValidFrom == null || t.ValidFrom <= on) && (t.ValidTo == null || t.ValidTo >= on));
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            query = query.Where(t => (t.Customer != null && t.Customer.Name.Contains(text)) || (t.Partner != null && t.Partner.Name.Contains(text))
                || (t.Material != null && t.Material.Name.Contains(text)) || (t.LoadingSite != null && t.LoadingSite.Name.Contains(text))
                || (t.UnloadingSite != null && t.UnloadingSite.Name.Contains(text)) || (t.Notes != null && t.Notes.Contains(text)));
        }
        return query;
    }

    /// <summary>"all" in a key filter selects the lines whose key is All.</summary>
    private static bool IsAll(string? token) => string.Equals(token?.Trim(), "all", StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------ details and history (FR-026)

    public async Task<TariffDetails> GetAsync(string token, CancellationToken ct)
    {
        var row = await FindRowAsync(token, tracking: false, ct).ConfigureAwait(false);
        return new TariffDetails(ToResponse(row), await history.ReadAsync(RateHistoryLog.Tariff, [row.T.Id], ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<RateHistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var row = await FindRowAsync(token, tracking: false, ct).ConfigureAwait(false);
        return await history.ReadAsync(RateHistoryLog.Tariff, [row.T.Id], ct).ConfigureAwait(false);
    }

    private async Task<Row> FindRowAsync(string? token, bool tracking, CancellationToken ct)
    {
        var publicId = support.Decode<Tariff>(token) ?? throw Fail.NotFound();
        var set = tracking ? db.Set<Tariff>().AsTracking() : db.Set<Tariff>().AsNoTracking();
        return await Rows(set.Where(t => t.PublicId == publicId)).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    private async Task<TariffResponse> ResponseAsync(long id, CancellationToken ct) =>
        ToResponse(await Rows(db.Set<Tariff>().AsNoTracking().Where(t => t.Id == id)).SingleAsync(ct).ConfigureAwait(false));

    // ------------------------------------------------------------ add, edit, archive, restore (FR-019)

    public async Task<(TariffResponse Value, bool Created)> CreateAsync(TariffRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Tariff>(clientId, ct).ConfigureAwait(false) is { } again) return (await ResponseAsync(again.Id, ct).ConfigureAwait(false), false);
        var t = new Tariff();
        if (clientId is Guid id) t.PublicId = id;
        var after = await ApplyAsync(t, r, null, ct).ConfigureAwait(false);
        Tariff? raced;
        try
        {
            raced = await db.InTransactionAsync(async c =>
            {
                db.Set<Tariff>().Add(t);
                if (await support.SaveCreateAsync<Tariff>(clientId, c).ConfigureAwait(false) is { } other) return other;
                history.Add(RateHistoryLog.Tariff, t.Id, "Create", null, after);
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                return null;
            }, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsKeyViolation(ex))
        {
            throw Fail.Conflict(ErrorCodes.TariffDuplicate);
        }
        return raced is not null ? (await ResponseAsync(raced.Id, ct).ConfigureAwait(false), false) : (await ResponseAsync(t.Id, ct).ConfigureAwait(false), true);
    }

    public async Task<TariffResponse> UpdateAsync(string token, TariffRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var row = await FindRowAsync(token, tracking: true, ct).ConfigureAwait(false);
        var t = row.T;
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, r.RowVersion);
        var before = Snapshot(row);
        var keysBefore = (t.CustomerId, t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId);
        var after = await ApplyAsync(t, r, row, ct).ConfigureAwait(false);
        var changed = !SameValues(before, after) || keysBefore != (t.CustomerId, t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId);
        try
        {
            await db.InTransactionAsync(async c =>
            {
                // Saved even without changes when a row version was sent, so a stale version is still refused (review L6).
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                if (!changed) return;
                history.Add(RateHistoryLog.Tariff, t.Id, "Update", before, after);
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsKeyViolation(ex))
        {
            throw Fail.Conflict(ErrorCodes.TariffDuplicate);
        }
        return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
    }

    public async Task<TariffResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var row = await FindRowAsync(token, tracking: true, ct).ConfigureAwait(false);
        var t = row.T;
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        var status = active ? MasterCodes.Active : MasterCodes.Archived;
        if (t.Status == status) return ToResponse(row);
        db.ExpectRowVersion(t, rowVersion);
        var before = Snapshot(row);
        t.Status = status;
        var action = active ? "Restore" : "Archive";
        changes.SetAction(t, action);
        await db.InTransactionAsync(async c =>
        {
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            history.Add(RateHistoryLog.Tariff, t.Id, action, before, new Dictionary<string, object?>(before) { ["Status"] = status });
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
    }

    /// <summary>The resolved keys of a line (null = All) with the names shown in the history.</summary>
    internal sealed record ResolvedKeys(
        long? CustomerId, long? PartnerId, long? MaterialId, long? LoadingSiteId, long? UnloadingSiteId,
        string? PartyName, string? MaterialName, string? LoadingName, string? UnloadingName);

    /// <summary>
    /// Validates a request and applies it to the line. Returns the snapshot of the new values for the rate history.
    /// <paramref name="current"/> is the stored line on edit: its archived references stay acceptable.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, object?>> ApplyAsync(Tariff t, TariffRequest r, Row? current, CancellationToken ct)
    {
        var (partyType, notes) = Validate(r, await DecimalsAsync(ct).ConfigureAwait(false));
        long? customerId = null, partnerId = null, materialId = null, loadingId = null, unloadingId = null;
        string? partyName = null, materialName = null, loadingName = null, unloadingName = null;
        if (!string.IsNullOrWhiteSpace(r.PartyId))
        {
            if (partyType == PartyTypes.Customer)
            {
                var c = await support.ChooseAsync<Customer>("partyId", r.PartyId, current?.T.CustomerId, x => x.Name, ct).ConfigureAwait(false);
                (customerId, partyName) = (c.Id, c.Name);
            }
            else
            {
                var p = await support.ChooseAsync<Partner>("partyId", r.PartyId, current?.T.PartnerId, x => x.Name, ct).ConfigureAwait(false);
                (partnerId, partyName) = (p.Id, p.Name);
            }
        }
        if (!string.IsNullOrWhiteSpace(r.MaterialId))
        {
            var m = await support.ChooseAsync<Material>("materialId", r.MaterialId, current?.T.MaterialId, x => x.Name, ct).ConfigureAwait(false);
            (materialId, materialName) = (m.Id, m.Name);
        }
        if (!string.IsNullOrWhiteSpace(r.LoadingSiteId))
        {
            var s = await support.ChooseAsync<LoadingSite>("loadingSiteId", r.LoadingSiteId, current?.T.LoadingSiteId, x => x.Name, ct).ConfigureAwait(false);
            (loadingId, loadingName) = (s.Id, s.Name);
        }
        if (!string.IsNullOrWhiteSpace(r.UnloadingSiteId))
        {
            var s = await support.ChooseAsync<UnloadingSite>("unloadingSiteId", r.UnloadingSiteId, current?.T.UnloadingSiteId, x => x.Name, ct).ConfigureAwait(false);
            (unloadingId, unloadingName) = (s.Id, s.Name);
        }
        var k = new ResolvedKeys(customerId, partnerId, materialId, loadingId, unloadingId, partyName, materialName, loadingName, unloadingName);

        // Unique per keys and start date within the company, archived lines included (design UX_TRN_Tariffs_…_ValidFrom).
        var validFrom = r.ValidFrom;
        var id = t.Id;
        if (await db.Set<Tariff>().AnyAsync(x => x.Id != id && x.PartyType == partyType && x.CustomerId == customerId && x.PartnerId == partnerId
                && x.MaterialId == materialId && x.LoadingSiteId == loadingId && x.UnloadingSiteId == unloadingId && x.ValidFrom == validFrom, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.TariffDuplicate);
        return Set(t, partyType, k, r, notes);
    }

    /// <summary>Field rules of a line (form and import): party type, price &gt; 0 with the currency's decimals, Valid to ≥ Valid from, notes.</summary>
    internal static (string PartyType, string? Notes) Validate(TariffRequest r, int decimals)
    {
        ArgumentNullException.ThrowIfNull(r);
        var partyType = PartyTypeOrNull(r.PartyType, "partyType");
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator()
            .Check(partyType is not null, "partyType", string.IsNullOrWhiteSpace(r.PartyType) ? ErrorCodes.ValidationRequired : ErrorCodes.ValidationFormat)
            .Check(r.Price is not null, "price", ErrorCodes.ValidationRequired)
            .Check(r.Price is null || r.Price > 0 && Money.IsValidAmount(r.Price.Value, decimals), "price", ErrorCodes.ValidationRange)
            .Check(r.ValidFrom is null || r.ValidTo is null || r.ValidTo >= r.ValidFrom, "validTo", ErrorCodes.ValidationRange)
            .MaxLength("notes", notes, 500)
            .ThrowIfInvalid();
        return (partyType!, notes);
    }

    /// <summary>Writes validated values and resolved keys to the line; returns the history snapshot.</summary>
    internal static IReadOnlyDictionary<string, object?> Set(Tariff t, string partyType, ResolvedKeys k, TariffRequest r, string? notes)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(r);
        t.PartyType = partyType;
        t.CustomerId = k.CustomerId;
        t.PartnerId = k.PartnerId;
        t.MaterialId = k.MaterialId;
        t.LoadingSiteId = k.LoadingSiteId;
        t.UnloadingSiteId = k.UnloadingSiteId;
        t.Price = r.Price!.Value;
        t.ValidFrom = r.ValidFrom;
        t.ValidTo = r.ValidTo;
        t.Notes = notes;
        return Snapshot(partyType, k.PartyName, k.MaterialName, k.LoadingName, k.UnloadingName, t.Price, t.ValidFrom, t.ValidTo, notes, t.Status);
    }

    /// <summary>The unique key of a line (design unique index: party type, the five keys, Valid from).</summary>
    internal static string UniqueKey(string partyType, long? customerId, long? partnerId, long? materialId, long? loadingId, long? unloadingId, DateOnly? validFrom) =>
        string.Join('|', partyType, customerId, partnerId, materialId, loadingId, unloadingId, validFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The unique keys of every line of the company (import duplicate check in one query).</summary>
    internal async Task<HashSet<string>> ExistingKeysAsync(CancellationToken ct) =>
        (await db.Set<Tariff>().AsNoTracking()
            .Select(x => new { x.PartyType, x.CustomerId, x.PartnerId, x.MaterialId, x.LoadingSiteId, x.UnloadingSiteId, x.ValidFrom })
            .ToListAsync(ct).ConfigureAwait(false))
        .Select(x => UniqueKey(x.PartyType, x.CustomerId, x.PartnerId, x.MaterialId, x.LoadingSiteId, x.UnloadingSiteId, x.ValidFrom))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>A save refused by the design's unique index on the line keys (two saves racing past the duplicate check, review L7).</summary>
    private static bool IsKeyViolation(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("UX_TRN_Tariffs_CompanyId_PartyType", StringComparison.Ordinal) == true;

    /// <summary>The keys of a line as the resolver uses them (party = customer or partner by party type).</summary>
    public static TariffKeys Keys(Tariff t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return new(t.CustomerId ?? t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId);
    }

    private int? _decimals;

    /// <summary>Decimals of the company currency (prices and rates are entered with at most this many, DT-61).</summary>
    internal async Task<int> DecimalsAsync(CancellationToken ct)
    {
        _decimals ??= await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => (int)c.CurrencyDecimals)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) is var d and > 0 ? d : 3;
        return _decimals.Value;
    }

    internal static string? PartyTypeOrNull(string? text, string field) => TextNormalizer.Key(text) switch
    {
        "" => null,
        "CUSTOMER" or "عميل" or "العميل" or "عملاء" => PartyTypes.Customer,
        "PARTNER" or "شريك" or "الشريك" or "شركاء" => PartyTypes.Partner,
        _ => throw Fail.Field(field, ErrorCodes.ValidationFormat, field),
    };

    // ------------------------------------------------------------ snapshots and responses

    private static Dictionary<string, object?> Snapshot(Row row) =>
        Snapshot(row.T.PartyType, row.PartyName, row.MaterialName, row.LoadingName, row.UnloadingName, row.T.Price, row.T.ValidFrom, row.T.ValidTo, row.T.Notes, row.T.Status);

    private static Dictionary<string, object?> Snapshot(string partyType, string? party, string? material, string? loading, string? unloading, decimal price,
        DateOnly? validFrom, DateOnly? validTo, string? notes, string status) => new(StringComparer.Ordinal)
    {
        ["PartyType"] = partyType,
        ["Party"] = party,
        ["Material"] = material,
        ["LoadingSite"] = loading,
        ["UnloadingSite"] = unloading,
        ["Price"] = RateHistoryLog.Value(price),
        ["ValidFrom"] = RateHistoryLog.Value(validFrom),
        ["ValidTo"] = RateHistoryLog.Value(validTo),
        ["Notes"] = notes,
        ["Status"] = status,
    };

    private static bool SameValues(Dictionary<string, object?> a, IReadOnlyDictionary<string, object?> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && Equals(kv.Value, v));

    internal TariffResponse ToResponse(Row row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var t = row.T;
        Ref? party = row.CustomerPid is Guid c ? new Ref(support.Token<Customer>(c), row.CustomerName!)
            : row.PartnerPid is Guid p ? new Ref(support.Token<Partner>(p), row.PartnerName!) : null;
        return new TariffResponse(support.Token<Tariff>(t.PublicId), t.PartyType, party,
            row.MaterialPid is Guid m ? new Ref(support.Token<Material>(m), row.MaterialName!) : null,
            row.LoadingPid is Guid l ? new Ref(support.Token<LoadingSite>(l), row.LoadingName!) : null,
            row.UnloadingPid is Guid u ? new Ref(support.Token<UnloadingSite>(u), row.UnloadingName!) : null,
            t.Price, t.ValidFrom, t.ValidTo, PricingRules.Specificity(Keys(t)), t.Notes, t.Status, Convert.ToBase64String(t.RowVersion));
    }
}
