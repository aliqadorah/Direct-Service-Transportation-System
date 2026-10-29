using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// Partners — sub-contracted haulage companies (FR-158, PRD 7.8.3): name unique among active partners, optional tax, contact and
/// payment details (IBAN validated). The profile lists the partner's drivers; archive/restore; delete only while no trip, driver,
/// partner payment or tariff refers to it (BR-040).
/// </summary>
public sealed class PartnerService(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks)
{
    public const string EntityType = "TRN.Partner";

    private static readonly IReadOnlyDictionary<string, Expression<Func<Partner, object?>>> SortFields = new Dictionary<string, Expression<Func<Partner, object?>>>(StringComparer.Ordinal)
    {
        ["name"] = p => p.Name,
        ["taxNumber"] = p => p.TaxNumber,
        ["status"] = p => p.Status,
        ["createdAt"] = p => p.CreatedAt,
    };

    public async Task<PagedResult<PartnerResponse>> ListAsync(MasterListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        return await MasterDataSupport.PageAsync(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name"), q.Page, q.PageSize, ToResponse, ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<Partner>> AllAsync(MasterListQuery q, CancellationToken ct) =>
        await MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name").ToListAsync(ct).ConfigureAwait(false);

    private IQueryable<Partner> Filter(MasterListQuery q)
    {
        var query = MasterDataSupport.WithStatus(db.Set<Partner>().AsNoTracking(), q.Status);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            var compact = MasterDataSupport.Compact(text) ?? text;
            query = query.Where(p => p.Name.Contains(text) || (p.TaxNumber != null && p.TaxNumber.Contains(compact))
                || (p.Phone != null && p.Phone.Contains(compact)) || (p.ContactPerson != null && p.ContactPerson.Contains(text)));
        }
        return query;
    }

    public async Task<IReadOnlyList<MasterOption>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<Partner>().AsNoTracking().Where(p => p.Status == MasterCodes.Active).OrderBy(p => p.Name)
            .Select(p => new { p.PublicId, p.Name }).ToListAsync(ct).ConfigureAwait(false))
        .Select(p => new MasterOption(support.Token<Partner>(p.PublicId), p.Name)).ToList();

    public async Task<PartnerProfile> ProfileAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        var drivers = (await db.Set<Driver>().AsNoTracking().Where(d => d.PartnerId == p.Id).OrderBy(d => d.FullName)
                .Select(d => new { d.PublicId, d.FullName }).ToListAsync(ct).ConfigureAwait(false))
            .Select(d => new MasterOption(support.Token<Driver>(d.PublicId), d.FullName)).ToList();
        return new PartnerProfile(ToResponse(p), drivers, await ReferencesAsync(p.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, p.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(p.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, p.Id, ct).ConfigureAwait(false);
    }

    public async Task<(PartnerResponse Value, bool Created)> CreateAsync(PartnerRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Partner>(clientId, ct).ConfigureAwait(false) is { } again) return (ToResponse(again), false);
        var p = new Partner();
        if (clientId is Guid id) p.PublicId = id;
        await ApplyAsync(p, r, ct).ConfigureAwait(false);
        db.Set<Partner>().Add(p);
        if (await support.SaveCreateAsync<Partner>(clientId, ct).ConfigureAwait(false) is { } raced) return (ToResponse(raced), false);
        return (ToResponse(p), true);
    }

    public async Task<PartnerResponse> UpdateAsync(string token, PartnerRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(p, r.RowVersion);
        await ApplyAsync(p, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(p);
    }

    public async Task<PartnerResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        if (active && p.Status != MasterCodes.Active) await EnsureUniqueNameAsync(p.Name, p.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(p, rowVersion);
        p.Status = active ? MasterCodes.Active : MasterCodes.Archived;
        changes.SetAction(p, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(p);
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var p = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(p.Id, ct).ConfigureAwait(false));
        p.DeleteReason = why;
        db.Set<Partner>().Remove(p);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) => support.References(
        ("Trips", await db.Set<Trip>().CountAsync(t => t.PartnerId == id, ct).ConfigureAwait(false)),
        ("Drivers", await db.Set<Driver>().CountAsync(d => d.PartnerId == id, ct).ConfigureAwait(false)),
        ("PartnerPayments", await db.Set<PartnerPayment>().CountAsync(x => x.PartnerId == id, ct).ConfigureAwait(false)),
        ("Tariffs", await db.Set<Tariff>().CountAsync(x => x.PartnerId == id, ct).ConfigureAwait(false)));

    internal async Task ApplyAsync(Partner p, PartnerRequest r, CancellationToken ct)
    {
        var name = MasterDataSupport.Name(r.Name);
        var tax = MasterDataSupport.Compact(r.TaxNumber);
        var phone = MasterDataSupport.Compact(r.Phone);
        var address = TextNormalizer.CleanText(r.Address);
        var contact = MasterDataSupport.Name(r.ContactPerson);
        var bankName = TextNormalizer.CleanText(r.BankName);
        var iban = MasterDataSupport.Compact(r.Iban)?.ToUpperInvariant();
        var cliq = TextNormalizer.CleanText(r.CliqAlias);
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator()
            .Require("name", name).MaxLength("name", name, 200)
            .MaxLength("taxNumber", tax, 30)
            .Check(phone is null || await support.IsPhoneAsync(phone, ct).ConfigureAwait(false), "phone")
            .MaxLength("address", address, 300).MaxLength("contactPerson", contact, 150).MaxLength("bankName", bankName, 100)
            .Check(iban is null || iban.Length <= 34 && await support.IsIbanAsync(iban, ct).ConfigureAwait(false), "iban")
            .MaxLength("cliqAlias", cliq, 50).MaxLength("notes", notes, 1000)
            .ThrowIfInvalid();
        if (p.Status == MasterCodes.Active) await EnsureUniqueNameAsync(name!, p.Id, ct).ConfigureAwait(false);
        p.Name = name!;
        p.TaxNumber = tax;
        p.Phone = phone;
        p.Address = address;
        p.ContactPerson = contact;
        p.BankName = bankName;
        p.Iban = iban;
        p.CliqAlias = cliq;
        p.Notes = notes;
    }

    private async Task EnsureUniqueNameAsync(string name, long id, CancellationToken ct)
    {
        if (await db.Set<Partner>().AnyAsync(x => x.Status == MasterCodes.Active && x.Name == name && x.Id != id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name);
    }

    internal PartnerResponse ToResponse(Partner p) =>
        new(support.Token<Partner>(p.PublicId), p.Name, p.TaxNumber, p.Phone, p.Address, p.ContactPerson, p.BankName, p.Iban, p.CliqAlias, p.Notes,
            p.Status, Convert.ToBase64String(p.RowVersion));
}
