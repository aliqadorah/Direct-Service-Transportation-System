# Direct Service Transportation — Build Status

_Last updated: 8 Oct 2026 (step 4 · B2 done; next: B3 Master data)_

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
| 4 | `docs/step4/B1_Foundation_and_Identity.md`, `docs/step4/B2_Accounting_Core_and_Posting_Engine.md` (delivery, tests, code + security review) |
| API | `docs/api/openapi-v1.json` (exported OpenAPI 3.1, Redocly-linted with `redocly.yaml`) |

## Completed steps
- **Step 0 — Confirm inputs: done (8 Oct 2026).**
- **Step 1 — Requirements inventory and traceability matrix skeleton: approved (8 Oct 2026).**
- **Step 2 — Architecture and build plan: approved (8 Oct 2026)** with Q-01…Q-10 answers.
- **Step 3 — Database implementation: done (8 Oct 2026).** 66 entities + 66 Fluent configurations, `MasterDbContext` (named filters SoftDelete/Tenant/Company), `AuditSaveChangesInterceptor`, migration `InitialMasterDatabase` (rev. 5) applied to `localhost/DirectServiceMaster`, idempotent script `database/MasterDatabase_Idempotent.sql`, seed (22 permissions, TRIAL plan), verification report with **no differences** (66 tables, 1,410 columns, 302 defaults, 66 PKs, 418 FKs, 111 unique + 141 other indexes, 246 checks), 17 integration tests passing. Commit: see git log "Step 3".

- **Step 4 · B1 — Foundation & identity: done (8 Oct 2026).** Web host (`src/DirectService.Transportation.Web`), Identity on `core.Users` with mandatory 2FA, server sessions, tenant/company context, permission policies, URL tokens, sign-up + company provisioning, users/roles/invitations (BR-001…003, no escalation), tenant & companies (plan limit, archive), company settings (FR-168…171), numbering (UPDLOCK), period guard, record locks + expiry worker, SignalR hub, OpenAPI (`docs/api/openapi-v1.json`, Redocly clean). 85 tests passing (Domain 4, Application 27, Infrastructure 22, Api 32). Code + security review: `docs/step4/B1_Foundation_and_Identity.md` (SR-01 High fixed). Backend only — UI waits for steps 5–6.

- **Step 4 · B2 — Accounting core + posting engine: done (8 Oct 2026).** Posting engine (8.6: keys → accounts, balance, period checked before/after the journal-number lock, control parties, reversal, savepoint → pending postings + worker retry, `IPostingSourceHandler` registry for document modules), chart of accounts (tree rules, approved transport template D-10 with cash box + 13 mappings, Excel import/export), opening balances (difference account during setup), account mapping + banner status, cost centres, cash boxes, bank accounts, periods (pre-close check, close/reopen), manual journal (create, edit = reverse + re-post D-05, void, import), journal and automatic-entries lists, voucher data with amount in words, journal export batches (JSON/CSV). 57 endpoints. 143 tests passing (85 before B2; counts include theory cases). Code review (CR-B2-01 High fixed) + security review (SR-B2-01/02 Medium fixed): `docs/step4/B2_Accounting_Core_and_Posting_Engine.md`. OpenAPI re-exported, Redocly clean.

## Current step
- **Step 4 · B3 — Master data** (step 2 §10): drivers with truck, customers, partners, materials, sites, ownership types; archive/restore, reference counts, rename, history, import/export, custom fields on drivers/customers, standard-field config (FR-152…167, FR-179…183).
- Waiting (the build will stop there): **step 5 design directions** — not started yet; backend blocks continue first.

## Resume here (for a new session)
1. Read this file, then `docs/inputs/Build_Prompt.md` (process), `docs/step2/02_Owner_Decisions_Step2.md` (working mode), `docs/step2/01_Architecture_and_Build_Plan.md` §10 (build order) and `docs/step4/B1_Foundation_and_Identity.md` (patterns in use).
2. Next block: **B3** (see "Current step"). Requirements: PRD 7.8, 7.9.3, 8.8, 8.9 and the FR-152…167, FR-179…183 rows of `docs/step1/02_Traceability_Matrix.md`. Read `docs/step4/B2_*.md` for the accounting services that later blocks call.
3. Module pattern (follow B1):
   - Services in `src/…Application/<Module>/` using `IAppDbContext`, `IRequestContext`, `IChangeContext.SetAction(entity, action, reason)` for audited business actions, `db.InTransactionAsync` for multi-save operations, `db.ExpectRowVersion` for updates, `Fail.*` / `Validator` for problems (codes + AR/EN messages in `Contracts/Errors/ErrorCatalog.cs` — a unit test requires every code to have both messages).
   - DTOs in `src/…Contracts/<Module>/` — identifiers are always URL tokens (`IUrlTokenService.Encode<T>`), never internal ids.
   - Endpoints in `src/…Web/Endpoints/` under the `/companies/{company}` group with `.RequirePermission(PermissionKeys.X)` (or member metadata); add every route to `Web/Pipeline/ApiSummaries.cs` (test-enforced) and register the services in `Application/DependencyInjection.cs`.
   - Use `GrantGuard`, `IPeriodGuard`, `INumberingService` (inside a transaction), `IRecordLockGuard` where the PRD requires.
   - Financial documents (B5 trips, B6 invoices/credit notes, B8 cash documents, B9 AP/AR) post through `PostingEngine.PostOrPendAsync` inside their transaction (`PostingLine.DebitKey/CreditKey` with `PostingKeys.*`, cash/bank by `AccountId`), reverse with `ReverseAsync`, and register an `IPostingSourceHandler` (rebuild entry for retries, attach `JournalEntryId`, describe number + token for links). Parties: `PostingParty("TRN","Customer"|"Driver"|"Partner", id)`. Invoicing must call `PostingEngine.EnsurePostingReadyAsync` (FR-094).
   - Imports: `ISpreadsheetService` + `ImportProblems` / `ImportParse` (`Application/Accounting/ImportSupport.cs`), strict preview/commit, `cfg.ImportBatches` row.
   - Tests: unit (Application.Tests), DB rules (Infrastructure.Tests), API flows + isolation (Api.Tests — the generic isolation and coverage tests pick up new endpoints automatically).
   - After the block: code review + security review (money modules), all tests green, export + lint OpenAPI, `python tools/trace/set_status.py partial|done <IDs>`, a `docs/step4/B<n>_*.md` report, update this file, commit (never push).
