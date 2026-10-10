# Step 4 · Block B5 — Trips: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied. Owner approved the step 6 mockups and accepted DT-65 … DT-69 on 10 Oct 2026._

## What was built (backend; screens follow in step 8)

| Area | Implementation | Requirements |
|---|---|---|
| Create | `TripService.CreateAsync`: form and quick-entry row share one path; selections must be active records (archived refused unless already stored); receipt number unique ignoring case and spaces (Arabic digits folded); trip and loading dates not in the future (company time zone) and not in a closed period; loading date defaults to the trip date; unloading date ≥ loading date; loaded weight > 0 (3 decimals), unloaded ≥ 0; company field rules (labels, required, hidden optional fields keep stored values) and custom fields; idempotent create by `clientId` | FR-001, FR-002, FR-017, 7.1.1 |
| Truck and fleet | Driver's plate copied to `TruckPlateAtSave`; fleet class from the driver's ownership type; partner trip when a partner is selected (own-fleet driver + partner refused, DT-48) | FR-003, FR-004, FR-153 |
| Pricing | Customer price and partner rate from the winning tariff line when not typed (tariff validity on the trip date); `TariffPrice` stored beside the price used; driver rate by FR-030 from the monthly rate of the loading month and fleet class (partner trip → 0, DT-54); missing rate blocked naming site, month and fleet type; a typed different rate saved as Manual only with masters.manage (BR-004); tariff lock refuses deviations without masters.manage and deviations are audited with the reason | FR-006, FR-022, FR-025, FR-030, FR-031, BR-028…030 |
| Calculations | `TripCalculator` (pure): billing and wage quantities by the weight-basis settings, revenue, wage, partner cost, margin, shortage and %, shortage flag above the company tolerance, tariff breach flag and amount; half-up at each amount | FR-005, FR-012, FR-023, 8.2 |
| Warnings | Non-blocking: loaded above truck capacity, unloaded above loaded × 1.02, expired driver document named (licence, registration, insurance, inspection) | FR-002, FR-003, FR-155 |
| Posting | In the same transaction: own/external fleet with wage accrual on → driver wage expense / driver payable (party driver); partner trip → partner cost expense / partner payable (party partner), always; nothing when accrual is off or the amount is 0. Technical failure → pending posting (FR-101); `TripPostingHandler` rebuilds the entry on retry, attaches it and describes trips for journal links | FR-016, FR-096…101, 8.6 |
| Edit | Only active, unbilled, unsettled trips in open periods (old and new dates); record lock and row version; the old entry is reversed (dated like the original, DT-22) and a new one posted; driver rate kept unless loading site, loading month, driver or partner changes, a different typed rate follows the manual rules; tariff price re-resolved only when customer, material, sites, partner or trip date change (FR-026) | FR-010, FR-032, BR-035 |
| Void | Reason required; blocked when invoiced, settled or in a closed period; reversal posted, status Void, record kept; an open pending posting is resolved | FR-011 |
| Needs review | Set by tolerant imports with the defaulted fields in the note; cleared by trips.edit, recorded in history as "Reviewed" | FR-014 |
| Lists | Created Trips list with all FR-009 filters (status, billing, settled, fleet, partner trips, shortage, breach, manual rate, needs review, date range, parties and sites, search in receipt / plate / names), sort, paging and totals of the filtered set; Excel/CSV export with company labels and custom fields; last ten trips of the user; quick-entry defaults from the user's last trip (DT-70) | FR-007, FR-008, FR-009, FR-018 (data) |
| Import | .xlsx, CSV, TSV or pasted rows (sent as a .tsv file); columns matched by header (Arabic/English aliases, field key, company label) or explicit mapping; driver by national ID, plate or name; partner from the cell or the driver (DT-71); every row through the form's rules with in-memory tariffs and monthly rates (the commit re-reads the rates WITH (UPDLOCK)); duplicates in the file and the company; strict (all or nothing) or tolerant (invalid optional values default and flag Needs review; rejected rows skipped, DT-72); no rate column → monthly rate, no rate → row rejected, different rate → Manual (masters.manage); import log in `cfg.ImportBatches` with mode and counts; every imported trip posts | FR-013, FR-033, US-02 |
| History | Created, each edit old → new (driver, customer, sites, material, partner, invoice and settlement shown by name/number), voided, reviewed, deviations with reason; technical-only updates (journal link) hidden | FR-015, FR-166 |
| Rate-clearing race | `IMonthlyRateLock` (Infrastructure, raw SQL `WITH (UPDLOCK, ROWLOCK)`) is used by trip save and import, so clearing a site's rates (FR-035 re-check in the clearing transaction) and saving a trip serialise (B4 review M2) | FR-035 |

12 new endpoints under `/api/v1/companies/{company}/trips` (trips.view: list, export, details, history; trips.edit: create, edit, clear review, import, recent, quick-entry defaults; trips.void: void).

