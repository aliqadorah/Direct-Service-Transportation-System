using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Accounting.Contracts.Parties;

namespace DirectService.Accounting.Application.Parties;

/// <summary>
/// Parties on entry lines (FR-106, G-17): Customer / Driver / Partner (TRN) and ApArParty (ACC), identified by URL
/// tokens in the API and by internal ids in acc.EntryLines (PartyModule + PartyType + PartyId, no FK — design §3.2).
/// Since Stage 3 session 6c the parties come from the modules' <see cref="IPartyProvider"/>s (registration order: TRN, then ACC).
/// </summary>
public sealed class PartyResolver(IEnumerable<IPartyProvider> providers) : IPartyLookup
{
    private readonly IReadOnlyList<IPartyProvider> _providers = providers.ToList();

    public IReadOnlyList<string> Types => _providers.SelectMany(p => p.Types).ToList();

    public string ModuleOf(string type) => ProviderOf(type)?.Module ?? (type == AccountingCodes.PartyApAr ? AccountingCodes.ModuleAcc : AccountingCodes.ModuleTrn);

    /// <summary>Resolves a party for a new line: it must exist in the company and not be archived.</summary>
    public async Task<PostingParty?> ResolveAsync(PartyRequest? party, string field, CancellationToken ct)
    {
        if (party is null || (string.IsNullOrEmpty(party.Type) && string.IsNullOrEmpty(party.Id))) return null;
        var provider = party.Type is null ? null : ProviderOf(party.Type);
        if (provider is null) throw Fail.Field(field + ".type", SharedErrorCodes.ValidationFormat, new FieldRef(field + ".type"));
        var found = await provider.FindAsync(party.Type!, party.Id, ct).ConfigureAwait(false)
            ?? throw Fail.Field(field + ".id", SharedErrorCodes.ValidationFormat, new FieldRef(field + ".id"));
        if (!found.IsActive) throw Fail.RuleOn(field, SharedErrorCodes.RecordArchived, new FieldRef(party.Type!));
        return new PostingParty(provider.Module, party.Type!, found.Id);
    }

    /// <summary>Internal id of a party of a given type from its token (filters only; null when not found).</summary>
    public async Task<long?> IdOfAsync(string type, string? token, CancellationToken ct) =>
        ProviderOf(type) is { } provider ? (await provider.FindAsync(type, token, ct).ConfigureAwait(false))?.Id : null;

    /// <summary>Display data (token + name) for the parties of a set of lines.</summary>
    public async Task<Dictionary<(string Type, long Id), PartyResponse>> DescribeAsync(IEnumerable<(string? Type, long? Id)> parties, CancellationToken ct)
    {
        var result = new Dictionary<(string, long), PartyResponse>();
        foreach (var group in parties.Where(p => p.Type is not null && p.Id is not null).GroupBy(p => p.Type!))
        {
            if (ProviderOf(group.Key) is not { } provider) continue;
            var ids = group.Select(p => p.Id!.Value).Distinct().ToList();
            foreach (var (id, info) in await provider.DescribeAsync(group.Key, ids, ct).ConfigureAwait(false))
                result[(group.Key, id)] = new PartyResponse(provider.Module, group.Key, info.Token, info.Name);
        }
        return result;
    }

    /// <summary>The import keys of the parties of every type (DT-27): (type, raw key, id, role).</summary>
    public async Task<IReadOnlyList<(string Type, PartyKey Key)>> ImportKeysAsync(CancellationToken ct)
    {
        var keys = new List<(string, PartyKey)>();
        foreach (var provider in _providers)
            foreach (var type in provider.Types)
                keys.AddRange((await provider.ImportKeysAsync(type, ct).ConfigureAwait(false)).Select(k => (type, k)));
        return keys;
    }

    private IPartyProvider? ProviderOf(string type) => _providers.FirstOrDefault(p => p.Types.Contains(type));
}
