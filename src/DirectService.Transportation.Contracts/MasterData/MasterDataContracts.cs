using System.Text.Json;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.MasterData;

// Master data (M04, PRD 7.8). Identifiers are always URL tokens (SEC-22); related records are { id, name }.
// Custom-field values travel as a JSON object keyed by the field key (FR-179, FR-180).

/// <summary>
/// List query shared by every master-data list (FR-165): search text, status (active | archived | all; default active),
/// sort (white-listed field, "-" prefix for descending) and paging. Drivers add fleet, ownership type, partner and
/// document-expiry filters (FR-154).
/// </summary>
public sealed record MasterListQuery(
    string? Q, string? Status, string? Sort, string? Fleet, string? OwnershipTypeId, string? PartnerId, string? Expiry,
    int Page = 1, int PageSize = 50);

/// <summary>An active record offered in pick lists of forms (FR-162 "archived records hidden from new-entry lists", FR-167).</summary>
public sealed record MasterOption(string Id, string Name);

/// <summary>One referencing record type with its count (BR-040), e.g. ("Trips", "trips", 4).</summary>
public sealed record ReferenceCount(string Type, string Label, int Count);

/// <summary>What refers to a record; <see cref="CanDelete"/> is false while anything does (FR-162).</summary>
public sealed record ReferencesResponse(bool CanDelete, IReadOnlyList<ReferenceCount> References);

/// <summary>
/// A history entry of a record (FR-166), readable (owner feedback 14 Oct 2026, DT-188): <paramref name="Text"/> is the event as a sentence in
/// the reader's language ("Ali Ali added the trip to draft invoice INV-00001"), <paramref name="ActionLabel"/> the action in words, the
/// changes with field labels and formatted values (technical columns left out), <paramref name="Lines"/> the lines of a journal entry posted
/// with it and <paramref name="Links"/> the documents it names (the screen opens them when the reader may see them).
/// </summary>
public sealed record HistoryEntry(DateTime At, string? User, string Action, string? Reason, IReadOnlyList<FieldChange> Changes,
    string? Text = null, string? ActionLabel = null, IReadOnlyList<HistoryLine>? Lines = null, IReadOnlyList<HistoryLink>? Links = null);

// FieldChange, HistoryLine and HistoryLink: Shared.Contracts (the shared audit describer builds them, Stage 2 session 9).

// ---------------------------------------------------------------- drivers with truck (FR-152 … FR-156)

public sealed record DriverRequest(
    string? FullName, string? NationalId, string? Phone1, string? Phone2, string? Address,
    string? LicenceNo, string? LicenceType, DateOnly? LicenceExpiry,
    string? PaymentMethod, string? BankName, string? Iban, string? CliqAlias,
    string? PlateNumber, string? TruckType, string? TrailerNo, decimal? CapacityTons, string? OwnershipTypeId, string? PartnerId,
    DateOnly? RegistrationExpiry, DateOnly? InsuranceExpiry, DateOnly? InspectionExpiry,
    string? Notes, Dictionary<string, JsonElement>? CustomValues, string? ClientId, string? RowVersion);

/// <summary>A driver document that is expired or expires within 30 days (FR-154).</summary>
public sealed record DocumentAlert(string Document, DateOnly ExpiresOn, bool Expired);

public sealed record DriverResponse(
    string Id, string FullName, string NationalId, string Phone1, string? Phone2, string? Address,
    string LicenceNo, string LicenceType, DateOnly LicenceExpiry,
    string PaymentMethod, string? BankName, string? Iban, string? CliqAlias,
    string PlateNumber, string? TruckType, string? TrailerNo, decimal CapacityTons, Ref OwnershipType, string FleetClass, Ref? Partner,
    DateOnly RegistrationExpiry, DateOnly InsuranceExpiry, DateOnly InspectionExpiry,
    string Status, string? Notes, IReadOnlyDictionary<string, JsonElement> CustomValues, IReadOnlyList<DocumentAlert> Alerts,
    string RowVersion);

