# Step 4 · Block B3 — Master data: delivery and review

_Completed 8 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied. Owner accepted DT-21 … DT-35 on 8 Oct 2026._

## What was built (backend only — no UI code before mockups, step 6)

| Area | Implementation | Requirements |
|---|---|---|
| Drivers with truck | `DriverService`: all PRD 7.8.1 fields; national ID 10 digits and plate unique within the company with Arabic digits folded and plate spacing ignored (DT-37); phone and IBAN by the company's market patterns; bank name + IBAN required when paid by bank, CliQ alias when paid by CliQ; capacity > 0; ownership type and optional partner (a partner cannot be linked to an own-fleet driver, DT-48); custom fields; record-lock guard on edits | FR-152, 8.9 |
| Truck history | Every change audited old → new; history endpoint shows ownership type, partner and accounting party by name and custom values per field | FR-153, FR-166 |
| Document alerts | Expired or expiring within 30 days (company time zone): alerts on every driver, list filter `expiry=expired|expiring|alerts`, `/drivers/expiry-alerts` for the dashboard (B10), alerts in the trip-form pick list (B5 warns, save allowed) | FR-154, FR-155 (data) |
| Profiles | Driver, customer and partner profiles = record + reference counts + history; partner profile lists its drivers. Account figures (wages, advances, balances, unbilled trips, open invoices) join in B6/B8 (DT-39) | FR-156…158 (part) |
| Customers, partners | Name unique among **active** records; restore refused while an active record has the name (DT-40); customer email, phone, payment terms 0–3,650 days; optional link to an active receivable party of shared Accounting | FR-157, FR-158 |
| Materials, sites | Materials (unit default ton) and two separate site lists; name unique in its list including archived records; rename by identifier | FR-159, FR-160, FR-163 (rename) |
| Ownership types | Own/External; built-in "Company" cannot be renamed, reclassified or deleted; no archive (no status column, DT-38); a type with partner-linked drivers cannot become own fleet | FR-161, BR-029 |
| Protected deletion | Reference counts per BR-040 table (trips, invoices, receipts, credit notes, tariffs, advances, settlements, drivers, partner payments, monthly rates); refusal "Cannot delete — linked to 4 trips · 1 tariff lines. Archive instead."; soft delete with reason; archive/restore; archived records hidden from pick lists | FR-162, BR-040, BR-041 |
| Lists | Search, status (active/archived/all), white-listed sort, paging; driver filters fleet, ownership type, partner, expiry | FR-165 |
| Import / export | Every list: Excel/CSV export with the company's labels and visible custom fields; import with column matching (Arabic/English/field key/company label, or an explicit `mapping`), preview showing matched and unused columns, duplicates on unique keys in the file and in the company, links by name; strict and add-only (DT-44), one transaction, `cfg.ImportBatches` | FR-164, FR-180 |
| Custom fields | Trips, drivers, customers: Text, Number, Date, List, Checkbox, LongText; labels AR/EN, options, default, help, required, show-as-column, order, hidden; values validated by type, unknown keys refused, default on create, hidden values kept (DT-41); with data → type fixed, used options kept, delete refused (hide offered) | FR-179…181 |
| Standard fields | Catalogue per entity (trip core fields per US-27); rename (one label for both languages), hide, require, reorder; core and business-required fields never hidden; conditional fields (bank name, IBAN, CliQ) never hidden; partner never made always required; required fields block saving with "Field «label» is required"; hidden optional fields keep stored values on edit | FR-182 |
| Form configuration | `/forms/{entity}`: standard + custom fields in order with effective labels and required flags (forms, lists and the preview read it) | FR-180, FR-184 (data) |
| Column views | Personal view (any member) over the company default (settings.manage); reset; 200 views per user | FR-183 |
| Idempotent creates | `clientId` (GUID) becomes the PublicId: a repeated create returns 200 with the first record, a new one 201; a race on the same id returns the record created by the other request | API-05, G-13 |
| Authorization | New `RequireAnyPermission` (driver/partner records: drv.view or masters.manage) and `RequireMember` (lists of customers/materials/sites/ownership types, all pick lists, form configuration) — DT-36 | PRD 3.4 |

100 new endpoints under `/api/v1/companies/{company}/{drivers|customers|partners|materials|loading-sites|unloading-sites|ownership-types|custom-fields|standard-fields|forms|column-views}`, each with permission or member metadata and an OpenAPI summary (test-enforced). OpenAPI exported (182 paths) — Redocly lint: valid, 0 warnings.