4. Pitfalls already met: never edit files containing Arabic with PowerShell `Get-Content/Set-Content` (use the Edit/Write tools); `sqlcmd` needs `-I`; do not re-run `tools/design/gen_efcore.py` (it would overwrite hand-maintained entities/configurations); `UserManager.Set*` persists the user (do not call on unsaved users); creating an authenticator key changes the security stamp.
5. Stops still required: step 5 design directions, step 6 mockups (no UI code before approval), real blockers. (D-10 chart template: approved and seeded in B2.)
6. Shell pitfall: multi-line `python - <<'EOF'` heredocs containing quotes sometimes fail in the Bash tool ("unexpected EOF"); write the script to the scratchpad with the Write tool and run it.

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
- Step 1 decisions D-01…D-18 (`docs/step1/03_Owner_Decisions.md`): D-01 namespaced permission keys; D-02 LayoutType default 'Claim'; D-03 tariff price / deduction > 0; D-04 rejected e-invoices resubmittable, Failed = Rejected + LastError; D-05 manual-entry edit = reverse + re-post; D-06 tariff lock lets masters.manage deviate; D-07 C-08…C-11 recommendations; D-08 tenant settings columns; D-09 Companies.Status; D-10 chart template drafted for approval; D-11 credits reduce only their invoice; D-12 TRIAL plan, trial end → Suspended → Cancelled; D-13 provider console for all staff, support window read-only; D-14 G-recommendations; D-15 AR documents e-invoiced; D-16 prefixes TRP-, CRN-, RCV-, ADV-, STL-, PPY-, JV-, BIL-, ARD-, PAY-, RTN-, JEX- (5 digits); D-17 assumptions accepted; D-18 help pages at the end.
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
| DT-14 | Audit JSON for secret columns | `PasswordHash`, `SecurityStamp`, `TwoFactorSecret`, `CodeHash`, `TokenHash`, `ClientIdEncrypted`, `SecretKeyEncrypted` written as `"***"`; binary columns (logo) omitted | as stated |

## Extra indexes / constraints added beyond the design
- None. (The design's indexes only; ISJSON checks per DT-03 are required by the design's JSON convention.)

## Raw SQL used (where EF Core cannot express something)
| File | Why |
|---|---|
| `database/00_CreateDatabase.sql` | Create the database with collation `Arabic_100_CI_AI_SC_UTF8` and READ_COMMITTED_SNAPSHOT (EF applies the model collation only when it creates the database itself, not in a migration script) |
| `database/90_ApplicationUser.sql` | Application database user `dst_app`: read/write, `DENY DELETE` on core/acc/cfg/trn, `DENY UPDATE` on `core.AuditLogs`, `core.MaintenanceLogs`, `trn.TRN_RateHistory`, `DENY ALTER` on the schemas (verified 8 Oct 2026 on the test database) |
| `tests/…/DatabaseFixture.cs` | `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` on the disposable test database |
| `Infrastructure/Persistence/NumberingService.cs` | `SELECT … FROM cfg.NumberSequences WITH (UPDLOCK, ROWLOCK)` inside the document transaction (BR-038); the increment uses EF `ExecuteUpdate`. `LockAsync` (B2) takes the same lock without issuing a number, so closing a period waits for postings in progress (BR-036) |
| Planned (B7, B10) | E-invoice counter read `WITH (UPDLOCK, ROWLOCK)`; worker queue claims `WITH (UPDLOCK, READPAST)` |

## Tools (development only)
`tools/design/parse_design.py` (design → model), `checks.py` (check constraints), `gen_efcore.py` (one-off source generation — do not re-run over hand-edited files), `verify_database.py` + `catalog.sql` (verification report), `selftest_verify.py`.

## Open issues
- Posting source handlers to register in B5/B6/B8/B9 (`IPostingSourceHandler`); until then pending postings of those types report "cannot be re-posted in this version".
- Voucher PDF / journal-list PDF with the print engine (B6); financial reports, statements, reconciliation RPT-23 and cost-centre report (B9/B10); dashboard cards for pending postings and opening difference (B10).
- System-job soft deletes: `DeletedBy` is required by CHECK, but the tenant-retention job (G-18) has no acting user. To be decided in B12 (likely a seeded platform "system" account); record-lock expiry will attribute the release to the lock holder.

## Deferred (provider-neutral interfaces with local/dev implementations)
- Hosting provider and data residency (DEF-01) — incl. SignalR scale-out backplane, encryption at rest (TDE), backups.
- Payment gateway (DEF-02).
- Email provider (DEF-02).
- Legal check on anonymising personal data (owner handles).
