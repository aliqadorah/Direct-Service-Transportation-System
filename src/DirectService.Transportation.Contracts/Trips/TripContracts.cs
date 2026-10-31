using DirectService.Shared.Contracts.History;
using System.Text.Json;
using DirectService.Transportation.Contracts.MasterData;

namespace DirectService.Transportation.Contracts.Trips;

/// <summary>
/// A trip from the full form or the quick-entry row (PRD 7.1.1). Selections are URL tokens. Prices left empty are filled on the
/// server: customer price and partner rate from the tariffs, driver rate from the monthly site rate (FR-006, FR-022, FR-030).
/// A typed driver rate that differs from the monthly rate is saved as Manual and needs masters.manage (BR-004).
/// </summary>
public sealed record TripRequest(
    string? ReceiptNo, DateOnly? TripDate, DateOnly? LoadingDate, DateOnly? UnloadingDate,
    string? DriverId, string? CustomerId, string? LoadingSiteId, string? UnloadingSiteId, string? MaterialId, string? PartnerId,
    decimal? LoadedWeight, decimal? UnloadedWeight, decimal? CustomerPrice, decimal? DriverRate, decimal? PartnerRate,
    string? Notes, IReadOnlyDictionary<string, JsonElement>? CustomValues, string? ClientId, string? RowVersion,
    string? PartnerPaidHandling = null, string? PartnerPaidReason = null);

/// <summary>A linked document shown by number (invoice, settlement, journal entry).</summary>
public sealed record DocRef(string Id, string? Number);

/// <summary>A non-blocking warning on save (FR-002): capacity, unloaded above loaded × 1.02, expired driver document (FR-155).</summary>
public sealed record TripWarning(string Code, string Message);

/// <summary>
/// A trip with its frozen prices and amounts (PRD 7.1.1, 8.2). <see cref="Billing"/> is Unbilled / Invoiced; <see cref="Status"/>
/// Active / Void; <see cref="BreachAmount"/> = (price − tariff price) × billing quantity, negative = loss (FR-023).
/// DT-271: <see cref="IsSettled"/> = paid — a driver trip in a settlement, a partner trip fully covered by the partner's payments (oldest trips
/// first); <see cref="Payment"/> Settled / Unsettled (driver trips), PartnerPaid / PartnerPartlyPaid / PartnerUnpaid (partner trips), null when
/// void; <see cref="PartnerOpen"/> = the part of the partner cost still to pay (partner trips).
/// </summary>
public sealed record TripResponse(
    string Id, string ReceiptNo, DateOnly TripDate, DateOnly LoadingDate, DateOnly? UnloadingDate,
    Ref Driver, string TruckPlate, Ref Customer, Ref LoadingSite, Ref UnloadingSite, Ref Material, Ref? Partner, string FleetClass,
    decimal LoadedWeight, decimal? UnloadedWeight, decimal BillingQty, decimal WageQty,
    decimal CustomerPrice, decimal? TariffPrice, decimal? BreachAmount, decimal DriverRate, string RateSource, string RateMonth, decimal? PartnerRate,
    decimal Revenue, decimal DriverWage, decimal PartnerCost, decimal Margin, decimal? ShortageWeight, decimal? ShortagePct,
    bool IsShortageFlagged, bool IsTariffBreach, bool IsManualRate, bool NeedsReview, string? ReviewNote,
    string Status, string Billing, bool IsSettled, bool CanEdit, DocRef? Invoice, DocRef? Settlement, DocRef? JournalEntry, bool PostingPending,
    string? VoidReason, string? Notes, IReadOnlyDictionary<string, JsonElement> CustomValues, DateTime CreatedAt, string RowVersion,
    string? Payment = null, decimal? PartnerOpen = null);

/// <summary>Inputs of the trip form's live calculation (UX-17): weights and prices as typed; nothing is saved.</summary>
public sealed record TripCalculationRequest(
    decimal? LoadedWeight, decimal? UnloadedWeight, decimal? CustomerPrice, decimal? TariffPrice, decimal? DriverRate, decimal? PartnerRate, bool IsPartnerTrip);

