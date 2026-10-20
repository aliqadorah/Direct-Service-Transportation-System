using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Accounting parties (FR-122): suppliers and customers for non-transport documents — name (unique among the active parties of a role),
/// tax number, phone, address, control account of the role's party type, payment terms; archive/restore, protected delete, history.
/// Transport customers are offered as receivable parties: a receivable for a transport customer uses its linked accounting party, created
/// and linked on first use (DT-101).
/// </summary>
public sealed class ApArPartyService(IAppDbContext db, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks, ChartOfAccountsService chart, IRequestContext ctx)
{
    public const string EntityType = "ACC.ApArParty";

    private static readonly IReadOnlyDictionary<string, Expression<Func<ApArParty, object?>>> SortFields = new Dictionary<string, Expression<Func<ApArParty, object?>>>(StringComparer.Ordinal)
    {
        ["name"] = p => p.Name,
        ["role"] = p => p.PartyRole,
        ["status"] = p => p.Status,
        ["createdAt"] = p => p.CreatedAt,
    };

    /// <summary>"Supplier" / "Customer" from a request value (case-insensitive).</summary>
    public static string? Role(string? role, string field = "role") => (role?.Trim().ToLowerInvariant()) switch
    {
        null or "" => null,
        "supplier" => ApArCodes.Supplier,
        "customer" => ApArCodes.Customer,
        _ => throw Fail.Field(field, ErrorCodes.ValidationFormat, field),
    };

    public async Task<PagedResult<ApArPartyResponse>> ListAsync(ApArPartyListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = MasterDataSupport.WithStatus(db.Set<ApArParty>().AsNoTracking(), q.Status);
        if (Role(q.Role) is { } role) query = query.Where(p => p.PartyRole == role);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            var compact = MasterDataSupport.Compact(text) ?? text;
            query = query.Where(p => p.Name.Contains(text) || (p.TaxNumber != null && p.TaxNumber.Contains(compact)) || (p.Phone != null && p.Phone.Contains(compact)));
        }
        var paging = new PageRequest(q.Page, q.PageSize);
        var sorted = MasterDataSupport.Sort(query, q.Sort, SortFields, "name").ThenBy(p => p.Id);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await sorted.Skip(paging.Skip).Take(paging.SafePageSize).Select(p => p.Id).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<ApArPartyResponse>(await ResponsesAsync(rows, ct).ConfigureAwait(false), paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>Choices for a document: active accounting parties of the role and, for receivables, active transport customers.</summary>
    public async Task<IReadOnlyList<ApArPartyOption>> OptionsAsync(string? role, CancellationToken ct)
    {
        var r = Role(role) ?? throw Fail.Field("role", ErrorCodes.ValidationRequired, "role");
        var parties = (await db.Set<ApArParty>().AsNoTracking().Where(p => p.PartyRole == r && p.Status == ApArCodes.Active).OrderBy(p => p.Name)
                .Select(p => new { p.PublicId, p.Name, p.TaxNumber, p.PaymentTermsDays }).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => new ApArPartyOption(AccountingCodes.PartyApAr, support.Token<ApArParty>(p.PublicId), p.Name, p.TaxNumber, p.PaymentTermsDays)).ToList();
        if (r == ApArCodes.Customer)
        {
            parties.AddRange((await db.Set<Customer>().AsNoTracking().Where(c => c.Status == MasterCodes.Active).OrderBy(c => c.Name)
                    .Select(c => new { c.PublicId, c.Name, c.TaxNumber, c.PaymentTermsDays }).ToListAsync(ct).ConfigureAwait(false))
                .Select(c => new ApArPartyOption(AccountingCodes.PartyCustomer, support.Token<Customer>(c.PublicId), c.Name, c.TaxNumber, c.PaymentTermsDays)));
        }
        return parties;
    }

    public async Task<ApArPartyDetails> GetAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<ApArParty>(token, ct).ConfigureAwait(false);
        return new ApArPartyDetails((await ResponsesAsync([p.Id], ct).ConfigureAwait(false))[0], await ReferencesAsync(p.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, p.Id, ct).ConfigureAwait(false));
    }

