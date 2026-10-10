# Direct Service Transportation — Build Status

_Last updated: 10 Oct 2026 (step 6 approved; step 4 · B5 Trips done; next: B6 Invoicing & credit notes, then B7…B12, then steps 7–8)_

## Input files (exact copies of the approved Project docs)
| File | Location in this folder | Project path |
|---|---|---|
| Build brief | `docs/inputs/Build_Brief.md` | `claude/Build_Brief.md` |
| PRD v1.2 (approved for build) | `docs/inputs/PRD_v1.2.md` | `claude/PRD_v1.2.md` |
| Database design, revision 4 | `docs/inputs/Database_Design.md` | `claude/Database_Design.md` |
| Build prompt | `docs/inputs/Build_Prompt.md` | `claude/Build_Prompt.md` |
| Visual DB design | https://claude.ai/artifact/BKNbYXkgCEYo3KVF9JWzZ5 | — |
| Logo | `assets/logo.png` | — |

When the build prompt says "this Project's files", use the copies in `docs/inputs/`.

## Working mode (owner instruction, `docs/step2/02_Owner_Decisions_Step2.md`)
Run steps 3, 4, 7, 8, 9 without stopping. **Stop only for:** step 5 design directions, step 6 mockups (no UI code before approval), chart-of-accounts template approval (D-10), real blockers. New ambiguities: choose the recommended option, record it under "Decisions taken without asking", continue. Quality gates per module (build, all tests green, code review, security review for auth/isolation/money). After each module: update this file and commit locally (never push).