/// <summary>
/// The amounts a save would store (PRD 8.2) with the company's settings that drove them, so the form shows the same figures as the
/// server (calculation rules stay on the server, NFR-14).
/// </summary>
public sealed record TripCalculationResponse(
    decimal BillingQty, decimal WageQty, decimal Revenue, decimal DriverWage, decimal PartnerCost, decimal Margin, decimal? ShortageWeight,
    decimal? ShortagePct, bool IsShortageFlagged, bool IsTariffBreach, decimal? BreachAmount, bool BillOnUnloadedWeight, bool PayDriverOnLoadedWeight,
    decimal ShortageTolerancePct, int Decimals);

/// <summary>A saved trip with the warnings of the save (FR-002).</summary>
public sealed record TripSaveResult(TripResponse Trip, IReadOnlyList<TripWarning> Warnings);

/// <summary>A trip with its history (FR-015).</summary>
public sealed record TripDetails(TripResponse Trip, IReadOnlyList<TripWarning> Warnings, IReadOnlyList<HistoryEntry> History);

/// <summary>
/// Created Trips list filters (FR-009): status (active / void / all), billing (unbilled / invoiced), settled (yes / no), fleet (own /
/// external), partner trips (yes / no), flags (shortage, breach, manual, review), date range on the trip date, parties and sites by
/// token, search in receipt number and plate, white-listed sort, paging.
/// </summary>
public sealed record TripListQuery(
    string? Status, string? Billing, string? Settled, string? Fleet, string? PartnerTrips, bool? Shortage, bool? Breach, bool? Manual, bool? Review,
    DateOnly? From, DateOnly? To, string? Driver, string? Customer, string? Partner, string? LoadingSite, string? UnloadingSite, string? Material,
    string? Q, string? Sort, int Page = 1, int PageSize = 50);

/// <summary>Totals of the filtered list (FR-009 totals row).</summary>
public sealed record TripTotals(int Trips, decimal LoadedWeight, decimal UnloadedWeight, decimal Revenue, decimal DriverWage, decimal PartnerCost, decimal Margin);

/// <summary>A page of trips with the totals of every trip matching the filters.</summary>
public sealed record TripPage(PagedResult<TripResponse> Page, TripTotals Totals);

/// <summary>
/// Values the quick-entry row starts with (FR-007): driver, customer, material and route of the user's last saved trip, and today's
/// date in the company time zone.
/// </summary>
public sealed record QuickEntryDefaults(DateOnly Today, Ref? Driver, Ref? Customer, Ref? Material, Ref? LoadingSite, Ref? UnloadingSite);

/// <summary>Reason for a void (FR-011).</summary>
/// <summary>
/// A trip void. DT-230: when the trip was already paid to its partner, <paramref name="PartnerPaidHandling"/> says what to do with the amount
/// (Return or Deduct; NotPaid is done by voiding the payment first) — without it the void is answered with <c>trip.partnerPaid</c>.
/// </summary>
public sealed record TripVoidRequest(string? Reason, string? RowVersion, string? PartnerPaidHandling = null);

/// <summary>Clearing "Needs review" after checking (FR-014).</summary>
public sealed record TripReviewRequest(string? RowVersion);

/// <summary>
/// Trip import preview (FR-013): rows with problems, duplicates, rows flagged for review, matched columns, and the importable fields
/// with their labels and whether the file must have them (for the column-matching step). When required columns are not found
/// (<paramref name="MissingColumns"/>, field keys) the rows are not checked: the columns are matched by hand first.
/// </summary>
public sealed record TripImportPreview(
    int TotalRows, int ValidRows, int RejectedRows, int DuplicateRows, int ReviewRows, string Mode,
    IReadOnlyList<ImportRowResult> Rows, ImportColumns Columns, IReadOnlyList<ImportField>? Fields = null, IReadOnlyList<string>? MissingColumns = null);

/// <summary>A field an import can fill: key (mapping key), label in the user's language, and whether the file must have the column.</summary>
public sealed record ImportField(string Key, string Label, bool Required);

/// <summary>Trip import result: batch, imported, rejected and flagged counts (import log).</summary>
public sealed record TripImportResult(string BatchId, int TotalRows, int ImportedRows, int RejectedRows, int ReviewRows);
