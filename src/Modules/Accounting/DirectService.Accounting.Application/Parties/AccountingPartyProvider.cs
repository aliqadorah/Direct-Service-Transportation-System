using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Contracts.Parties;
using DirectService.Accounting.Contracts.Posting;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Accounting.Application.Parties;

/// <summary>
/// The accounting parties on entry lines (<see cref="IPartyProvider"/>, design b; Stage 3 session 6c): the customers and suppliers of
/// acc.ApArParties, found by their URL tokens and described with their names (archived and deleted ones too).
/// </summary>
public sealed class AccountingPartyProvider(IAppDbContext db, IUrlTokenService tokens) : IPartyProvider
{
    public string Module => PartyCodes.ModuleAcc;

    public IReadOnlyList<string> Types { get; } = [PartyCodes.ApArParty];

    public async Task<PartyMatch?> FindAsync(string type, string? token, CancellationToken ct)
    {
        if (type != PartyCodes.ApArParty) return null;
        var publicId = tokens.Decode<ApArParty>(token);
        if (publicId is null) return null;
        var row = await db.Set<ApArParty>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false);
        return row is null ? null : new PartyMatch(row.Id, row.Status != AccountingCodes.Archived);
    }

    public async Task<IReadOnlyDictionary<long, PartyInfo>> DescribeAsync(string type, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (type != PartyCodes.ApArParty) return new Dictionary<long, PartyInfo>();
        return (await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name, x.Status, x.PartyRole }).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(x => x.Id, x => new PartyInfo(x.Name, tokens.Encode<ApArParty>(x.PublicId), x.Status != AccountingCodes.Archived, x.PartyRole));
    }

    public async Task<IReadOnlyList<PartyKey>> ImportKeysAsync(string type, CancellationToken ct) => type != PartyCodes.ApArParty ? []
        : await db.Set<ApArParty>().AsNoTracking().Where(x => x.Status != AccountingCodes.Archived).Select(x => new PartyKey(x.Id, x.Name, x.PartyRole))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <summary>An accounting party is only itself until the shared customers of session 8 link transport customers to it.</summary>
    public Task<IReadOnlyList<PartyRef>> SamePartyAsync(PartyRef party, CancellationToken ct) => Task.FromResult<IReadOnlyList<PartyRef>>([party]);
}
