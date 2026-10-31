using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Parties on entry lines (FR-106, G-17): Customer / Driver / Partner (TRN) and ApArParty (ACC), identified by URL
/// tokens in the API and by internal ids in acc.EntryLines (PartyModule + PartyType + PartyId, no FK — design §3.2).
/// </summary>
public sealed class PartyResolver(IAppDbContext db, IUrlTokenService tokens)
{
    public static readonly IReadOnlyList<string> Types =
        [AccountingCodes.PartyCustomer, AccountingCodes.PartyDriver, AccountingCodes.PartyPartner, AccountingCodes.PartyApAr];

    public static string ModuleOf(string type) => type == AccountingCodes.PartyApAr ? AccountingCodes.ModuleAcc : AccountingCodes.ModuleTrn;

    /// <summary>Resolves a party for a new line: it must exist in the company and not be archived.</summary>
    public async Task<PostingParty?> ResolveAsync(PartyRequest? party, string field, CancellationToken ct)
    {
        if (party is null || (string.IsNullOrEmpty(party.Type) && string.IsNullOrEmpty(party.Id))) return null;
        if (party.Type is null || !Types.Contains(party.Type)) throw Fail.Field(field + ".type", ErrorCodes.ValidationFormat, new FieldRef(field + ".type"));
        var found = party.Type switch
        {
            AccountingCodes.PartyCustomer => await FindAsync<Customer>(party.Id, c => c.Status, ct).ConfigureAwait(false),
            AccountingCodes.PartyDriver => await FindAsync<Driver>(party.Id, d => d.Status, ct).ConfigureAwait(false),
            AccountingCodes.PartyPartner => await FindAsync<Partner>(party.Id, p => p.Status, ct).ConfigureAwait(false),
            _ => await FindAsync<ApArParty>(party.Id, p => p.Status, ct).ConfigureAwait(false),
        };
        if (found is null) throw Fail.Field(field + ".id", ErrorCodes.ValidationFormat, new FieldRef(field + ".id"));
        if (found.Value.Status == AccountingCodes.Archived) throw Fail.RuleOn(field, ErrorCodes.RecordArchived, new FieldRef(party.Type));
        return new PostingParty(ModuleOf(party.Type), party.Type, found.Value.Id);
    }

    /// <summary>Internal id of a party of a given type from its token (filters only; null when not found).</summary>
    public async Task<long?> IdOfAsync(string type, string? token, CancellationToken ct) => type switch
    {
        AccountingCodes.PartyCustomer => (await FindAsync<Customer>(token, c => c.Status, ct).ConfigureAwait(false))?.Id,
        AccountingCodes.PartyDriver => (await FindAsync<Driver>(token, d => d.Status, ct).ConfigureAwait(false))?.Id,
        AccountingCodes.PartyPartner => (await FindAsync<Partner>(token, p => p.Status, ct).ConfigureAwait(false))?.Id,
        AccountingCodes.PartyApAr => (await FindAsync<ApArParty>(token, p => p.Status, ct).ConfigureAwait(false))?.Id,
        _ => null,
    };

    /// <summary>Display data (token + name) for the parties of a set of lines.</summary>
    public async Task<Dictionary<(string Type, long Id), PartyResponse>> DescribeAsync(IEnumerable<(string? Type, long? Id)> parties, CancellationToken ct)
    {
        var result = new Dictionary<(string, long), PartyResponse>();
        foreach (var group in parties.Where(p => p.Type is not null && p.Id is not null).GroupBy(p => p.Type!))
        {
            var ids = group.Select(p => p.Id!.Value).Distinct().ToList();
            IEnumerable<(long Id, Guid PublicId, string Name, Type Entity)> rows = group.Key switch
            {
                AccountingCodes.PartyCustomer => (await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name }).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => (x.Id, x.PublicId, x.Name, typeof(Customer))),
                AccountingCodes.PartyDriver => (await db.Set<Driver>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, Name = x.FullName }).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => (x.Id, x.PublicId, x.Name, typeof(Driver))),
                AccountingCodes.PartyPartner => (await db.Set<Partner>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name }).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => (x.Id, x.PublicId, x.Name, typeof(Partner))),
                AccountingCodes.PartyApAr => (await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PublicId, x.Name }).ToListAsync(ct).ConfigureAwait(false))
                    .Select(x => (x.Id, x.PublicId, x.Name, typeof(ApArParty))),
                _ => [],
            };
            foreach (var r in rows)
                result[(group.Key, r.Id)] = new PartyResponse(ModuleOf(group.Key), group.Key, tokens.Encode(r.Entity, r.PublicId), r.Name);
        }
        return result;
    }

    private async Task<(long Id, string Status)?> FindAsync<T>(string? token, Func<T, string> status, CancellationToken ct)
        where T : AuditableEntity
    {
        var publicId = tokens.Decode<T>(token);
        if (publicId is null) return null;
        var row = await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false);
        return row is null ? null : (row.Id, status(row));
    }
}

/// <summary>Names of the EF Core query filters (defined in the infrastructure DbContext).</summary>
internal static class QueryFilterNames
{
    public const string SoftDelete = "SoftDelete";
}