**Fixed in B1 code during B3:** the audit interceptor logged every update that went through the row-version check as **"Restore"** (it looked only at the IsDeleted "modified" flag, which marking the entry Modified sets for every column). It now requires IsDeleted to change from 1 to 0. Regression test added. Earlier audit rows from B1/B2 updates on the development database carry "Restore" instead of "Update" (test data only).

Deferred to the blocks that own them: driver/customer/partner account figures (B6, B8); trips keeping fleet/partner/plate at save, the trip-form warning and FR-167 (B5); printed documents keeping issued names (B6 print snapshots); PDF list export (print engine, B6); custom fields in the quick-entry row, invoice annex and report builder (B5, B6, B10); dashboard alert card (B10); live list updates (B11).

## Tests — 181 passing (143 → 181)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 19 cases | Field catalogue invariants and US-27 core list; custom value parsing per type (Arabic digits, dates, yes/no) and rejections; plate/name/phone normalisation; expiry alert boundaries (today, +30, +31, expired) |
| Infrastructure.Tests | 2 | Audit action Update vs Restore (interceptor fix); customer name unique among active only (filtered index) |
| Api.Tests | 17 | Driver uniqueness with Arabic digits, validation by payment method, partner/own-fleet rule and truck history, expiry alerts, list search/filter/sort/paging; reference protection with counts and soft delete; built-in ownership type; customer active-name rule and restore; materials/sites; idempotent create; custom fields; standard fields; column views; customer and driver import/export with mapping; permissions (Viewer, Dispatcher, custom role); review regressions. The generic isolation and endpoint-coverage tests cover all 100 new endpoints |

## Code review — summary (independent reviewer + author)
- **No High findings.**
- **Fixed:**
  - M1: hiding an optional standard field made the next edit erase it (e.g. payment terms → 0). Hidden optional fields now keep their stored values; conditional fields (bank name, IBAN, CliQ alias) cannot be hidden.
  - M2: reclassifying an ownership type to own fleet left partner-linked drivers inconsistent. Refused while such drivers exist (`ownership.fleetInUse`).
  - M3: a customer could be linked to a supplier or archived accounting party. Only active Customer parties are accepted (a stored link stays valid).
  - M4: two identical creates racing on the same clientId returned 409 instead of the first record. The save failure now re-reads and returns it; a client id used elsewhere → 400.
  - L2: export used non-catalogue keys for ownership type/partner (company labels ignored) and exported hidden standard fields. Fixed.
  - L3: a market pattern that times out gave a 500. Treated as "does not match".
  - L5: unbounded custom-value keys and standard-field lists, unlimited personal column views. Capped (50, catalogue size, 200).
  - L6: making the driver's partner always required would block every own-fleet driver. Refused (`field.cannotRequire`).
- **Accepted / suggestions:**
  - L1: the import's in-file duplicate check is case-insensitive but not accent-insensitive, while the database collation is CI_AI. Names differing only in Arabic diacritics pass the preview and then fail the commit with 409 (whole import rolled back, nothing saved). Suggestion: diacritic-folding key in the preview.
  - L4: `rowVersion` is optional on updates (same as B1/B2). Suggestion: make it required on PUT once the UI exists.
  - L7: custom-field list runs one data scan per definition (≤ 50); driver import makes about 4 queries per row. Bounded by the sizes of master data.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B3-01 | Medium | Idempotent create path (M4) could answer 409 for a GUID used in another tenant, confirming that it exists | **Fixed**: 400 `validation.format` on `clientId`, no record data returned |
| SR-B3-02 | Medium | Customer → accounting-party link accepted any party of the company (M3) | **Fixed** |
| SR-B3-03 | Low | Regex from company settings could time out → 500 (L3) | **Fixed**; the 1-second timeout prevents ReDoS |
| SR-B3-04 | Low | Unbounded lists in requests (L5) | **Fixed** (caps) |
| SR-B3-05 | Info | `IgnoreQueryFilters()` in history (user names, linked names) and in the clientId check | Reviewed: constrained to this company's ids or used only for an existence check that returns no data |

Isolation (tokens through filtered sets, per-type tokens, generic cross-tenant test over every endpoint), permissions, mass assignment (no Status/Id/IsBuiltIn/company fields in requests), upload limits (11 MB, 20,000 rows, formula cells refused), CSV injection on export, and history disclosure were checked and found sound. No new packages (dependency scan unchanged).