/// <summary>Active driver for the trip form: truck, fleet class and expired-document warnings (FR-155, FR-167).</summary>
public sealed record DriverOption(
    string Id, string Name, string PlateNumber, decimal CapacityTons, string FleetClass, Ref? Partner, IReadOnlyList<DocumentAlert> Alerts);

/// <summary>Driver profile (FR-156): the record, what refers to it and its history. Account figures join in B8.</summary>
public sealed record DriverProfile(DriverResponse Driver, IReadOnlyList<ReferenceCount> References, IReadOnlyList<HistoryEntry> History);

// ---------------------------------------------------------------- customers (FR-157)

public sealed record CustomerRequest(
    string? Name, string? TaxNumber, string? RegistrationNo, string? Phone, string? Email, string? Address, string? City,
    string? ContactPerson, int? PaymentTermsDays, string? ApArPartyId, string? Notes, Dictionary<string, JsonElement>? CustomValues,
    string? ClientId, string? RowVersion);

public sealed record CustomerResponse(
    string Id, string Name, string? TaxNumber, string? RegistrationNo, string? Phone, string? Email, string? Address, string? City,
    string? ContactPerson, int PaymentTermsDays, Ref? ApArParty, string Status, string? Notes,
    IReadOnlyDictionary<string, JsonElement> CustomValues, string RowVersion);

public sealed record CustomerProfile(CustomerResponse Customer, IReadOnlyList<ReferenceCount> References, IReadOnlyList<HistoryEntry> History);

// ---------------------------------------------------------------- partners (FR-158)

public sealed record PartnerRequest(
    string? Name, string? TaxNumber, string? Phone, string? Address, string? ContactPerson, string? BankName, string? Iban,
    string? CliqAlias, string? Notes, string? ClientId, string? RowVersion);

public sealed record PartnerResponse(
    string Id, string Name, string? TaxNumber, string? Phone, string? Address, string? ContactPerson, string? BankName, string? Iban,
    string? CliqAlias, string? Notes, string Status, string RowVersion);

/// <summary>Partner profile (FR-158): the record, its linked drivers, references and history.</summary>
public sealed record PartnerProfile(
    PartnerResponse Partner, IReadOnlyList<MasterOption> Drivers, IReadOnlyList<ReferenceCount> References, IReadOnlyList<HistoryEntry> History);

// ---------------------------------------------------------------- materials, sites, ownership types (FR-159 … FR-161)

public sealed record MaterialRequest(string? Name, string? Unit, string? ClientId, string? RowVersion);

public sealed record MaterialResponse(string Id, string Name, string Unit, string Status, string RowVersion);

/// <summary>Loading or unloading site (two separate lists, FR-160).</summary>
public sealed record SiteRequest(string? Name, string? Area, string? ClientId, string? RowVersion);

public sealed record SiteResponse(string Id, string Name, string? Area, string Status, string RowVersion);

public sealed record OwnershipTypeRequest(string? Name, string? FleetClass, string? ClientId, string? RowVersion);

public sealed record OwnershipTypeResponse(string Id, string Name, string FleetClass, bool IsBuiltIn, string RowVersion);

/// <summary>Record shown with its references and history (materials, sites, ownership types).</summary>
public sealed record RecordDetails<T>(T Record, IReadOnlyList<ReferenceCount> References, IReadOnlyList<HistoryEntry> History);

/// <summary>Columns matched by an import (field key → header found in the file) and the headers left unused (FR-164).</summary>
public sealed record ImportColumns(IReadOnlyDictionary<string, string> Matched, IReadOnlyList<string> Unused);

/// <summary>
/// Preview of a master-data import (FR-164): every row with its problems (duplicates on unique keys, unknown links, invalid values)
/// and the column matching used. Nothing is saved while any row has a problem (strict mode, DT-44).
/// </summary>
public sealed record MasterImportPreview(
    int TotalRows, int ValidRows, int RejectedRows, IReadOnlyList<ImportRowResult> Rows, ImportColumns Columns);
