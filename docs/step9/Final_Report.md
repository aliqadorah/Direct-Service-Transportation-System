# Direct Service Transportation — Final Report (step 9)

_Prepared 11 Oct 2026. Inputs: `docs/inputs/PRD_v1.2.md`, `Database_Design.md` (revision 4, implemented as revision 5), `Build_Brief.md`, `Build_Prompt.md`. Running the system: `docs/step9/Setup_and_Run.md`._

## 1. What was built

| Part | Where | Summary |
|---|---|---|
| Database | `src/…Infrastructure/Persistence`, `database/` | 66 tables exactly as the design (revision 5: three owner-approved additions — default invoice layout 'Claim', tenant sign-in settings, company status). EF Core migration + idempotent script; verification report with no differences (`docs/step3/02_Design_vs_Database_Verification.md`). Least-privilege application user (`database/90_ApplicationUser.sql`). |
| Business logic + API | `src/…Domain`, `src/…Application`, `src/…Web/Endpoints` | Modules B1–B12 (`docs/step4/B*.md`): identity with mandatory 2FA, tenants/companies/roles, posting engine and accounting, master data, pricing, trips, invoicing and credit notes, national e-invoicing (JoFotara connector + simulator), cash/drivers/partners, AP/AR and financial reports, reports and dashboard, collaboration (live updates, locks, search, notifications, audit log), platform (provider console, subscriptions, lifecycle, data export, erasure). REST API `/api/v1`, OpenAPI 3.1 in `docs/api/openapi-v1.json` (373 paths, 434 operations, Redocly-clean). |
| User interface | `src/…Web.Client`, `src/…Web/Components/Account` | Blazor WebAssembly, Arabic RTL first and English LTR, design direction A · Ledger + C parts (step 5), design system (step 7), every screen SCR-00…94 (step 8). |
| Tests | `tests/` | Domain, Application (unit), Infrastructure (database rules), Api (integration, isolation, security), UI (bUnit page and component tests), `tests/e2e` (Playwright, PRD Section 9 flows per role in Arabic and English, WCAG 2.1 AA scan of every screen). |
| Operations | `Dockerfile`, `.github/workflows/ci.yml`, `docs/step9/Setup_and_Run.md` | Container image (with Chromium for PDFs), CI workflow (build, tests with coverage, OpenAPI lint), staff-account command `create-staff`, hosting notes. Neither the image nor the workflow has been run yet (no container runtime on the development PC; the repository is never pushed, Q-10). |

### Test results (final run, 11 Oct 2026)
| Suite | Result |
|---|---|
| Domain (unit) | 4 / 4 |
| Application (unit, incl. the PRD Section 8 worked examples) | 144 / 144 |
| Infrastructure (database rules, numbering concurrency) | 36 / 36 |
| Api (integration, tenant/company isolation, security) | 127 / 127 |
| UI (bUnit components and pages) | 78 / 78 |
| E2E flows (Section 9 + console, Arabic and English) | 14 / 14 |
| E2E accessibility (WCAG 2.1 AA, axe-core: 3 account pages + 81 screens, Arabic and English) | 0 violations |
| Line coverage (all .NET suites merged) | Domain 100 %, Application 85.2 % (target ≥ 80 %, TST-12…14), Web host 72 % |
| OpenAPI lint (Redocly) | valid |

Build: no warnings (warnings are errors). NuGet audit on every restore (SEC-15 dependency scanning).

### Section 9 flows (TST-03) — Arabic RTL and English

