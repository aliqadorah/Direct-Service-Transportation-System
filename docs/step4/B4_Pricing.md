# Step 4 · Block B4 — Pricing: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied. Owner accepted DT-36 … DT-49 on 10 Oct 2026._

## What was built (backend only — no UI code before mockups, step 6)

| Area | Implementation | Requirements |
|---|---|---|
| Tariff lines | `TariffService`: party type Customer/Partner; party, material, loading site, unloading site each a record or All (null); price per ton > 0 (D-03) with the company currency's decimals; optional Valid from/to (to ≥ from); notes. Add, edit, archive, restore (no delete, DT-51); unique per keys + start date within the company, archived lines included (design unique index) → `tariff.duplicate`; archived master records cannot be chosen on new lines (a stored link stays valid); record-lock guard and row version on edits; idempotent create by `clientId` | FR-019, 7.2.1 |
| List | Filters: party type; party, material, loading/unloading site by token or `all` (lines whose key is All); lines valid on a date; search in names and notes; status; white-listed sort; paging | FR-019, FR-165 |
| Resolution | `PricingRules` (pure): specificity party 8 + material 4 + loading 2 + unloading 1; a line matches when each key equals the trip's value or is All and the date is within validity; highest score wins, tie → latest Valid from (open start = earliest), then newest line. `PricingResolver.TariffAsync` runs the same rules in SQL through `IX_TRN_Tariffs_Lookup`; archived lines never resolve | FR-020, FR-021, BR-028 |
| Rate history | `RateHistoryLog`: every add, edit, archive, restore (tariffs) and create, edit, clear (monthly rates) writes an insert-only `trn.TRN_RateHistory` row with readable before/after snapshots (names at the time, "All" = empty), user and time; history endpoints show old → new per field. Tariff and rate rows themselves are also audited in `core.AuditLogs`; history rows are not copied there (DT-60) | FR-026, FR-036 |
| Import / export | Tariffs: Excel/CSV export (All shown as "All"/"الكل"); import with column matching (Arabic/English/field key or explicit `mapping`), party/material/sites by name among active records, empty or All = All, the form's rules per row, duplicates in the file and against the company, strict and add-only (DT-44), one transaction with a history row per line and `cfg.ImportBatches` | FR-019 |
| Monthly site rates | `MonthlyRateService`: month strip with status None/Partial/Complete over the active loading sites and a "has trips" flag (DT-53); a month lists every active site (plus archived sites that still have rates there) with own/external rate, trip count, token and row version; bulk save of the sites sent in one transaction; each rate > 0 with the currency's decimals; no inheritance between months; archived sites get no new rates (DT-62) | FR-027, FR-028, FR-029 |
| Month with trips | Saving a month that has (non-void) trips returns the warning "only new trips use the new rates"; the GET shows `hasTrips` and per-site trip counts so the UI warns before saving (DT-58). Clearing both rates of a site with trips in the month is refused (`rates.inUse`); otherwise clearing soft-deletes the row with a reason (DT-59). Rates are linked to the site by id, so renaming keeps them | FR-034, FR-035 |
| Missing rates | `/monthly-rates/missing` — active sites without rates in the current month (company time zone) or a given month, for the dashboard card (B10) | FR-037 (data) |
| Trip-form quote | `/pricing/quote`: customer price and partner rate from the winning tariff lines (with the line's token, score and validity), driver rate from the monthly table for the rate month (loading date) and the driver's fleet class; partner defaults to the driver's partner; partner trip → driver rate 0 (DT-54); no rate → message naming site, month and fleet type and whether the user may enter a manual rate (BR-004); tariff-lock state and whether the user may deviate (FR-025) | FR-006, FR-022, FR-030 (data) |
| Driver-rate rules for B5 | `PricingRules.ResolveDriverRate` (FR-030 table incl. `ManualNotAllowed`, DT-55) and `MustResolveAgain` (FR-032) ready for trip save/edit/import | FR-030 … FR-033 (logic) |
| Authorization | Writes and imports: masters.manage. Reads (lists, months, history, exports): masters.manage, trips.view, trips.edit or reports.view. Quote: trips.edit or masters.manage. Missing-rates warning: every member (DT-50) | PRD 3.4, BR-004 |

17 new endpoints under `/api/v1/companies/{company}/{tariffs|monthly-rates|pricing}`, each with permission metadata and an OpenAPI summary (test-enforced).

Deferred to the blocks that own them: trips storing tariff price, breach flag and amount, tariff-lock enforcement and audited deviations, frozen driver rate/source/month, edit and import rate rules (B5 — FR-022 … FR-025, FR-031 … FR-033 use the rules and resolver built here); breach report (B10, FR-024); PDF print of a month's rates (print engine, B6 — DT-63); dashboard card (B10, FR-037).

OpenAPI exported (196 paths) — Redocly lint: valid, 0 warnings.

**Also changed outside the module:** `ImportMapping` (explicit import column mapping) moved out of `MasterExchangeService` to be shared; `MasterDataSupport.Decode<T>`; `Tariff` implements `IArchivable`; `RateHistory` added to the interceptor's not-audited types (DT-60).

## Tests — 208 passing (181 → 208)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 18 cases | Specificity scores 0/8/12/3/15 (FR-021); matching with inclusive validity and open ends (FR-020); most-specific wins, tie → latest Valid from, open start earliest, expired lines lose (BR-028); the FR-030 table incl. partner trip 0 and manual not allowed (BR-004); fleet-class rate and FR-032 re-resolution; months, ranges and strip status |
| Api.Tests | 9 | Quote prices through the API with the same scores, validity and ties, archived lines ignored; list filters (key, `all`, validity date, status, sort); validation, typed party tokens, idempotent create, duplicates incl. archived lines, archive/restore/edit, stale version, history old → new, no RateHistory copies in AuditLogs, partner protected by its tariff; archived master records refused on new lines; tariff import (Arabic headers, All, unknown names, in-file and existing duplicates, explicit mapping, 403 for Viewer) and export; month strip, entry, no inheritance, validation, required/stale row versions, clear and re-create, history, rename keeps rates, export, site protected by its rates; months with trips warn and refuse clearing a used site (trip inserted directly — trips come in B5); quote driver rate by fleet class, missing-rate message, partner trip, own-fleet driver + partner refused, missing-rates warning; permissions (Viewer reads, Accountant cannot write, Dispatcher manages); cross-tenant tokens find nothing. The generic isolation and endpoint-coverage tests cover all 17 new endpoints |

## Code review — summary (independent reviewer + author)
- **No High findings.** Verified: computed Specificity matches the rules; SQL `ValidFrom DESC` puts open starts last as the rules require; null-safe duplicate check matches the unique index; tracked projections persist edits.
- **Fixed:**
  - M1: a monthly-grid save without `rowVersion` could silently overwrite another manager's rates. Changing or clearing stored rates now requires the row version (400 when missing, 409 when stale).
  - M2: clearing a site's rates could race with a trip save. The trip count is checked again inside the transaction after the cleared rows are written (their locks held to commit); **B5 must read the rate row with an update lock when saving a trip**.
  - M3: tariff import made ~6 queries per row. Names resolve from cached active records, currency decimals are read once, and duplicates are checked against one set of the company's existing keys (0 queries per row).
  - M4: DT-50 … DT-63 recorded in BUILD_STATUS for owner review.
  - L5: monthly save looked up each site separately; now one query.
  - L6: an unchanged tariff edit returned early and skipped the row-version check; now always checked, history written only when something changed (ids compared too, not only names).
  - L7: two concurrent creates of the same keys gave the generic 409; the unique-index violation now maps to `tariff.duplicate`.
  - L9: the quote accepted a partner with an own-fleet driver; refused (`driver.partnerOwnFleet`, DT-48).
- **Accepted / suggestions:**
  - L8: month history lifts all query filters (with an explicit company filter) to show cleared rows; safe, tighter named-filter use later.
  - L10: the FR-034 warning comes after saving; the UI must use `hasTrips`/trip counts from the GET to warn before (step 8).
  - Tie on equal specificity and equal Valid from cannot happen for matching lines (same score + both matching ⇒ same keys ⇒ unique index); the "newest line" tie-break is defensive only.
  - `rate.manualNotAllowed` is used from B5 (trip save); test it there.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B4-01 | Medium | Lost update on the shared monthly grid (M1) | **Fixed** (row version required) |
| SR-B4-02 | Medium | Import query volume could tie up the database for large files (M3) | **Fixed** |
| SR-B4-03 | Low | Partner/own-fleet combination accepted by the quote (L9) | **Fixed** |
| SR-B4-04 | Info | `IgnoreQueryFilters()` in rate history (user names; cleared monthly rows) | Reviewed: constrained to ids from company-filtered history or an explicit company filter; cross-tenant test added |
| SR-B4-05 | Info | Viewers (trips.view / reports.view) can read tariffs and driver rates (DT-50) | Policy decision for owner review |

Isolation (typed tokens through filtered sets, generic cross-tenant test over every endpoint plus a pricing-specific one), permissions (all writes and imports masters.manage), mass assignment (no Status/ids/company in requests), uploads (11 MB, 20,000 rows, formula cells refused), CSV injection on export and history disclosure checked. No new packages.