## Build documents
| Step | File |
|---|---|
| 1 | `docs/step1/01_Requirements_Inventory.md`, `02_Traceability_Matrix.md`, `03_Owner_Decisions.md` |
| 2 | `docs/step2/01_Architecture_and_Build_Plan.md` (ADR-01…18, API design, security model, build order), `02_Owner_Decisions_Step2.md` |
| 3 | `docs/step3/01_Database_Implementation.md`, `02_Design_vs_Database_Verification.md` (no differences), `03_Chart_Of_Accounts_Template_DRAFT.md` (**approved 8 Oct 2026**, seeded in B2) |
| Design | `docs/design/Database_Design_Revision5.md` |
| 4 | `docs/step4/B1_Foundation_and_Identity.md`, `docs/step4/B2_Accounting_Core_and_Posting_Engine.md`, `docs/step4/B3_Master_Data.md`, `docs/step4/B4_Pricing.md`, `docs/step4/B5_Trips.md` (delivery, tests, code + security review) |
| 5 | `docs/step5/Design_Directions.md` + `docs/step5/design-directions.html` (three directions; **owner chose A · Ledger + C parts**; https://claude.ai/artifact/JkisN139zLE9tGYCGKmDit) |
| 6 | `docs/step6/Mockups.md` + `docs/step6/index.html` (+ `app.js`, `screens-*.js`; serve `docs/` over HTTP to view locally — `.claude/launch.json` config "mockups") — **waiting for approval** |
| API | `docs/api/openapi-v1.json` (exported OpenAPI 3.1, Redocly-linted with `redocly.yaml`) |

## Completed steps
- **Step 0 — Confirm inputs: done (8 Oct 2026).**
- **Step 1 — Requirements inventory and traceability matrix skeleton: approved (8 Oct 2026).**
- **Step 2 — Architecture and build plan: approved (8 Oct 2026)** with Q-01…Q-10 answers.
- **Step 3 — Database implementation: done (8 Oct 2026).** 66 entities + 66 Fluent configurations, `MasterDbContext` (named filters SoftDelete/Tenant/Company), `AuditSaveChangesInterceptor`, migration `InitialMasterDatabase` (rev. 5) applied to `localhost/DirectServiceMaster`, idempotent script `database/MasterDatabase_Idempotent.sql`, seed (22 permissions, TRIAL plan), verification report with **no differences** (66 tables, 1,410 columns, 302 defaults, 66 PKs, 418 FKs, 111 unique + 141 other indexes, 246 checks), 17 integration tests passing. Commit: see git log "Step 3".

- **Step 4 · B1 — Foundation & identity: done (8 Oct 2026).** Web host (`src/DirectService.Transportation.Web`), Identity on `core.Users` with mandatory 2FA, server sessions, tenant/company context, permission policies, URL tokens, sign-up + company provisioning, users/roles/invitations (BR-001…003, no escalation), tenant & companies (plan limit, archive), company settings (FR-168…171), numbering (UPDLOCK), period guard, record locks + expiry worker, SignalR hub, OpenAPI (`docs/api/openapi-v1.json`, Redocly clean). 85 tests passing (Domain 4, Application 27, Infrastructure 22, Api 32). Code + security review: `docs/step4/B1_Foundation_and_Identity.md` (SR-01 High fixed). Backend only — UI waits for steps 5–6.

- **Step 4 · B2 — Accounting core + posting engine: done (8 Oct 2026).** Posting engine (8.6: keys → accounts, balance, period checked before/after the journal-number lock, control parties, reversal, savepoint → pending postings + worker retry, `IPostingSourceHandler` registry for document modules), chart of accounts (tree rules, approved transport template D-10 with cash box + 13 mappings, Excel import/export), opening balances (difference account during setup), account mapping + banner status, cost centres, cash boxes, bank accounts, periods (pre-close check, close/reopen), manual journal (create, edit = reverse + re-post D-05, void, import), journal and automatic-entries lists, voucher data with amount in words, journal export batches (JSON/CSV). 57 endpoints. 143 tests passing (85 before B2; counts include theory cases). Code review (CR-B2-01 High fixed) + security review (SR-B2-01/02 Medium fixed): `docs/step4/B2_Accounting_Core_and_Posting_Engine.md`. OpenAPI re-exported, Redocly clean.

- **Step 4 · B3 — Master data: done (8 Oct 2026).** Drivers with truck (national ID/plate unique with Arabic digits folded, payment details by method, document-expiry alerts), customers and partners (name unique among active), materials, loading/unloading sites, ownership types (built-in Company protected); reference counts with protected deletion, archive/restore, rename by id, history old → new; import/export of every list (column matching, duplicates, strict); custom fields (trips/drivers/customers), standard-field configuration, form configuration, column views; idempotent creates by `clientId`. 100 endpoints. 181 tests passing. Independent code + security review: no High; M1–M4 and L2/L3/L5/L6 fixed (`docs/step4/B3_Master_Data.md`). Also fixed a B1 audit bug: updates through the row-version check were logged as "Restore". OpenAPI re-exported (182 paths), Redocly clean.

- **Step 4 · B4 — Pricing: done (10 Oct 2026).** Tariffs (customer/partner; keys or All; price > 0 with the currency's decimals; validity; add/edit/archive/restore; unique per keys + start incl. archived; import/export), resolution (specificity 8/4/2/1, tie → latest Valid from) in `PricingRules` + `PricingResolver`, rate history on every change (`RateHistoryLog`, trn.TRN_RateHistory), monthly site rates (month strip None/Partial/Complete, month entry for all active sites, no inheritance, warning when the month has trips, clearing refused while trips use the site, required row versions), missing-rates data for the dashboard, trip-form price quote, FR-030 driver-rate rules ready for B5. 17 endpoints. 208 tests passing. Independent code + security review: no High; M1–M4, L5–L7, L9 fixed (`docs/step4/B4_Pricing.md`). OpenAPI re-exported (196 paths), Redocly clean.

- **Step 4 · B5 — Trips: done (10 Oct 2026).** Create from form/quick entry with 7.1.1 validation, pricing (tariffs, FR-030 driver rates, tariff lock), calculations (`TripCalculator`), warnings, wage/partner-cost posting in the same transaction with a pending-posting handler, edit with reverse + re-post (rate kept unless site/month/driver/fleet change), void, Needs review, Created Trips list with all filters and totals, export, recent trips, quick-entry defaults, strict/tolerant import (xlsx/CSV/TSV/paste), history; monthly rates read WITH (UPDLOCK) via `IMonthlyRateLock`. 12 endpoints. 221 tests (incl. a 500,000-trip list smoke test under 1 s). Independent review: H1, M1–M6, L1/L2/L4–L7 fixed (`docs/step4/B5_Trips.md`). OpenAPI 206 paths, Redocly clean. Invoice prefix changed to INV- (D-16).

## Current step
- **Step 6 — full mockups: approved 10 Oct 2026 as the basis for steps 7–8**, with fixes applied to the board: bidi isolation of codes/numbers, invoice prefix INV-, wider trip-form fields, full names in the quick-entry row on focus/dropdown (`docs/step6/Mockups.md`, board `docs/step6/index.html`, private artifact https://claude.ai/artifact/A9UDCp84GdEqF3qYtmn5Mr): 69 screens (SCR-00…SCR-94) in A · Ledger + C parts, Arabic RTL, five states each, 32 screens with variants. **No UI code before approval.** After approval: step 7 (tokens + Blazor components from the board's CSS), then step 8.
- **Step 4 · B6 — Invoicing & credit notes** (step 2 §10): trip picker, manual invoices, drafts/approval (setting), numbering at approval, posting, void, credit notes with proportional shares (BR-025…027), layouts + designer, print/PDF engine (HTML + headless Chromium, Q-08), QR, amount in words (FR-038…058). Trips: set/clear `InvoiceId` with audit actions on the trip ("Invoiced"/"Released"), lock trip rows WITH (UPDLOCK) while attaching (trip edit needs the row version, DT-73). Then B7 … B12, then steps 7–8.

## Resume here (for a new session)
1. Read this file, then `docs/inputs/Build_Prompt.md` (process), `docs/step2/02_Owner_Decisions_Step2.md` (working mode), `docs/step2/01_Architecture_and_Build_Plan.md` §10 (build order) and `docs/step4/B1_Foundation_and_Identity.md` (patterns in use).
2. Step 6 is approved (with fixes applied). Next backend block: **B6** (see "Current step"); requirements PRD 7.3, 8.3, 9.6–9.8, FR-038…058 rows of the traceability matrix; tables trn.TRN_Invoices, TRN_InvoiceLines, TRN_CreditNotes, TRN_CreditNoteLines, TRN_InvoiceLayouts. Read `docs/step4/B5_Trips.md` (trip service, posting handler pattern). After B12: step 7 (design tokens and reusable Blazor components from the board `docs/step6/`), step 8 (screens), step 9. Backend next block: **B5 Trips** (see "Current step"). Requirements: PRD 7.1, 8.2, 6.1, 9.3–9.5 and the FR-001…018 rows of `docs/step1/02_Traceability_Matrix.md` plus the B5 parts of FR-022…025 and FR-031…033. Read `docs/step4/B4_Pricing.md`: use `PricingResolver.TariffAsync`/`MonthlyRateAsync` and `PricingRules.ResolveDriverRate`/`MustResolveAgain`; **read the monthly rate row WITH (UPDLOCK) inside the trip transaction** (B4 review M2); store tariff price, breach flag, rate/source/month at save (BR-030); partner trips: driver rate 0, RateSource 'Monthly' (DT-54); tariff lock (FR-025) via `PricingResolver.TariffLockAsync`, deviations audited; manual rate needs masters.manage (`rate.manualNotAllowed`).
3. Module pattern (follow B1):
   - Services in `src/…Application/<Module>/` using `IAppDbContext`, `IRequestContext`, `IChangeContext.SetAction(entity, action, reason)` for audited business actions, `db.InTransactionAsync` for multi-save operations, `db.ExpectRowVersion` for updates, `Fail.*` / `Validator` for problems (codes + AR/EN messages in `Contracts/Errors/ErrorCatalog.cs` — a unit test requires every code to have both messages).
   - DTOs in `src/…Contracts/<Module>/` — identifiers are always URL tokens (`IUrlTokenService.Encode<T>`), never internal ids.
   - Endpoints in `src/…Web/Endpoints/` under the `/companies/{company}` group with `.RequirePermission(PermissionKeys.X)` (or member metadata); add every route to `Web/Pipeline/ApiSummaries.cs` (test-enforced) and register the services in `Application/DependencyInjection.cs`.
   - Use `GrantGuard`, `IPeriodGuard`, `INumberingService` (inside a transaction), `IRecordLockGuard` where the PRD requires.
   - Financial documents (B5 trips, B6 invoices/credit notes, B8 cash documents, B9 AP/AR) post through `PostingEngine.PostOrPendAsync` inside their transaction (`PostingLine.DebitKey/CreditKey` with `PostingKeys.*`, cash/bank by `AccountId`), reverse with `ReverseAsync`, and register an `IPostingSourceHandler` (rebuild entry for retries, attach `JournalEntryId`, describe number + token for links). Parties: `PostingParty("TRN","Customer"|"Driver"|"Partner", id)`. Invoicing must call `PostingEngine.EnsurePostingReadyAsync` (FR-094).
   - Imports: `ISpreadsheetService` + `ImportProblems` / `ImportParse` (`Application/Accounting/ImportSupport.cs`), strict preview/commit, `cfg.ImportBatches` row.
   - Master-data style (B3): `MasterDataSupport` (find by token, `ChooseAsync` for active links, `SaveCreateAsync` for idempotent creates with `clientId`, list status/sort/paging, reference counts + `EnsureUnreferenced`, history from the audit log, market phone/IBAN checks, company-local `TodayAsync`); `StandardFieldService.RulesAsync(entity)` → `FieldRules` (labels, required, hidden → `Keep` stored value); `CustomFieldService.ValidateValuesAsync` for CustomValues (trips will use FieldCatalog.Trip). Avoid public instance methods named `BindAsync`/`TryParse` on services: minimal APIs treat them as parameter binders and every endpoint fails at start-up.
   - Tests: unit (Application.Tests), DB rules (Infrastructure.Tests), API flows + isolation (Api.Tests — the generic isolation and coverage tests pick up new endpoints automatically).
   - After the block: code review + security review (money modules), all tests green, export + lint OpenAPI, `python tools/trace/set_status.py partial|done <IDs>`, a `docs/step4/B<n>_*.md` report, update this file, commit (never push).
4. Pitfalls already met: never edit files containing Arabic with PowerShell `Get-Content/Set-Content` (use the Edit/Write tools); `sqlcmd` needs `-I`; do not re-run `tools/design/gen_efcore.py` (it would overwrite hand-maintained entities/configurations); `UserManager.Set*` persists the user (do not call on unsaved users); creating an authenticator key changes the security stamp.
5. Stops still required: step 5 design directions, step 6 mockups (no UI code before approval), real blockers. (D-10 chart template: approved and seeded in B2.)
6. Shell pitfall: multi-line `python - <<'EOF'` heredocs containing quotes sometimes fail in the Bash tool ("unexpected EOF"); write the script to the scratchpad with the Write tool and run it. `sed -i` in Git Bash turns CRLF files into LF — restore CRLF afterwards (source files are CRLF).
7. OpenAPI export: `dotnet run --project src/DirectService.Transportation.Web --launch-profile DirectService.Transportation.Web` (the only profile; any other name silently runs as Production without a connection string), then `curl -sk https://localhost:52233/openapi/v1.json -o docs/api/openapi-v1.json` (HTTP redirects to HTTPS); stop the host afterwards.

## How to run (development)
- API host: `dotnet run --project src/DirectService.Transportation.Web` (Development; needs user-secret `UrlTokens:Key` — set on this machine). Health: `/health/ready`; OpenAPI: `/openapi/v1.json`; API reference: `/scalar` (Development only).
- Tests: `dotnet test` (needs local SQL Server; creates `DirectServiceMaster_Tests` and `DirectServiceMaster_ApiTests`).
- OpenAPI lint: export `/openapi/v1.json` to `docs/api/openapi-v1.json`, then `npx @redocly/cli lint docs/api/openapi-v1.json`.

## Environment
- Owner's computer (Windows, `D:\Direct Service Transportation System`). .NET SDK 10.0.401 (`global.json` pins 10.0.401, roll forward latestFeature; tests use Microsoft.Testing.Platform); `dotnet-ef` 10.0.12 global tool; SQL Server 2025 Developer on `localhost` (Windows auth); Node.js 24.20.0 LTS + npm 11.19.0 (installed 8 Oct 2026 via winget); git 2.56 — local repository `main`, author "Direct Service Company <info@directservicetech.com>" (repository-local config), never pushed.
- Databases: `DirectServiceMaster` (development; collation `Arabic_100_CI_AI_SC_UTF8`, READ_COMMITTED_SNAPSHOT ON); `DirectServiceMaster_Tests` (recreated by the integration tests on every run).
- `sqlcmd` needs `-I` (QUOTED_IDENTIFIER ON) for any DML on tables with filtered indexes.
- Packages managed centrally in `Directory.Packages.props`; warnings are errors (`Directory.Build.props`); NuGet audit on every restore (SEC-15).

## Decisions made
- All items listed as settled in `Build_Brief.md`; `core.Users.Email` unique platform-wide.
- Step 1 decisions D-01…D-18 (`docs/step1/03_Owner_Decisions.md`): D-01 namespaced permission keys; D-02 LayoutType default 'Claim'; D-03 tariff price / deduction > 0; D-04 rejected e-invoices resubmittable, Failed = Rejected + LastError; D-05 manual-entry edit = reverse + re-post; D-06 tariff lock lets masters.manage deviate; D-07 C-08…C-11 recommendations; D-08 tenant settings columns; D-09 Companies.Status; D-10 chart template drafted for approval; D-11 credits reduce only their invoice; D-12 TRIAL plan, trial end → Suspended → Cancelled; D-13 provider console for all staff, support window read-only; D-14 G-recommendations; D-15 AR documents e-invoiced; D-16 prefixes INV- (was TRP-, changed 10 Oct 2026), CRN-, RCV-, ADV-, STL-, PPY-, JV-, BIL-, ARD-, PAY-, RTN-, JEX- (5 digits); D-17 assumptions accepted; D-18 help pages at the end.
- DT-21 … DT-35 (B2 decisions taken without asking): **accepted by the owner on 8 Oct 2026.**
- DT-36 … DT-49 (B3 decisions taken without asking): **accepted by the owner on 10 Oct 2026.** DT-50 … DT-64 (B4): **accepted by the owner on 10 Oct 2026.** DT-65 … DT-69 (step 6): **accepted by the owner on 10 Oct 2026.**
- **Step 5 design direction (owner, 10 Oct 2026): A · Ledger**, adding from C: saved views above lists, the filter row under table headers, the Ctrl+K command bar for global search, and the compact/comfortable density switch.
- Step 2 decisions Q-01…Q-10 (`docs/step2/02_Owner_Decisions_Step2.md`): architecture approved; revision 5 option R5-A; idle timeout 30 min (5–1,440); absolute session lifetime 12 h (platform setting); password length 8–64; only Active companies count toward the plan; collation `Arabic_100_CI_AI_SC_UTF8`; PDF via HTML + headless Chromium; build order as proposed; git + Node.js yes, commit locally after each module, never push.

## Database design revision 5 (implemented)
See `docs/design/Database_Design_Revision5.md`: `TRN_Invoices.LayoutType` default 'Claim'; `core.Tenants` PasswordMinLength / PasswordRequireLetter / PasswordRequireDigit / SessionIdleTimeoutMinutes; `core.Companies.Status`.

## Decisions taken without asking (for owner review)
| # | Topic | Options considered | Chosen |
|---|---|---|---|
| DT-01 | Names of the 300 standard foreign keys (design lists them without names) | (a) design convention `FK_<Table>_<RefTable>_<Column>`; (b) EF default names | (a), e.g. `FK_TRN_Trips_Users_CreatedBy` |
| DT-02 | Names of multi-column check constraints | (a) `CK_<Table>_<Col1>_<Col2>…` per convention; (b) short descriptive names | (a), e.g. `CK_TRN_Trips_VoidReason_VoidedBy_VoidedAt`, `CK_<Table>_DeletedAt_DeletedBy_DeleteReason` |
| DT-03 | ISJSON check on JSON columns whose rule cell does not say CHECK (`TRN_InvoiceLayouts.Labels/Signatures`, `CustomFieldDefinitions.Options`) | (a) add (design §3.3 + build prompt: every JSON column has an ISJSON check); (b) omit | (a). E-invoice `RequestPayload/ResponsePayload` ("JSON/XML text") have no ISJSON check because UBL is XML |
| DT-04 | "Required when …" rules without "(CHECK)" (e.g. `TRN_Invoices.ApprovedBy`, `SubscriptionInvoices.PaidAt`, `ApArEInvoiceSubmissions` document pairing, `Accounts.ControlPartyType` required for control accounts) | (a) application rules only, as the design marks them; (b) add database checks | (a) — enforced by the services; candidates for extra checks later if wanted |
| DT-05 | EF Core's automatic index per foreign key (~300 unlisted indexes) | (a) disable — only the design's indexes; (b) keep and list as extras | (a) |
| DT-06 | EF migrations history table | (a) `dbo.__EFMigrationsHistory` (EF infrastructure, outside the design schemas); (b) inside `core` | (a) |
| DT-07 | `TRN_Tariffs.Specificity` persisted computed column (A-07) | expression wrapped in `ISNULL(…, 0)` so SQL Server infers NOT NULL as designed | as stated |
| DT-08 | TRIAL plan limits (placeholders, D-12) | — | Price 0, Monthly, MaxCompanies 2, MaxActiveUsers 5, MaxTripsPerMonth 2,000, MaxStorageMb 1,024, IsActive 0 (not for sale). Owner edits in the provider console |
| DT-09 | Permission `GroupName` and Arabic descriptions | — | English group names from PRD 3.4 groups; Arabic descriptions translated from the PRD "Grants" text (`ReferenceDataSeed.cs`) |
| DT-10 | Database defaults vs EF insert behaviour | (a) defaults declared in the database, but the application always sends the value (`ValueGeneratedNever`), entity initialisers mirror the design defaults; (b) rely on database defaults (EF "sentinel" problem: a `bool` defaulting to 1 could never be inserted as 0) | (a) |
| DT-11 | READ_COMMITTED_SNAPSHOT | (a) ON (readers never block writers; 25 concurrent users); (b) server default | (a) — in `00_CreateDatabase.sql`, local and test databases. Numbering still uses `UPDLOCK, ROWLOCK` |
| DT-12 | Audit rows of inserted records need their identity values | (a) audit rows saved right after the main save inside the same transaction (the interceptor starts one if the caller has not); (b) a separate transaction | (a) — the document and its audit rows commit or roll back together |
| DT-13 | Rows not written to `core.AuditLogs` | `core.UserSessions` (LastSeenAt every minute) and `cfg.RecordLocks` (heartbeats) — technical, high-frequency, not business events; sign-ins/sign-outs are audited explicitly by the identity services | as stated |
| DT-15 | Names of preset roles and of the built-in ownership type (stored values) | (a) fixed English codes (`Owner`, `Administrator`, `Accountant`, `Dispatcher`, `Viewer`; ownership type `Company`) translated by the UI; (b) Arabic text in the database | (a) — presets cannot be renamed (FR-173, C-08); the UI shows them in the user's language |
| DT-16 | Notification text for "invitation accepted" (FR-190) | Bilingual message stored in `core.Notifications.Message` ("… قبل الدعوة / accepted the invitation") until the notifications module (B11) renders per language | as stated |
| DT-17 | Tenant-level permission (core.tenant.manage) on `/api/v1/tenant/*` | (a) held in at least one active company of the tenant; (b) a separate tenant role | (a) — no schema change; Owner preset holds it |
| DT-18 | Privilege boundaries in user administration (security review SR-01) | (a) a caller can grant only keys it holds; only tenant.manage holders act on Owner-level users; (b) PRD wording only ("users.manage can … assign roles") | (a) — prevents an Administrator from making itself Owner; the PRD does not allow escalation explicitly or implicitly |
| DT-19 | Archived company visibility | Visible (read-only) only to users holding core.tenant.manage; others get 404 | as stated (D-09: "only the Owner sees it") |
| DT-20 | Authenticator code replay within its 30 s window (SR-04) | (a) accept ASP.NET Identity default for now; (b) store the last used time-step (needs a column — schema change) | (a) — listed as a Suggestion in the B1 review |
| DT-21 | FR-094 "posting disabled" scope | (a) automatic posting and invoicing only; (b) also manual and opening entries | (a) — manual and opening entries name their accounts; the opening entry needs the difference key only when unbalanced |
| DT-22 | Date of reversal entries (voids, edits) | (a) the original entry's date; (b) the void date | (a) — the void/edit nets to zero in the original's period; voiding is refused when that period is closed (FR-133) |
| DT-23 | "Until … the first period is closed" (FR-110) and reopen stamps | (a) setup ends once any period has ever been closed; ClosedBy/ClosedAt record the latest close or reopen; (b) only while a period is currently closed | (a) |
| DT-24 | Opening balances on control accounts (no party column for openings) | (a) opening entry lines carry no party; the pre-close check lists them until allocated to parties by manual entries; (b) refuse openings on control accounts | (a) — FR-106 targets manual journal lines |
| DT-25 | Cash box / bank ledger account | Each needs its own active, postable, non-control Asset leaf account, not shared with another cash box or bank account ("one account per cash box / bank account", 7.5.1) | as stated |
| DT-26 | Chart import and existing codes ("preview rejects duplicate codes") | (a) duplicates within the file and codes already in the chart are rejected; import adds new accounts only; (b) update existing accounts | (a) — openings of existing accounts are edited on the opening-balances screen |
| DT-27 | Party identification in the journal import | Natural keys: customer name, driver national ID, partner name, accounting-party name (type "Supplier" = role Supplier); ambiguous keys rejected | as stated |
| DT-28 | Edit history link for manual entries (D-05) — no column links the corrected entry to the original | Audit rows: original "Reverse" (reason = reversal number), corrected entry "Edit" (reason "JV-x → JV-y"); the reversal links both ways in the table | as stated |
| DT-29 | Pre-close check contents (FR-134) | Pending postings = all open ones (they carry no date); unapproved drafts = TRN invoices in Draft dated in the month; unreconciled = control-account amounts without party up to month end (full party reconciliation RPT-23 in B9) | as stated |
| DT-30 | Period rules | Months without a row are Open; closing a future month is refused; close/reopen need settings.manage + reason | as stated |
| DT-31 | Journal export content | All entries of the period range (posted, reversed, void, reversals); entries point to the latest batch; re-export makes a new batch; the file is returned when created | as stated |
| DT-32 | Permissions of B2 screens | Mapping and periods: core.settings.manage; journal list: acc.acct.view; automatic-entries list: acc.je.view (automatic entries only); retry and export: acc.je.export; account-status banner: every company member | as stated (PRD 3.4) |
| DT-33 | Pending-posting worker | Every 5 min, back-off 2^attempts min (max 60), oldest first, skips suspended tenants and archived companies; acts as a system job | as stated |
| DT-34 | Account changes once entries exist | Changing type, control flag or control party type is refused once the account has entry lines; deactivation refused with openings, links or active children | as stated |
| DT-35 | Reversal entries of manual entries | Cannot be edited or voided themselves (CR-B2-01) | as stated |
| DT-36 | Read access to master data (PRD 3.4 has no masters.view) | (a) driver and partner records need drv.view or masters.manage; customer, material, site and ownership-type lists, every pick list ("options") and the form configuration are open to company members; writes masters.manage; field configuration and company default column views settings.manage; personal column views any member; (b) masters.manage for all reads | (a) — every role needs names to read trips, invoices and reports; personal data (national ID, IBAN) stays behind drv.view |
| DT-37 | Normalisation of keys | Names, national IDs, phones, IBANs, plates stored with ASCII digits; plates upper case without spaces; phones/IBANs without spaces or dashes; uniqueness checked on the stored values (collation CI_AI) | as stated |
| DT-38 | Ownership types have no Status column | Not archived; deleted (soft) only while unreferenced; built-in Company cannot be renamed, reclassified or deleted (design + FR-161) | as stated |
| DT-39 | Profile account figures (FR-156…158) | Filled when the documents exist (B6 invoices/credit notes, B8 receipts/advances/settlements/partner payments); B3 profile = record + reference counts + history | as stated |
| DT-40 | Restoring an archived customer/partner whose name an active record now uses | Refused (duplicate.key) until one is renamed | as stated |
| DT-41 | Custom values storage | Only non-empty values stored; hidden fields keep stored values (client value ignored); unknown keys refused; default applied on create when absent; "has data" = a non-deleted record stores the key; FieldKey fixed after creation | as stated |
| DT-42 | Deleting a custom field | Without data → soft delete with reason; with data → refused, hide offered (flow 9.14); a used List option cannot be removed | as stated |
| DT-43 | Standard-field rules | Core trip fields (US-27) and business-required fields cannot be hidden or made optional; conditional fields (driver bank name, IBAN, CliQ alias) cannot be hidden; partner cannot be made always required; optional fields can be renamed, hidden, required, reordered; hidden optional fields keep their stored values on edit; one replacement label (design has one Label column) shown in both languages | as stated |
| DT-44 | Master-data imports | Strict (any bad row blocks) and add-only (existing keys = duplicates), like DT-26; column matching by header aliases (Arabic, English, field key, company label) or an explicit `mapping` (field key → header); links (ownership type, partner) by name, active only | as stated |
| DT-45 | PDF export of lists (FR-165) | With the print engine in B6; Excel/CSV now | as stated |
| DT-46 | Driver payment details when Cash | Bank/IBAN/CliQ kept as entered and format-checked when given; required only for their method | as stated |
| DT-47 | Expiry alert window | Expired = before today; expiring = today … today + 30 days inclusive; "today" in the company time zone | as stated |
| DT-48 | Partner on an own-fleet driver | Refused (a partner's truck is external fleet, BR-029); an ownership type with partner-linked drivers cannot become own fleet | as stated |
| DT-49 | Idempotent create (API-05) | Implemented from B3 on: `clientId` GUID → PublicId; repeat → 200 with the record, new → 201; a client id that exists outside the company → 400 | as stated |
| DT-50 | Read access to pricing (PRD 3.4 has no pricing view key) | (a) tariffs, monthly rates, their history and exports readable with masters.manage, trips.view, trips.edit or reports.view; quote with trips.edit or masters.manage; missing-rates warning any member; writes/imports masters.manage; (b) masters.manage only | (a) — trip viewers see prices and wages on trips anyway; the breach report (reports.view) needs tariffs |
| DT-51 | Tariff delete | (a) archive/restore only (FR-019 lists archive; nothing references tariffs); (b) soft delete too | (a) — archived lines stay in the design's unique index, so an identical line is edited or restored instead |
| DT-52 | Tariff edit semantics | (a) edit in place, old → new in TRN_RateHistory and core.AuditLogs; party type, keys and dates editable; (b) new version row per change | (a) — trips store their prices, so edits never alter saved trips (FR-026) |
| DT-53 | Month-strip status | A site counts as rated with at least one of the two rates (design allows one); Complete = every active loading site rated; Partial = some; None = none (or no active site) | as stated |
| DT-54 | Driver rate of partner trips | 0 regardless of the monthly table (7.1.1, FR-005); stored RateSource 'Monthly' (CHECK allows Monthly/Manual; nothing was typed) | as stated (applies in B5) |
| DT-55 | Typed driver rate without masters.manage | No monthly rate → blocked with the missing-rate message (FR-030 "otherwise"); differs from the monthly rate → refused `rate.manualNotAllowed` (BR-004) | as stated (applies in B5) |
| DT-56 | Dates used by pricing | Tariff validity checked on the trip date (FR-020); rate month = loading date's month (BR-029); the quote uses one for the other when only one is given | as stated |
| DT-57 | Editing rates of past or closed months | Allowed (rates are configuration and never alter saved trips; trips cannot be posted into closed periods anyway) | as stated |
| DT-58 | FR-034 "warns" | Non-blocking: GET shows `hasTrips` and trip counts per site so the UI warns before saving; the save response repeats the warning. "Trips" = non-void trips with that rate month | as stated |
| DT-59 | FR-035 "clearing both rates" | Both empty → soft delete with reason "Rates cleared", refused while non-void trips of that site use the month; removing one of two rates is allowed | as stated |
| DT-60 | Rate history format | Readable snapshots (names at the time, amounts invariant, All = empty) + action (Create/Update/Archive/Restore/Clear) and subject; RateHistory rows are not copied into core.AuditLogs (the tariff/rate rows are audited there) | as stated |
| DT-61 | Decimals of prices and rates | Company currency decimals (3 for JOD; PRD 7.2.1 "Money, 3 decimals"), although the columns hold 4 | as stated |
| DT-62 | Archived loading sites in the month grid | Keep and show their rates in months where they have them; get no new rates; the grid lists active sites | as stated |
| DT-63 | "Save and print" of monthly rates (FR-028) | Excel/CSV now; PDF with the print engine in B6 (like DT-45) | as stated |
| DT-64 | Concurrency on the monthly grid (B4 review M1) | Changing or clearing stored rates requires the row version (400 missing, 409 stale) | as stated — stricter than other PUTs (B3 L4 still open for those) |
| DT-65 | Digits and dates in the Arabic UI | Western digits isolated as LTR runs; default date format YYYY-MM-DD (user preference can change it, FR-178) | as stated |
| DT-66 | Menu placement of setup checklist, tenant group, provider console | Setup checklist top-level while setup is incomplete; Tenant group only for core.tenant.manage; provider console in its own shell | as stated |
| DT-67 | Default table density | Compact (32 px rows / 32 px controls); comfortable 40 / 36 px | as stated |
| DT-68 | Storage of saved views (owner chose C's saved views; no table in the design) | (a) rows of cfg.TableColumnViews under namespaced keys `<list>#view:<name>`, Columns JSON = columns + filters + name; personal (UserId) or company-shared (UserId NULL, settings.manage); built-in views defined in code; (b) new table (schema change) | (a) — no schema change |
| DT-69 | Storage of the density preference (no column in core.Users) | (a) per device in browser storage; (b) new column (schema change) | (a) — shown in the profile as "saved on this device" |
| DT-65 (amended) | Date format default | The database design defaults `core.Users.DateFormat` to `dd/MM/yyyy`; that default stays (design contract). The board's YYYY-MM-DD dates are illustrative; each user chooses the format (FR-178) | as stated |
| DT-70 | Quick-entry memory (FR-007) — no column in the design | Driver, customer, material and route of the user's last saved trip (works across sessions and devices, no schema change) | as stated |
| DT-71 | Partner on imported rows without a partner cell | The driver's partner, as the form pre-fills it (7.1.1); a tolerant import with an unknown partner also falls back to it and flags the row | as stated |
| DT-72 | Strict vs tolerant trip import (FR-013) | Strict: any rejected row blocks the import. Tolerant: invalid optional values (unloaded weight, unloading/loading date, partner, custom values) take defaults and flag Needs review with the fields named; rows with errors in required values are skipped and reported | as stated |
| DT-73 | Row version on trip changes | Required on edit, void and clear-review (400 missing, 409 stale), so invoicing/settling meanwhile refuses stale changes | as stated |
| DT-74 | Re-resolution on edit (FR-032, FR-026) | When site/month/driver/fleet (rate) or customer/material/sites/date (tariff) change, a rate or price resent unchanged by the form counts as not typed and the new value applies; a different typed value follows the manual/lock rules | as stated |
| DT-75 | Trip list search (FR-009 "search") with 500,000 trips (NFR-01) | Any part of the receipt number; drivers (with plates), customers, partners, sites and materials by their filters; plates and names also through the Ctrl+K global search (FR-187, B11) | as stated |
| DT-76 | Loading date in a closed period | Refused, as PRD 8.9 requires for trip and loading dates (7.1.1 lists it only for the trip date) | as stated |
| DT-77 | Amounts visible to trips.view | Trip amounts (wage, partner cost, margin) are part of the trip a Viewer may view (PRD 3.4) | as stated |
| DT-78 | Edit re-posting | The wage/partner-cost entry is reversed and re-posted only when what it posts changes (amount, party, date, receipt) | as stated |
| DT-14 | Audit JSON for secret columns | `PasswordHash`, `SecurityStamp`, `TwoFactorSecret`, `CodeHash`, `TokenHash`, `ClientIdEncrypted`, `SecretKeyEncrypted` written as `"***"`; binary columns (logo) omitted | as stated |

## Extra indexes / constraints added beyond the design
- None. (The design's indexes only; ISJSON checks per DT-03 are required by the design's JSON convention.)

## Raw SQL used (where EF Core cannot express something)
| File | Why |
|---|---|
| `database/00_CreateDatabase.sql` | Create the database with collation `Arabic_100_CI_AI_SC_UTF8` and READ_COMMITTED_SNAPSHOT (EF applies the model collation only when it creates the database itself, not in a migration script) |
| `database/90_ApplicationUser.sql` | Application database user `dst_app`: read/write, `DENY DELETE` on core/acc/cfg/trn, `DENY UPDATE` on `core.AuditLogs`, `core.MaintenanceLogs`, `trn.TRN_RateHistory`, `DENY ALTER` on the schemas (verified 8 Oct 2026 on the test database) |
| `tests/…/DatabaseFixture.cs` | `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` on the disposable test database |
| `Infrastructure/Persistence/MonthlyRateLock.cs` | `SELECT … FROM trn.TRN_MonthlySiteRates WITH (UPDLOCK, ROWLOCK)` inside the trip transaction (trip save and import), so clearing rates and saving trips serialise (FR-035, B4 review M2) |
| `Infrastructure/Persistence/NumberingService.cs` | `SELECT … FROM cfg.NumberSequences WITH (UPDLOCK, ROWLOCK)` inside the document transaction (BR-038); the increment uses EF `ExecuteUpdate`. `LockAsync` (B2) takes the same lock without issuing a number, so closing a period waits for postings in progress (BR-036) |
| Planned (B7, B10) | E-invoice counter read `WITH (UPDLOCK, ROWLOCK)`; worker queue claims `WITH (UPDLOCK, READPAST)` |

## Tools (development only)
`tools/design/parse_design.py` (design → model), `checks.py` (check constraints), `gen_efcore.py` (one-off source generation — do not re-run over hand-edited files), `verify_database.py` + `catalog.sql` (verification report), `selftest_verify.py`.

## Open issues
- B5: tolerant imports run in one transaction (bounded by 20,000 rows) — chunking is a candidate; breach amount in responses uses 3 decimals (revisit for markets with other decimals); ambiguous driver names in imports reported as unknown.
- B4: trip save (B5) must read the monthly rate row with an update lock so clearing rates and saving a trip serialise (review M2); breach report RPT-04 (B10); monthly-rate PDF (B6); dashboard missing-rates card (B10); the UI must warn before saving a month with trips (L10).
- B3 review suggestions not built: import preview should fold Arabic diacritics like the CI_AI collation (L1); make `rowVersion` required on PUT once the UI exists (L4); per-row queries in large driver imports (L7).
- Development database: audit rows of B1/B2 updates written before the interceptor fix carry Action "Restore" instead of "Update" (test data only).
- Posting source handlers to register in B5/B6/B8/B9 (`IPostingSourceHandler`); until then pending postings of those types report "cannot be re-posted in this version".
- Voucher PDF / journal-list PDF with the print engine (B6); financial reports, statements, reconciliation RPT-23 and cost-centre report (B9/B10); dashboard cards for pending postings and opening difference (B10).
- System-job soft deletes: `DeletedBy` is required by CHECK, but the tenant-retention job (G-18) has no acting user. To be decided in B12 (likely a seeded platform "system" account); record-lock expiry will attribute the release to the lock holder.

## Deferred (provider-neutral interfaces with local/dev implementations)
- Hosting provider and data residency (DEF-01) — incl. SignalR scale-out backplane, encryption at rest (TDE), backups.
- Payment gateway (DEF-02).
- Email provider (DEF-02).
- Legal check on anonymising personal data (owner handles).