| PRD flow | E2E test |
|---|---|
| 9.1 Sign-up & first-time setup | `flows-01-onboarding` |
| 9.2 Month-start monthly rates | `flows-04-owner-admin-viewer` |
| 9.3 Record a trip (quick entry) | `flows-02-dispatcher` |
| 9.4 Import trips | `flows-06-import-einvoice-lock` |
| 9.5 Edit or void a trip | `flows-02-dispatcher` |
| 9.6 Invoice a customer | `flows-03-accountant` |
| 9.7 E-invoicing submission | `flows-06-import-einvoice-lock` (simulator) |
| 9.8 Credit note or void invoice | `flows-03-accountant` |
| 9.9 Customer receipt | `flows-03-accountant` |
| 9.10 Driver advance & settlement | `flows-03-accountant` |
| 9.11 Partner payment | `flows-07-partner-payment` |
| 9.12 Month-end close | `flows-04-owner-admin-viewer` |
| 9.13 Concurrent work (record lock) | `flows-06-import-einvoice-lock` |
| 9.14 Configure fields | `flows-04-owner-admin-viewer` |
| 5.5 / BR-014 provider console and support access | `flows-05-provider-console` |

Roles exercised: Owner, Administrator, Accountant, Dispatcher, Viewer, platform staff.

## 2. Discrepancies found between the PRD and the database design

All were put to the owner in step 1 (`docs/step1/01_Requirements_Inventory.md` §6–7, answers in `docs/step1/03_Owner_Decisions.md`) and built as decided:

| # | Topic | Decision |
|---|---|---|
| C-01 | Default invoice layout (PRD: Claim; DB: TaxInvoice) | DB default changed to 'Claim' (revision 5) |
| C-02 | Tariff price ≥ 0 vs CHECK > 0 | DB rule (> 0) |
| C-03 | Settlement deduction ≥ 0 vs CHECK > 0 | DB rule (> 0); a zero line is not saved |
| C-04 | Rejected e-invoice: resubmit vs credit note + new invoice | Resubmit the never-accepted document; credit note route for accepted ones |
| C-05 | "Failed" e-invoice state missing in the CHECK | Status Rejected + `LastError`, listed as "Rejected / Failed" |
| C-06 | Editing posted manual entries vs "never edited" | Edit = reversal + new linked entry |
| C-07 | Tariff lock: masters.manage may deviate vs nobody | PRD wording |
| C-08 | Built-in ownership type: no delete/reclassify vs no rename/delete | Both rules |
| C-09 | English account name required vs nullable column | Required by the application |
| C-10 | Partner/bank payment details conditionally required, no PaymentMethod column | Optional, validated when given |
| C-11 | Suspended tenant: view/print/export vs export only | PRD wording |
| G-01, G-02 | Tenant sign-in settings, company archiving — no columns | Columns added (revision 5) |
| G-03…G-21 | Gaps where the PRD is silent (chart template, credits, trial end, staff roles, support scope, setup progress, demo company, quick-entry memory, idempotency, partner tariff breach, partner driver-rate fields, shortage deduction, control party types, tenant deletion, prefixes, terms texts, unnamed screens) | Recorded in `03_Owner_Decisions.md` (D-01…D-18) |

Found later and settled the same way (owner-accepted DT entries in `BUILD_STATUS.md`): invoice prefix INV- instead of TRP- (D-16); buyer tax number sent only when known (DT-86/DT-102); credits reduce only their invoice (D-11).

## 3. Decisions taken without asking

`BUILD_STATUS.md` → "Decisions taken without asking" lists DT-01 … DT-165 with options and the choice. DT-01 … DT-151 are accepted by the owner. **DT-152 … DT-165 are waiting for review**; the step 9 ones are:

| DT | Topic | Choice |
|---|---|---|
| DT-159 | FR-163 names on issued documents (no snapshot columns in the design) | Resolved from the append-only audit log as at the issue time; erased records never resolved |
| DT-160 | FR-183 column views | Column chooser on the Created Trips list (the list with optional columns) |
| DT-161 | Live updates in screens (FR-185, RT-02) | Lists and record views reload on a change (debounced) and highlight changed rows; forms protected by the record lock; reload after reconnect |
| DT-162 | API-07 per-tenant rate limit | 3,000 requests/min per tenant chained after 600/min per user |
| DT-163 | UX-12 filtered-list URLs | Filters, search, sort, page and view written to the address (replace navigation) and restored on load |
| DT-164 | Hosting without a chosen provider | Dockerfile and CI workflow written, not run; forwarded headers only with listed proxies |
| DT-165 | Keyboard access to tables without row links | The list's scroll area is a tab stop |