OpenAPI exported (206 paths) — Redocly lint: valid, 0 warnings.

Deferred to their blocks: invoice/credit-note/settlement events on trips (B6, B8 write their audit actions on the trip), trip statement print and PDF list export (B6 print engine), shortage and breach reports (B10), live list updates (B11).

## Tests — 221 passing (208 → 221)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 5 | PRD 8.2 example (191.100 / 111.475 / 79.625, shortage 0.150 t = 0.4688 %); weight-basis settings and fall-back to loaded; partner trip cost and shortage flag; breach amount half-up (−8.198) and half-up at each amount; tolerance, capacity and unloaded warnings |
| Api.Tests | 8 | Pricing from tariff and monthly rate, frozen against later rate changes, wage-accrual entry with driver party, expired-document warning, quick-entry defaults and recent trips; partner trip with partner cost entry and own-fleet refusal; receipt uniqueness ignoring case/spaces, future date, weight and date rules, missing rate naming the site, Owner manual rate, clerk refused (BR-004) and same-as-monthly accepted, tariff lock for a clerk, audited deviation; edit with reverse + re-post (3 entries), notes-only edit keeps the entry, required/stale row version, re-resolution on site change, history old → new, void with reason and reversal, closed period refuses create/edit/void; list filters, search, totals, export, FR-035 clearing refused; strict vs tolerant import (duplicates, defaults → review, no-rate rejection, partner from driver, manual rate, audited reason), clear review; permissions (Viewer, Dispatcher), cross-tenant 404, idempotent create |
| Api.Tests (performance) | 1 | NFR-01/02 smoke test: one company with 500,000 trips (bulk-inserted); the list answers under 1 s — first page with totals of all trips ~240 ms, a month ~50 ms, flags + sort ~260 ms, receipt search < 1 s |

## Code review — summary (independent reviewer + author)
- **Fixed:**
  - H1: imported trips lost their audit reasons (manual rate, tariff deviation) because saving the import batch reset the change context; the reasons are kept per row and set again before the trips are saved (test added).
  - M1: a pending-posting retry racing an edit or a void could attach a second entry or an entry to a void trip; attaching now refuses (the trip's row version also protects the update).
  - M2: edit/void checked invoiced/settled outside the transaction with an optional row version; the row version is now required on edit, void and clear-review (DT-73), so an invoice or settlement attached meanwhile turns the stale change into 409.
  - M3: tolerant import with an unknown partner fell back to no partner; it now defaults to the driver's partner and flags the row.
  - M4: under the tariff lock a clerk could not edit any field of a trip saved with an approved deviation; the lock applies only to a new deviation (new trip, changed price or changed tariff), and the reason is recorded only then.
  - M5: on re-resolution the value resent by the form counted as typed; an unchanged resent rate or price is now treated as not typed (DT-74).
  - M6: per-row receipt queries and period checks in imports; the receipt set is checked once per file and each distinct date once.
  - L1 both dates' months preloaded; L2 shortage % clamped to the column range; L4 loading date kept on edit when omitted; L5 a partner change no longer re-resolves the customer tariff; L6 partner trips (rate 0) no longer block clearing a site's rates; L7 a changed fleet class resolves the rate again; DT-70…DT-78 recorded.
  - Suggestion applied: an edit that changes nothing the entry posts (amount, party, date, receipt) keeps the posted entry.
  - Performance (NFR-01): the list search over receipt, plate, driver and customer names took 6.8 s with 500,000 trips; search now matches any part of the receipt number once and narrows the list by ids (DT-75).
- **Accepted / noted:**
  - L3: the closed-period check on the loading date follows PRD 8.9 ("Trip and loading dates: not in a closed period") — DT-76.
  - L8: the response's breach amount uses 3 decimals (company decimals are 3 for JOD; revisit with multi-market).
  - L10: an ambiguous driver name in an import is reported as unknown; national ID or plate are unambiguous alternatives.
  - Imports run in one transaction (strict needs all-or-nothing); the 20,000-row cap bounds it. Splitting tolerant imports into chunks is a candidate improvement.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B5-01 | Medium | Stale edit/void after invoicing or settling (M2) | **Fixed** (row version required) |
| SR-B5-02 | Medium | Double posting through a retry race (M1) | **Fixed** |
| SR-B5-03 | Low | Large imports hold rate and numbering locks for the whole transaction (M6) | Reduced (fewer queries); bounded by the 20,000-row cap |
| SR-B5-04 | Info | Viewers (trips.view) see wages and margins on trips and exports | Accepted: PRD 3.4 "View trips" covers the trip's amounts (DT-77) |

Isolation (typed tokens through filtered sets; IgnoreQueryFilters only by id or explicit company), permissions per endpoint with masters.manage checks inside, mass assignment, the parameterised raw SQL of `MonthlyRateLock`, CSV injection on export and upload limits were checked and found sound. No new packages.
