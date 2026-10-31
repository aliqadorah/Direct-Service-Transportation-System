using DirectService.Accounting.Contracts.Accounting;
using DirectService.Accounting.Contracts.Posting;

namespace DirectService.Accounting.Contracts.Parties;

// First form of the party and source contracts of design b / c (Stage 3 session 2). IPartyProvider is implemented since session 6c; the session that
// switches each use to its contract may refine its members (sessions 7, 8, 11 of the Stage 3 plan) and records the change as a DT row.

/// <summary>What accounting shows of a party: its name, URL token, active flag and the control-account type its lines need (FR-106).</summary>
public sealed record PartyInfo(string Name, string? Token, bool IsActive, string? ControlPartyType);

/// <summary>
/// Registered per party module (TRN: Customer / Driver / Partner; ACC: ApArParty). Accounting treats the module, type and control-party type
/// as opaque codes from the providers (design b).
/// </summary>
public interface IPartyProvider
{
    string Module { get; }

    IReadOnlyList<string> Types { get; }

    /// <summary>The parties of one type (set-based: one call per type and page).</summary>
    Task<IReadOnlyDictionary<long, PartyInfo>> DescribeAsync(string type, IReadOnlyCollection<long> ids, CancellationToken ct);

    /// <summary>
    /// A party of one type from its URL token (Stage 3 session 6c): its id and whether it may be chosen on new lines (not archived); null when
    /// the token is not one of the company’s parties of that type.
    /// </summary>
    Task<PartyMatch?> FindAsync(string type, string? token, CancellationToken ct);

    /// <summary>The parties of one type that may be chosen, by the natural key a spreadsheet names them with (DT-27; not normalised).</summary>
    Task<IReadOnlyList<PartyKey>> ImportKeysAsync(string type, CancellationToken ct);

    /// <summary>Every party reference that is the same party (a transport customer and its linked accounting party), the given one included.</summary>
    Task<IReadOnlyList<PartyRef>> SamePartyAsync(PartyRef party, CancellationToken ct);
}

/// <summary>A party found by its token: its id and whether it may be chosen on new lines (not archived).</summary>
public sealed record PartyMatch(long Id, bool IsActive);

/// <summary>A party by its natural key for imports (name; national id for drivers), with its role when it has one (an accounting party).</summary>
public sealed record PartyKey(long Id, string? Key, string? Role = null);

/// <summary>
/// The parties of every module as the reports show and filter them (Stage 3 session 6c): the party types in the providers’ order, the module of a
/// type, the id of a party from its token and the display data of the parties of a set of lines.
/// </summary>
public interface IPartyLookup
{
    IReadOnlyList<string> Types { get; }

    string ModuleOf(string type);

    /// <summary>Internal id of a party of a given type from its token (filters only; null when not found).</summary>
    Task<long?> IdOfAsync(string type, string? token, CancellationToken ct);

    /// <summary>Display data (token + name) for the parties of a set of lines.</summary>
    Task<Dictionary<(string Type, long Id), PartyResponse>> DescribeAsync(IEnumerable<(string? Type, long? Id)> parties, CancellationToken ct);
}

/// <summary>A customer or supplier of the shared register (acc.ApArParties, design b / K-07).</summary>
public sealed record PartyProfile(string Role, string Name, string? TaxNumber, string? Phone, string? Address, int? PaymentTermsDays);

/// <summary>The shared customer / supplier register owned by Accounting (design b): every system finds, creates and links its parties here.</summary>
public interface IPartyDirectory
{
    Task<PartyRef?> FindAsync(string role, string name, CancellationToken ct);

    /// <summary>The party linked to a profile: the existing one, or a new one created from it (ensure-party on save, design b).</summary>
    Task<PartyRef> EnsureAsync(PartyRef? linked, PartyProfile profile, CancellationToken ct);

    Task<PartyInfo?> GetAsync(PartyRef party, CancellationToken ct);

    /// <summary>The balance of a party over all its references (<see cref="IPartyProvider.SamePartyAsync"/>) at a date.</summary>
    Task<decimal> BalanceAsync(PartyRef party, DateOnly asOf, CancellationToken ct);
}

/// <summary>A party's open balance and previous-balance figure for the accounting dashboard and RPT-19 / RPT-20.</summary>
public sealed record PartyBalanceRow(PartyRef Party, decimal Balance, decimal PreviousBalance);

/// <summary>Party balances a module contributes (drivers, partners); a query composed in the caller's context, never per-row calls.</summary>
public interface IPartyBalanceSource
{
    string Module { get; }

    IQueryable<PartyBalanceRow> Balances(DateOnly asOf);
}

/// <summary>A sub-ledger total to reconcile with its control account (RPT-23).</summary>
public sealed record SubLedgerTotal(string MappingKey, string PartyType, decimal Total);

/// <summary>The sub-ledger totals a module keeps outside accounting (receivables of transport invoices, driver / partner payables).</summary>
public interface IReconciliationSource
{
    string Module { get; }

    Task<IReadOnlyList<SubLedgerTotal>> TotalsAsync(DateOnly asOf, CancellationToken ct);
}