DT-152 … DT-158 (step 8: support-session companies, data-export package, live layout preview, register footer counts, company currency, support-window lengths, "Test connection" deferred) are described in `BUILD_STATUS.md`.

## 4. Suggestions not implemented (no scope change)

- E-invoicing "Test connection" button (DT-158) — needs the JoFotara test environment (R-01).
- Row versions on operations settings and standard-field configuration (settings review L14; last save wins today).
- Leave-page confirmation on the remaining forms (`Ui/UnsavedGuard` is ready; built on the layout designer and field configuration).
- Field-configuration "default columns" variant and a company-settings history box from the board.
- Chunked tolerant imports beyond 20,000 rows; Arabic diacritic folding in import previews (B3 L1); fewer queries in very large driver imports (B3 L7).
- Pro-rating from the last amount paid instead of the current plan price (B12 F12); refunds through the gateway (DEF-02); enforcing the storage limit (DT-128).
- Rebuilding the driver payable from documents in the reconciliation (B9).
- Column views (FR-183) on lists other than Created Trips; metrics export (NFR-18) beyond logs and health checks.

## 5. Open items for the owner or the hosting step

| Item | Why it is open |
|---|---|
| JoFotara verification (R-01, TST-04, REL-04) | Needs the owner's test credentials; format built from public descriptions of the ISTD guide v1.5 |
| Hosting provider, data residency, TLS, encryption at rest, backups and restore drill (DEF-01, NFR-05/06, SEC-08, TST-07) | Provider not chosen; the image and configuration are provider-neutral |
| Payment gateway and e-mail provider (DEF-02, INT-02/03) | Development implementations in place behind `IPaymentGateway` / e-mail sender |
| Performance and load test 500,000 trips / 25 users (NFR-01/02, TST-05) | The list smoke test passes alone; a load test needs the hosting environment (the development PC is memory-bound) |
| Penetration test before launch (SEC-15) | External |
| Legal check of anonymisation and keeping the audit log (SEC-19, DT-134) | Owner |
| Terms of Service / Privacy texts (G-20) | Placeholders "draft-1" |
| Reference data set and reconciliation after a reference month (TST-08, TST-11) | Needs the owner's reference month |
| ~~Help pages (D-18)~~ | Done 12 Oct 2026: in-app help page per PRD 9.1–9.14 in Arabic and English, linked from the screens (DT-166) |

## 6. Traceability

`docs/step1/02_Traceability_Matrix.md` maps every requirement to tables, code, screens and tests; statuses were re-checked in step 9: **362 done, 18 partial, 5 not started** of 385 rows. Every open row depends on the owner or the hosting step:

- partial — US-13 / 9.7 / INT-01 / SEC-20 (e-invoicing built and tested against the simulator; JoFotara verification needs test credentials), NFR-01 / NFR-02 / TST-05 (500,000-trip list test passes; load test on the hosting environment), NFR-06 / SEC-08 (TLS and isolation done; encryption at rest is a hosting setting), NFR-08 (tested on Edge/Chromium only), NFR-09 (market settings and connector interface in place; a second market not built), NFR-18 (health checks and logs; metrics exporter with the provider), NFR-19 / NFR-22 (CI workflow and image written, not yet run), SEC-15 (dependency audit on; pen test before launch), INT-02 / INT-03 (development gateway and e-mail drop), TST-08 (demo company and the E2E data builder; the full reference set needs the owner's reference month);
- not started — NFR-03 (availability SLA), NFR-05 and TST-07 (backups and restore drill), TST-04 (JoFotara test environment), TST-11 (reconciliation after the reference month).

## 7. Setup and run

See `docs/step9/Setup_and_Run.md` (tools, database, configuration, run, operations commands, tests, E2E, hosting notes).
