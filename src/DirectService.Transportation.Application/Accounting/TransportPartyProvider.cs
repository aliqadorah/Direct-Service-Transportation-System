using DirectService.Accounting.Contracts.Parties;
using DirectService.Accounting.Contracts.Posting;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The transport parties on entry lines (<see cref="IPartyProvider"/>, design b; Stage 3 session 6c): customers, drivers and partners, found by
/// their URL tokens and described with their names (archived and deleted ones too, for the lines that carry them).
/// </summary>
public sealed class TransportPartyProvider(IAppDbContext db, IUrlTokenService tokens) : IPartyProvider
{
    private const string Archived = "Archived";

    public string Module => PartyCodes.ModuleTrn;

    public IReadOnlyList<string> Types { get; } = [PartyCodes.Customer, PartyCodes.Driver, PartyCodes.Partner];

    public async Task<PartyMatch?> FindAsync(string type, string? token, CancellationToken ct) => type switch
    {
        PartyCodes.Customer => await FindAsync<Customer>(token, c => c.Status, ct).ConfigureAwait(false),
        PartyCodes.Driver => await FindAsync<Driver>(token, d => d.Status, ct).ConfigureAwait(false),
        PartyCodes.Partner => await FindAsync<Partner>(token, p => p.Status, ct).ConfigureAwait(false),
        _ => null,
    };

    public async Task<IReadOnlyDictionary<long, PartyInfo>> DescribeAsync(string type, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        IEnumerable<(long Id, Guid PublicId, string Name, string Status, Type Entity)> rows = type switch
        {
            PartyCodes.Customer => (await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name, x.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.Id, x.PublicId, x.Name, x.Status, typeof(Customer))),
            PartyCodes.Driver => (await db.Set<Driver>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, Name = x.FullName, x.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.Id, x.PublicId, x.Name, x.Status, typeof(Driver))),
            PartyCodes.Partner => (await db.Set<Partner>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name, x.Status }).ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.Id, x.PublicId, x.Name, x.Status, typeof(Partner))),
            _ => [],
        };
        return rows.ToDictionary(r => r.Id, r => new PartyInfo(r.Name, tokens.Encode(r.Entity, r.PublicId), r.Status != Archived, type));
    }

    public async Task<IReadOnlyList<PartyKey>> ImportKeysAsync(string type, CancellationToken ct) => type switch
    {
        PartyCodes.Customer => await db.Set<Customer>().AsNoTracking().Where(x => x.Status != Archived).Select(x => new PartyKey(x.Id, x.Name, null))
            .ToListAsync(ct).ConfigureAwait(false),
        PartyCodes.Driver => await db.Set<Driver>().AsNoTracking().Where(x => x.Status != Archived).Select(x => new PartyKey(x.Id, x.NationalId, null))
            .ToListAsync(ct).ConfigureAwait(false),
        PartyCodes.Partner => await db.Set<Partner>().AsNoTracking().Where(x => x.Status != Archived).Select(x => new PartyKey(x.Id, x.Name, null))
            .ToListAsync(ct).ConfigureAwait(false),
        _ => [],
    };

    /// <summary>A transport party is only itself until the shared customers of session 8 link it to an accounting party.</summary>
    public Task<IReadOnlyList<PartyRef>> SamePartyAsync(PartyRef party, CancellationToken ct) => Task.FromResult<IReadOnlyList<PartyRef>>([party]);

    private async Task<PartyMatch?> FindAsync<T>(string? token, Func<T, string> status, CancellationToken ct) where T : AuditableEntity
    {
        var publicId = tokens.Decode<T>(token);
        if (publicId is null) return null;
        var row = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false);
        return row is null ? null : new PartyMatch(row.Id, status(row) != Archived);
    }
}