    public async Task<(ApArPartyResponse Value, bool Created)> CreateAsync(ApArPartyRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<ApArParty>(clientId, ct).ConfigureAwait(false) is { } again) return ((await ResponsesAsync([again.Id], ct).ConfigureAwait(false))[0], false);
        var p = new ApArParty { PartyRole = Role(r.Role) ?? throw Fail.Field("role", ErrorCodes.ValidationRequired, "role"), Status = ApArCodes.Active };
        if (clientId is Guid id) p.PublicId = id;
        await ApplyAsync(p, r, ct).ConfigureAwait(false);
        db.Set<ApArParty>().Add(p);
        if (await support.SaveCreateAsync<ApArParty>(clientId, ct).ConfigureAwait(false) is { } raced) return ((await ResponsesAsync([raced.Id], ct).ConfigureAwait(false))[0], false);
        return ((await ResponsesAsync([p.Id], ct).ConfigureAwait(false))[0], true);
    }

    /// <summary>Edit; the role is fixed once documents exist (their control lines name the party under that role).</summary>
    public async Task<ApArPartyResponse> UpdateAsync(string token, ApArPartyRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var p = await support.FindAsync<ApArParty>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(p, r.RowVersion);
        if (Role(r.Role) is { } role && role != p.PartyRole)
        {
            if (await InUseAsync(p.Id, ct).ConfigureAwait(false) || await db.Set<Customer>().AnyAsync(c => c.ApArPartyId == p.Id, ct).ConfigureAwait(false))
                throw Fail.RuleOn("role", ErrorCodes.RecordInUse, p.Name);
            p.PartyRole = role;
        }
        await ApplyAsync(p, r, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ResponsesAsync([p.Id], ct).ConfigureAwait(false))[0];
    }

    public async Task<ApArPartyResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var p = await support.FindAsync<ApArParty>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        if (active && p.Status != ApArCodes.Active) await EnsureUniqueNameAsync(p.PartyRole, p.Name, p.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(p, rowVersion);
        p.Status = active ? ApArCodes.Active : MasterCodes.Archived;
        changes.SetAction(p, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ResponsesAsync([p.Id], ct).ConfigureAwait(false))[0];
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var p = await support.FindAsync<ApArParty>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, p.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(p.Id, ct).ConfigureAwait(false));
        p.DeleteReason = why;
        db.Set<ApArParty>().Remove(p);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) => support.References(
        ("ApArDocuments", await db.Set<ApArDocument>().CountAsync(d => d.PartyId == id, ct).ConfigureAwait(false)),
        ("Customers", await db.Set<Customer>().CountAsync(c => c.ApArPartyId == id, ct).ConfigureAwait(false)),
        ("EntryLines", await db.Set<EntryLine>().CountAsync(l => l.PartyModule == AccountingCodes.ModuleAcc && l.PartyType == AccountingCodes.PartyApAr && l.PartyId == id, ct)
            .ConfigureAwait(false)));

    private async Task ApplyAsync(ApArParty p, ApArPartyRequest r, CancellationToken ct)
    {
        var name = MasterDataSupport.Name(r.Name);
        var tax = MasterDataSupport.Compact(r.TaxNumber);
        var phone = MasterDataSupport.Compact(r.Phone);
        var address = Common.TextNormalizer.CleanText(r.Address);
        var v = new Validator().Require("name", name).MaxLength("name", name, 200).MaxLength("taxNumber", tax, 30).MaxLength("address", address, 300)
            .Check(tax is null || tax.All(char.IsAsciiDigit), "taxNumber", ErrorCodes.ValidationFormat)
            .Check(r.PaymentTermsDays is null or (>= 0 and <= 3650), "paymentTermsDays", ErrorCodes.ValidationRange)
            .Check(!string.IsNullOrWhiteSpace(r.ControlAccountId), "controlAccountId", ErrorCodes.ValidationRequired);
        if (phone is not null && !await support.IsPhoneAsync(phone, ct).ConfigureAwait(false)) v.Check(false, "phone", ErrorCodes.ValidationFormat);
        v.ThrowIfInvalid();
        var account = await support.ChooseAsync<Account>("controlAccountId", r.ControlAccountId, p.ControlAccountId == 0 ? null : p.ControlAccountId, a => a.Code, ct).ConfigureAwait(false);
        if (account.Id != p.ControlAccountId)
        {
            // Payments and returns post to the party's control account: it is fixed once documents or journal lines use the party (review M1).
            if (p.Id != 0 && await InUseAsync(p.Id, ct).ConfigureAwait(false)) throw Fail.RuleOn("controlAccountId", ErrorCodes.RecordInUse, p.Name);
            EnsureControlAccount(account, p.PartyRole, await HasChildrenAsync(account.Id, ct).ConfigureAwait(false));
        }
        if (p.Status == ApArCodes.Active) await EnsureUniqueNameAsync(p.PartyRole, name!, p.Id, ct).ConfigureAwait(false);
        p.Name = name!;
        p.TaxNumber = tax;
        p.Phone = phone;
        p.Address = address;
        p.ControlAccountId = account.Id;
        p.PaymentTermsDays = r.PaymentTermsDays ?? p.PaymentTermsDays;
    }

    /// <summary>Documents or journal lines name the party.</summary>
    private async Task<bool> InUseAsync(long id, CancellationToken ct) =>
        await db.Set<ApArDocument>().AnyAsync(d => d.PartyId == id, ct).ConfigureAwait(false)
        || await db.Set<EntryLine>().AnyAsync(l => l.PartyModule == AccountingCodes.ModuleAcc && l.PartyType == AccountingCodes.PartyApAr && l.PartyId == id, ct).ConfigureAwait(false);

    private Task<bool> HasChildrenAsync(long accountId, CancellationToken ct) => db.Set<Account>().AnyAsync(a => a.ParentId == accountId, ct);

    /// <summary>Control account of the role's party type (design: "Must be a control account"; G-17 party types), active and postable.</summary>
    internal static void EnsureControlAccount(Account a, string role, bool hasChildren)
    {
        if (!a.IsControlAccount || a.ControlPartyType != role || a.Status != AccountingCodes.Active || !a.IsPostable || hasChildren)
            throw Fail.RuleOn("controlAccountId", ErrorCodes.ApArControlAccount, role);
    }

    private async Task EnsureUniqueNameAsync(string role, string name, long id, CancellationToken ct)
    {
        if (await db.Set<ApArParty>().AnyAsync(p => p.Id != id && p.PartyRole == role && p.Status == ApArCodes.Active && p.Name == name, ct).ConfigureAwait(false))
            throw Fail.Field("name", ErrorCodes.DuplicateKey, name);
    }

    /// <summary>
    /// The accounting party of a transport customer for receivables (FR-122): the linked one, or a new Customer party created from the
    /// customer's data with the trade-receivables control account and linked to it (DT-101). Runs inside the document's transaction.
    /// </summary>
    internal async Task<ApArParty> ForCustomerAsync(Customer c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        // Runs after the receivable number lock, so two first receivables of the customer serialise; the link is read again under it (review L2).
        var current = await db.Set<Customer>().AsNoTracking().Where(x => x.Id == c.Id).Select(x => x.ApArPartyId).SingleAsync(ct).ConfigureAwait(false);
        if (current is long linked) return await db.Set<ApArParty>().SingleAsync(p => p.Id == linked, ct).ConfigureAwait(false);
        var accountId = await db.Set<PostingKeyMapping>().Where(m => m.PostingKey == PostingKeys.TradeReceivables).Select(m => (long?)m.AccountId)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.Rule(ErrorCodes.PostingKeysMissing, PostingKeys.NameList([PostingKeys.TradeReceivables], ctx.Language));
        var party = new ApArParty
        {
            PartyRole = ApArCodes.Customer, Name = c.Name, TaxNumber = c.TaxNumber, Phone = c.Phone is { Length: <= 20 } ? c.Phone : null,
            Address = c.Address is { Length: <= 300 } ? c.Address : null, ControlAccountId = accountId, PaymentTermsDays = c.PaymentTermsDays, Status = ApArCodes.Active,
        };
        db.Set<ApArParty>().Add(party);
        changes.SetAction(party, "Create", c.Name);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var customer = await db.Set<Customer>().SingleAsync(x => x.Id == c.Id, ct).ConfigureAwait(false);
        customer.ApArPartyId = party.Id;
        changes.SetAction(customer, "LinkApArParty", party.Name);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return party;
    }

    private async Task<List<ApArPartyResponse>> ResponsesAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var rows = await db.Set<ApArParty>().AsNoTracking().Where(p => ids.Contains(p.Id)).Include(p => p.ControlAccount).ToListAsync(ct).ConfigureAwait(false);
        var balances = await db.Set<ApArDocument>().AsNoTracking().Where(d => ids.Contains(d.PartyId) && d.Status == ApArCodes.Posted)
            .GroupBy(d => d.PartyId).Select(g => new { g.Key, Open = g.Sum(d => d.RemainingAmount) }).ToDictionaryAsync(x => x.Key, x => x.Open, ct).ConfigureAwait(false);
        var customers = (await db.Set<Customer>().AsNoTracking().Where(c => c.ApArPartyId != null && ids.Contains(c.ApArPartyId.Value))
                .Select(c => new { c.ApArPartyId, c.PublicId, c.Name }).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(c => c.ApArPartyId!.Value, c => new Ref(support.Token<Customer>(c.PublicId), c.Name));
        var byId = rows.ToDictionary(p => p.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(p => new ApArPartyResponse(support.Token<ApArParty>(p.PublicId), p.PartyRole, p.Name, p.TaxNumber,
            p.Phone, p.Address, chart.Ref(p.ControlAccount), p.PaymentTermsDays, p.Status, balances.GetValueOrDefault(p.Id), customers[p.Id].ToList(),
            DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(p.RowVersion))).ToList();
    }
}
