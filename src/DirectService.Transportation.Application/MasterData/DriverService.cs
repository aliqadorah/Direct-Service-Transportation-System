using DirectService.Shared.Contracts.History;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.MasterData;

/// <summary>
/// Drivers with their truck (FR-152 … FR-156, PRD 7.8.1): one truck per driver, edited on the driver's profile; national ID
/// (10 digits) and plate unique within the company (Arabic digits folded, DT-37); payment details by method; document-expiry
/// alerts (expired or within 30 days, FR-154); archive/restore and delete only while unreferenced (FR-162); truck changes appear
/// in the history old → new (FR-153). Writes need masters.manage; edits respect record locks (BR-039).
/// </summary>
public sealed partial class DriverService(IAppDbContext db, MasterDataSupport support, IStandardFields fields, ICustomFields customFields,
    IChangeContext changes, IRecordLockGuard locks, IRequestContext ctx)
{
    public const string EntityType = "TRN.Driver";

    private static readonly IReadOnlyDictionary<string, Expression<Func<Driver, object?>>> SortFields = new Dictionary<string, Expression<Func<Driver, object?>>>(StringComparer.Ordinal)
    {
        ["fullName"] = d => d.FullName,
        ["nationalId"] = d => d.NationalId,
        ["plateNumber"] = d => d.PlateNumber,
        ["capacityTons"] = d => d.CapacityTons,
        ["licenceExpiry"] = d => d.LicenceExpiry,
        ["registrationExpiry"] = d => d.RegistrationExpiry,
        ["insuranceExpiry"] = d => d.InsuranceExpiry,
        ["inspectionExpiry"] = d => d.InspectionExpiry,
        ["status"] = d => d.Status,
        ["createdAt"] = d => d.CreatedAt,
    };

    // ------------------------------------------------------------ queries

    public async Task<PagedResult<DriverResponse>> ListAsync(MasterListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = await FilterAsync(q, ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var ordered = MasterDataSupport.Sort(query, q.Sort, SortFields, "fullName");
        return await MasterDataSupport.PageAsync(ordered, q.Page, q.PageSize, d => ToResponse(d, today), ct).ConfigureAwait(false);
    }

    /// <summary>All drivers matching a list query (exports), in list order.</summary>
    internal async Task<(IReadOnlyList<Driver> Drivers, DateOnly Today)> AllAsync(MasterListQuery q, CancellationToken ct)
    {
        var query = await FilterAsync(q, ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        return (await MasterDataSupport.Sort(query, q.Sort, SortFields, "fullName").ToListAsync(ct).ConfigureAwait(false), today);
    }

    private async Task<IQueryable<Driver>> FilterAsync(MasterListQuery q, CancellationToken ct)
    {
        var query = MasterDataSupport.WithStatus(db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).Include(d => d.Partner), q.Status);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            var compact = MasterDataSupport.Plate(text) ?? text;
            query = query.Where(d => d.FullName.Contains(text) || d.NationalId.Contains(compact) || d.PlateNumber.Contains(compact)
                || d.Phone1.Contains(compact) || (d.Phone2 != null && d.Phone2.Contains(compact)));
        }
        switch (q.Fleet?.Trim())
        {
            case null or "":
                break;
            case MasterCodes.Own or MasterCodes.External:
                var fleet = q.Fleet.Trim();
                query = query.Where(d => d.OwnershipType.FleetClass == fleet);
                break;
            default:
                throw Fail.Field("fleet", ErrorCodes.ValidationFormat, new FieldRef("fleet"));
        }
        if (!string.IsNullOrEmpty(q.OwnershipTypeId))
        {
            var type = await support.FindAsync<OwnershipType>(q.OwnershipTypeId, ct).ConfigureAwait(false);
            query = query.Where(d => d.OwnershipTypeId == type.Id);
        }
        if (!string.IsNullOrEmpty(q.PartnerId))
        {
            var partner = await support.FindAsync<Partner>(q.PartnerId, ct).ConfigureAwait(false);
            query = query.Where(d => d.PartnerId == partner.Id);
        }
        if (!string.IsNullOrWhiteSpace(q.Expiry))
        {
            var today = await support.TodayAsync(ct).ConfigureAwait(false);
            var limit = today.AddDays(MasterCodes.AlertDays);
            query = q.Expiry.Trim().ToLowerInvariant() switch
            {
                "expired" => query.Where(d => d.LicenceExpiry < today || d.RegistrationExpiry < today || d.InsuranceExpiry < today || d.InspectionExpiry < today),
                "expiring" => query.Where(d => (d.LicenceExpiry >= today && d.LicenceExpiry <= limit) || (d.RegistrationExpiry >= today && d.RegistrationExpiry <= limit)
                    || (d.InsuranceExpiry >= today && d.InsuranceExpiry <= limit) || (d.InspectionExpiry >= today && d.InspectionExpiry <= limit)),
                "alerts" => query.Where(d => d.LicenceExpiry <= limit || d.RegistrationExpiry <= limit || d.InsuranceExpiry <= limit || d.InspectionExpiry <= limit),
                _ => throw Fail.Field("expiry", ErrorCodes.ValidationFormat, new FieldRef("expiry")),
            };
        }
        return query;
    }

    /// <summary>Active drivers for the trip form with their truck and expired-document warnings (FR-155, FR-162, FR-167).</summary>
    public async Task<IReadOnlyList<DriverOption>> OptionsAsync(string? q, CancellationToken ct)
    {
        var query = db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).Include(d => d.Partner).Where(d => d.Status == MasterCodes.Active);
        if (MasterDataSupport.SearchText(q) is { } text)
        {
            var compact = MasterDataSupport.Plate(text) ?? text;
            query = query.Where(d => d.FullName.Contains(text) || d.PlateNumber.Contains(compact) || d.NationalId.Contains(compact));
        }
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var list = await query.OrderBy(d => d.FullName).Take(500).ToListAsync(ct).ConfigureAwait(false);
        return list.Select(d => new DriverOption(support.Token<Driver>(d.PublicId), d.FullName, d.PlateNumber, d.CapacityTons, d.OwnershipType.FleetClass,
            PartnerRef(d.Partner), Alerts(d, today))).ToList();
    }

    public async Task<DriverProfile> ProfileAsync(string token, CancellationToken ct)
    {
        var d = await LoadAsync(token, ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        return new DriverProfile(ToResponse(d, today), await ReferencesAsync(d.Id, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, d.Id, ct).ConfigureAwait(false));
    }

    public async Task<ReferencesResponse> ReferencesAsync(string token, CancellationToken ct)
    {
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        var refs = await ReferencesAsync(d.Id, ct).ConfigureAwait(false);
        return new ReferencesResponse(refs.Count == 0, refs);
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, d.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Active drivers with a document expired or expiring within 30 days, soonest first (FR-154; the dashboard reads this).</summary>
    public async Task<IReadOnlyList<DriverResponse>> ExpiryAlertsAsync(CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var limit = today.AddDays(MasterCodes.AlertDays);
        var list = await db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).Include(d => d.Partner)
            .Where(d => d.Status == MasterCodes.Active
                && (d.LicenceExpiry <= limit || d.RegistrationExpiry <= limit || d.InsuranceExpiry <= limit || d.InspectionExpiry <= limit))
            .ToListAsync(ct).ConfigureAwait(false);
        return list.Select(d => ToResponse(d, today)).OrderBy(r => r.Alerts.Min(a => a.ExpiresOn)).ThenBy(r => r.FullName, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------ writes

    public async Task<(DriverResponse Value, bool Created)> CreateAsync(DriverRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Driver>(clientId, ct).ConfigureAwait(false) is { } again)
            return (await ResponseAsync(again.Id, ct).ConfigureAwait(false), false);
        var d = new Driver();
        if (clientId is Guid id) d.PublicId = id;
        await ApplyAsync(d, r, isNew: true, ct).ConfigureAwait(false);
        db.Set<Driver>().Add(d);
        if (await support.SaveCreateAsync<Driver>(clientId, ct).ConfigureAwait(false) is { } raced) return (await ResponseAsync(raced.Id, ct).ConfigureAwait(false), false);
        return (await ResponseAsync(d.Id, ct).ConfigureAwait(false), true);
    }

    public async Task<DriverResponse> UpdateAsync(string token, DriverRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, d.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(d, r.RowVersion);
        await ApplyAsync(d, r, isNew: false, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(d.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Archive (hidden from new entries, kept in history and reports) or restore (FR-162).</summary>
    public async Task<DriverResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, d.Id, ct).ConfigureAwait(false);
        db.ExpectRowVersion(d, rowVersion);
        d.Status = active ? MasterCodes.Active : MasterCodes.Archived;
        changes.SetAction(d, active ? "Restore" : "Archive");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(d.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Soft delete with a reason, refused while trips, advances or settlements refer to the driver (BR-040, BR-041).</summary>
    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, d.Id, ct).ConfigureAwait(false);
        MasterDataSupport.EnsureUnreferenced(await ReferencesAsync(d.Id, ct).ConfigureAwait(false));
        d.DeleteReason = why;
        db.Set<Driver>().Remove(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ReferenceCount>> ReferencesAsync(long id, CancellationToken ct) => support.References(
        ("Trips", await db.Set<Trip>().CountAsync(t => t.DriverId == id, ct).ConfigureAwait(false)),
        ("Advances", await db.Set<Advance>().CountAsync(a => a.DriverId == id, ct).ConfigureAwait(false)),
        ("Settlements", await db.Set<Settlement>().CountAsync(s => s.DriverId == id, ct).ConfigureAwait(false)));

    /// <summary>Validates a request and applies it (shared by the form and the import).</summary>
    internal async Task ApplyAsync(Driver d, DriverRequest r, bool isNew, CancellationToken ct)
    {
        var rules = await fields.RulesAsync(FieldCatalog.Driver, ct).ConfigureAwait(false);
        var errors = new List<FieldError>();

        // Hidden optional fields are not on the form: an edit keeps their stored values (review M1).
        var fullName = MasterDataSupport.Name(r.FullName);
        var nationalId = TextNormalizer.CleanCode(r.NationalId)?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var phone1 = MasterDataSupport.Compact(r.Phone1);
        var phone2 = rules.Keep("phone2", isNew, MasterDataSupport.Compact(r.Phone2), d.Phone2);
        var address = rules.Keep("address", isNew, TextNormalizer.CleanText(r.Address), d.Address);
        var licenceNo = TextNormalizer.CleanCode(r.LicenceNo)?.ToUpperInvariant();
        var licenceType = MasterDataSupport.Name(r.LicenceType);
        var method = r.PaymentMethod is null ? "Cash" : MasterCodes.PaymentMethods.FirstOrDefault(m => string.Equals(m, r.PaymentMethod.Trim(), StringComparison.OrdinalIgnoreCase));
        var bankName = TextNormalizer.CleanText(r.BankName);
        var iban = MasterDataSupport.Compact(r.Iban)?.ToUpperInvariant();
        var cliq = TextNormalizer.CleanText(r.CliqAlias);
        var plate = MasterDataSupport.Plate(r.PlateNumber);
        var truckType = rules.Keep("truckType", isNew, TextNormalizer.CleanText(r.TruckType), d.TruckType);
        var trailer = rules.Keep("trailerNo", isNew, MasterDataSupport.Plate(r.TrailerNo), d.TrailerNo);
        var notes = rules.Keep("notes", isNew, TextNormalizer.CleanText(r.Notes), d.Notes);

        rules.Require(errors, "fullName", fullName is null);
        if (fullName is { Length: > 150 }) rules.Invalid(errors, "fullName", ErrorCodes.ValidationRange);
        rules.Require(errors, "nationalId", nationalId is null);
        if (nationalId is not null && !NationalIdPattern().IsMatch(nationalId)) rules.Invalid(errors, "nationalId");
        rules.Require(errors, "phone1", phone1 is null);
        if (phone1 is not null && !await support.IsPhoneAsync(phone1, ct).ConfigureAwait(false)) rules.Invalid(errors, "phone1");
        rules.Require(errors, "phone2", phone2 is null);
        if (phone2 is not null && !await support.IsPhoneAsync(phone2, ct).ConfigureAwait(false)) rules.Invalid(errors, "phone2");
        rules.Require(errors, "address", address is null);
        if (address is { Length: > 300 }) rules.Invalid(errors, "address", ErrorCodes.ValidationRange);
        rules.Require(errors, "licenceNo", licenceNo is null);
        if (licenceNo is { Length: > 30 }) rules.Invalid(errors, "licenceNo", ErrorCodes.ValidationRange);
        rules.Require(errors, "licenceType", licenceType is null);
        if (licenceType is { Length: > 30 }) rules.Invalid(errors, "licenceType", ErrorCodes.ValidationRange);
        rules.Require(errors, "licenceExpiry", r.LicenceExpiry is null);
        if (method is null) rules.Invalid(errors, "paymentMethod");
        rules.Require(errors, "bankName", bankName is null);
        rules.RequireWhen(errors, "bankName", method == "Bank" && bankName is null);
        if (bankName is { Length: > 100 }) rules.Invalid(errors, "bankName", ErrorCodes.ValidationRange);
        rules.Require(errors, "iban", iban is null);
        rules.RequireWhen(errors, "iban", method == "Bank" && iban is null);
        if (iban is not null && (iban.Length > 34 || !await support.IsIbanAsync(iban, ct).ConfigureAwait(false))) rules.Invalid(errors, "iban");
        rules.Require(errors, "cliqAlias", cliq is null);
        rules.RequireWhen(errors, "cliqAlias", method == "CliQ" && cliq is null);
        if (cliq is { Length: > 50 }) rules.Invalid(errors, "cliqAlias", ErrorCodes.ValidationRange);
        rules.Require(errors, "plateNumber", plate is null);
        if (plate is { Length: > 20 }) rules.Invalid(errors, "plateNumber", ErrorCodes.ValidationRange);
        rules.Require(errors, "truckType", truckType is null);
        if (truckType is { Length: > 50 }) rules.Invalid(errors, "truckType", ErrorCodes.ValidationRange);
        rules.Require(errors, "trailerNo", trailer is null);
        if (trailer is { Length: > 20 }) rules.Invalid(errors, "trailerNo", ErrorCodes.ValidationRange);
        rules.Require(errors, "capacityTons", r.CapacityTons is null);
        if (r.CapacityTons is decimal cap && (cap <= 0 || cap >= 1_000_000m || decimal.Round(cap, 3) != cap)) rules.Invalid(errors, "capacityTons", ErrorCodes.ValidationRange);
        rules.Require(errors, "ownershipTypeId", string.IsNullOrEmpty(r.OwnershipTypeId));
        rules.Require(errors, "registrationExpiry", r.RegistrationExpiry is null);
        rules.Require(errors, "insuranceExpiry", r.InsuranceExpiry is null);
        rules.Require(errors, "inspectionExpiry", r.InspectionExpiry is null);
        rules.Require(errors, "notes", notes is null);
        if (notes is { Length: > 1000 }) rules.Invalid(errors, "notes", ErrorCodes.ValidationRange);
        var custom = await customFields.ValidateValuesAsync(FieldCatalog.Driver, r.CustomValues, d.CustomValues, isNew, errors, ct).ConfigureAwait(false);
        if (errors.Count > 0) throw Fail.Fields(errors);

        var type = await support.ChooseAsync<OwnershipType>("ownershipTypeId", r.OwnershipTypeId, d.Id == 0 ? null : d.OwnershipTypeId, t => t.Name, ct).ConfigureAwait(false);
        Partner? partner = null;
        if (!string.IsNullOrEmpty(r.PartnerId))
        {
            partner = await support.ChooseAsync<Partner>("partnerId", r.PartnerId, d.PartnerId, p => p.Name, ct).ConfigureAwait(false);
            // A partner's truck is not part of the company's own fleet (BR-029: Company type = own fleet).
            if (type.FleetClass == MasterCodes.Own) throw Fail.RuleOn("partnerId", ErrorCodes.DriverPartnerOwnFleet, partner.Name);
        }

        if (await db.Set<Driver>().AnyAsync(x => x.NationalId == nationalId && x.Id != d.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, nationalId);
        if (await db.Set<Driver>().AnyAsync(x => x.PlateNumber == plate && x.Id != d.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, plate);

        d.FullName = fullName!;
        d.NationalId = nationalId!;
        d.Phone1 = phone1!;
        d.Phone2 = phone2;
        d.Address = address;
        d.LicenceNo = licenceNo!;
        d.LicenceType = licenceType!;
        d.LicenceExpiry = r.LicenceExpiry!.Value;
        d.PaymentMethod = method!;
        d.BankName = bankName;
        d.Iban = iban;
        d.CliqAlias = cliq;
        d.PlateNumber = plate!;
        d.TruckType = truckType;
        d.TrailerNo = trailer;
        d.CapacityTons = r.CapacityTons!.Value;
        d.OwnershipTypeId = type.Id;
        d.PartnerId = partner?.Id;
        d.RegistrationExpiry = r.RegistrationExpiry!.Value;
        d.InsuranceExpiry = r.InsuranceExpiry!.Value;
        d.InspectionExpiry = r.InspectionExpiry!.Value;
        d.Notes = notes;
        d.CustomValues = custom;
    }

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex NationalIdPattern();

    // ------------------------------------------------------------ responses

    private async Task<Driver> LoadAsync(string token, CancellationToken ct)
    {
        var d = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        return await db.Set<Driver>().AsNoTracking().Include(x => x.OwnershipType).Include(x => x.Partner).SingleAsync(x => x.Id == d.Id, ct).ConfigureAwait(false);
    }

    private async Task<DriverResponse> ResponseAsync(long id, CancellationToken ct)
    {
        var d = await db.Set<Driver>().AsNoTracking().Include(x => x.OwnershipType).Include(x => x.Partner).SingleAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return ToResponse(d, await support.TodayAsync(ct).ConfigureAwait(false));
    }

    internal DriverResponse ToResponse(Driver d, DateOnly today) =>
        new(support.Token<Driver>(d.PublicId), d.FullName, d.NationalId, d.Phone1, d.Phone2, d.Address, d.LicenceNo, d.LicenceType, d.LicenceExpiry,
            d.PaymentMethod, d.BankName, d.Iban, d.CliqAlias, d.PlateNumber, d.TruckType, d.TrailerNo, d.CapacityTons,
            new Ref(support.Token<OwnershipType>(d.OwnershipType.PublicId), OwnershipTypeNames.Display(d.OwnershipType.Name, ctx.Language)), d.OwnershipType.FleetClass, PartnerRef(d.Partner),
            d.RegistrationExpiry, d.InsuranceExpiry, d.InspectionExpiry, d.Status, d.Notes, CustomFieldValues.Read(d.CustomValues), Alerts(d, today),
            Convert.ToBase64String(d.RowVersion));

    private Ref? PartnerRef(Partner? p) => p is null ? null : new Ref(support.Token<Partner>(p.PublicId), p.Name);

    /// <summary>Documents expired or expiring within 30 days (FR-154), soonest first.</summary>
    public static IReadOnlyList<DocumentAlert> Alerts(Driver d, DateOnly today)
    {
        var limit = today.AddDays(MasterCodes.AlertDays);
        return new[] { ("licence", d.LicenceExpiry), ("registration", d.RegistrationExpiry), ("insurance", d.InsuranceExpiry), ("inspection", d.InspectionExpiry) }
            .Where(x => x.Item2 <= limit)
            .OrderBy(x => x.Item2)
            .Select(x => new DocumentAlert(x.Item1, x.Item2, x.Item2 < today))
            .ToList();
    }
}
