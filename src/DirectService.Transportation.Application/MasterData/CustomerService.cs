using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// Customers (FR-157, PRD 7.8.2): name unique among active customers, optional tax and contact details, payment terms (days, used
/// for due dates), custom fields and the configured standard fields (FR-180, FR-182). Created only here, never from the trip form
/// (FR-167). Archive/restore; delete only while no trip, invoice, receipt, credit note or tariff refers to it (BR-040).
/// </summary>
public sealed class CustomerService(IAppDbContext db, MasterDataSupport support, IStandardFields fields, ICustomFields customFields,
    IChangeContext changes, IRecordLockGuard locks)
{
    public const string EntityType = "TRN.Customer";

    private static readonly IReadOnlyDictionary<string, Expression<Func<Customer, object?>>> SortFields = new Dictionary<string, Expression<Func<Customer, object?>>>(StringComparer.Ordinal)
    {
        ["name"] = c => c.Name,
        ["city"] = c => c.City,
        ["taxNumber"] = c => c.TaxNumber,
        ["paymentTermsDays"] = c => c.PaymentTermsDays,
        ["status"] = c => c.Status,
        ["createdAt"] = c => c.CreatedAt,
    };

    public async Task<PagedResult<CustomerResponse>> ListAsync(MasterListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        return await MasterDataSupport.PageAsync(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name"), q.Page, q.PageSize, ToResponse, ct).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<Customer>> AllAsync(MasterListQuery q, CancellationToken ct) =>
        await MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "name").ToListAsync(ct).ConfigureAwait(false);

    private IQueryable<Customer> Filter(MasterListQuery q)
    {
        var query = MasterDataSupport.WithStatus(db.Set<Customer>().AsNoTracking().Include(c => c.ApArParty), q.Status);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            var compact = MasterDataSupport.Compact(text) ?? text;
            query = query.Where(c => c.Name.Contains(text) || (c.TaxNumber != null && c.TaxNumber.Contains(compact))
                || (c.Phone != null && c.Phone.Contains(compact)) || (c.City != null && c.City.Contains(text))
                || (c.ContactPerson != null && c.ContactPerson.Contains(text)));
        }
        return query;
    }

    /// <summary>Active customers for forms (FR-162, FR-167).</summary>
    public async Task<IReadOnlyList<MasterOption>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<Customer>().AsNoTracking().Where(c => c.Status == MasterCodes.Active).OrderBy(c => c.Name)
            .Select(c => new { c.PublicId, c.Name }).ToListAsync(ct).ConfigureAwait(false))
        .Select(c => new MasterOption(support.Token<Customer>(c.PublicId), c.Name)).ToList();

    public async Task<CustomerProfile> ProfileAsync(string token, CancellationToken ct)
    {
        var c = await LoadAsync(token, ct).ConfigureAwait(false);
        return new CustomerProfile(ToResponse(c), await ReferencesAsync(c.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, c.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(c.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, c.Id, ct).ConfigureAwait(false);
    }

    public async Task<(CustomerResponse Value, bool Created)> CreateAsync(CustomerRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Customer>(clientId, ct).ConfigureAwait(false) is { } again)
            return (await ResponseAsync(again.Id, ct).ConfigureAwait(false), false);
        var c = new Customer();
        if (clientId is Guid id) c.PublicId = id;
        await ApplyAsync(c, r, isNew: true, ct).ConfigureAwait(false);
        db.Set<Customer>().Add(c);
        if (await support.SaveCreateAsync<Customer>(clientId, ct).ConfigureAwait(false) is { } raced) return (await ResponseAsync(raced.Id, ct).ConfigureAwait(false), false);
        return (await ResponseAsync(c.Id, ct).ConfigureAwait(false), true);
    }

    public async Task<CustomerResponse> UpdateAsync(string token, CustomerRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, c.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(c, r.RowVersion);
        await ApplyAsync(c, r, isNew: false, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(c.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Archive or restore (FR-162). Restoring is refused while an active customer has the same name (DT-40).</summary>
    public async Task<CustomerResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, c.Id, ct).ConfigureAwait(false);
        if (active && c.Status != MasterCodes.Active)
            await EnsureUniqueNameAsync(c.Name, c.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(c, rowVersion);
        c.Status = active ? MasterCodes.Active : MasterCodes.Archived;
        changes.SetAction(c, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(c.Id, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, c.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(c.Id, ct).ConfigureAwait(false));
        c.DeleteReason = why;
        db.Set<Customer>().Remove(c);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) => support.References(
        ("Trips", await db.Set<Trip>().CountAsync(t => t.CustomerId == id, ct).ConfigureAwait(false)),
        ("Invoices", await db.Set<Invoice>().CountAsync(i => i.CustomerId == id, ct).ConfigureAwait(false)),
        ("Receipts", await db.Set<Receipt>().CountAsync(x => x.CustomerId == id, ct).ConfigureAwait(false)),
        ("CreditNotes", await db.Set<CreditNote>().CountAsync(x => x.CustomerId == id, ct).ConfigureAwait(false)),
        ("Tariffs", await db.Set<Tariff>().CountAsync(x => x.CustomerId == id, ct).ConfigureAwait(false)));

    internal async Task ApplyAsync(Customer c, CustomerRequest r, bool isNew, CancellationToken ct)
    {
        var rules = await fields.RulesAsync(FieldCatalog.Customer, ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        var name = MasterDataSupport.Name(r.Name);
        // Hidden fields are not on the form: an edit keeps their stored values (review M1).
        var tax = rules.Keep("taxNumber", isNew, MasterDataSupport.Compact(r.TaxNumber), c.TaxNumber);
        var registration = rules.Keep("registrationNo", isNew, MasterDataSupport.Compact(r.RegistrationNo), c.RegistrationNo);
        var phone = rules.Keep("phone", isNew, MasterDataSupport.Compact(r.Phone), c.Phone);
        var email = rules.Keep("email", isNew, TextNormalizer.CleanText(r.Email), c.Email);
        var address = rules.Keep("address", isNew, TextNormalizer.CleanText(r.Address), c.Address);
        var city = rules.Keep("city", isNew, MasterDataSupport.Name(r.City), c.City);
        var contact = rules.Keep("contactPerson", isNew, MasterDataSupport.Name(r.ContactPerson), c.ContactPerson);
        var terms = rules.Keep("paymentTermsDays", isNew, r.PaymentTermsDays, (int?)c.PaymentTermsDays);
        var notes = rules.Keep("notes", isNew, TextNormalizer.CleanText(r.Notes), c.Notes);

        rules.Require(errors, "name", name is null);
        if (name is { Length: > 200 }) rules.Invalid(errors, "name", ErrorCodes.ValidationRange);
        rules.Require(errors, "taxNumber", tax is null);
        if (tax is { Length: > 30 }) rules.Invalid(errors, "taxNumber", ErrorCodes.ValidationRange);
        rules.Require(errors, "registrationNo", registration is null);
        if (registration is { Length: > 30 }) rules.Invalid(errors, "registrationNo", ErrorCodes.ValidationRange);
        rules.Require(errors, "phone", phone is null);
        if (phone is not null && !await support.IsPhoneAsync(phone, ct).ConfigureAwait(false)) rules.Invalid(errors, "phone");
        rules.Require(errors, "email", email is null);
        if (email is not null && (email.Length > 150 || !EmailFormat.IsValid(email))) rules.Invalid(errors, "email");
        rules.Require(errors, "address", address is null);
        if (address is { Length: > 300 }) rules.Invalid(errors, "address", ErrorCodes.ValidationRange);
        rules.Require(errors, "city", city is null);
        if (city is { Length: > 100 }) rules.Invalid(errors, "city", ErrorCodes.ValidationRange);
        rules.Require(errors, "contactPerson", contact is null);
        if (contact is { Length: > 150 }) rules.Invalid(errors, "contactPerson", ErrorCodes.ValidationRange);
        rules.Require(errors, "paymentTermsDays", terms is null);
        if (terms is < 0 or > 3650) rules.Invalid(errors, "paymentTermsDays", ErrorCodes.ValidationRange);
        rules.Require(errors, "notes", notes is null);
        if (notes is { Length: > 1000 }) rules.Invalid(errors, "notes", ErrorCodes.ValidationRange);
        var custom = await customFields.ValidateValuesAsync(FieldCatalog.Customer, r.CustomValues, c.CustomValues, isNew, errors, ct).ConfigureAwait(false);
        if (errors.Count > 0) throw Fail.Fields(errors);

        ApArParty? party = null;
        if (!string.IsNullOrEmpty(r.ApArPartyId))
        {
            // Only an active receivable (Customer) party of shared Accounting; the stored link stays valid if it was archived since (review M3).
            party = await support.ChooseAsync<ApArParty>("apArPartyId", r.ApArPartyId, c.ApArPartyId, p => p.Name, ct).ConfigureAwait(false);
            if (party.PartyRole != AccountingCodes.PartyCustomer) throw Fail.Field("apArPartyId", ErrorCodes.ValidationFormat, new FieldRef("apArPartyId"));
            if (party.Status != MasterCodes.Active && party.Id != c.ApArPartyId) throw Fail.RuleOn("apArPartyId", ErrorCodes.RecordArchived, party.Name);
        }
        if (c.Status == MasterCodes.Active) await EnsureUniqueNameAsync(name!, c.Id, ct).ConfigureAwait(false);

        c.Name = name!;
        c.TaxNumber = tax;
        c.RegistrationNo = registration;
        c.Phone = phone;
        c.Email = email;
        c.Address = address;
        c.City = city;
        c.ContactPerson = contact;
        c.PaymentTermsDays = terms ?? 0;
        c.ApArPartyId = party?.Id;
        c.Notes = notes;
        c.CustomValues = custom;
    }

    private async Task EnsureUniqueNameAsync(string name, long id, CancellationToken ct)
    {
        if (await db.Set<Customer>().AnyAsync(x => x.Status == MasterCodes.Active && x.Name == name && x.Id != id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name);
    }

    private async Task<Customer> LoadAsync(string token, CancellationToken ct)
    {
        var c = await support.FindAsync<Customer>(token, ct).ConfigureAwait(false);
        return await db.Set<Customer>().AsNoTracking().Include(x => x.ApArParty).SingleAsync(x => x.Id == c.Id, ct).ConfigureAwait(false);
    }

    private async Task<CustomerResponse> ResponseAsync(long id, CancellationToken ct) =>
        ToResponse(await db.Set<Customer>().AsNoTracking().Include(x => x.ApArParty).SingleAsync(x => x.Id == id, ct).ConfigureAwait(false));

    internal CustomerResponse ToResponse(Customer c) =>
        new(support.Token<Customer>(c.PublicId), c.Name, c.TaxNumber, c.RegistrationNo, c.Phone, c.Email, c.Address, c.City, c.ContactPerson,
            c.PaymentTermsDays, c.ApArParty is null ? null : new Ref(support.Token<ApArParty>(c.ApArParty.PublicId), c.ApArParty.Name),
            c.Status, c.Notes, CustomFieldValues.Read(c.CustomValues), Convert.ToBase64String(c.RowVersion));
}
