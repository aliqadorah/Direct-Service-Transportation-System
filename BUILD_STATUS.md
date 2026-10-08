# Direct Service Transportation — Build Status

_Last updated: 8 Oct 2026 (step 3 done; next: step 4 block B1 Foundation & identity)_

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
| 3 | `docs/step3/01_Database_Implementation.md`, `02_Design_vs_Database_Verification.md` (no differences), `03_Chart_Of_Accounts_Template_DRAFT.md` (waiting for approval) |
| Design | `docs/design/Database_Design_Revision5.md` |

## Completed steps
- **Step 0 — Confirm inputs: done (8 Oct 2026).**
- **Step 1 — Requirements inventory and traceability matrix skeleton: approved (8 Oct 2026).**
- **Step 2 — Architecture and build plan: approved (8 Oct 2026)** with Q-01…Q-10 answers.
- **Step 3 — Database implementation: done (8 Oct 2026).** 66 entities + 66 Fluent configurations, `MasterDbContext` (named filters SoftDelete/Tenant/Company), `AuditSaveChangesInterceptor`, migration `InitialMasterDatabase` (rev. 5) applied to `localhost/DirectServiceMaster`, idempotent script `database/MasterDatabase_Idempotent.sql`, seed (22 permissions, TRIAL plan), verification report with **no differences** (66 tables, 1,410 columns, 302 defaults, 66 PKs, 418 FKs, 111 unique + 141 other indexes, 246 checks), 17 integration tests passing. Commit: see git log "Step 3".

## Current step
- **Step 4, block B1 — Foundation & identity** (build order in step 2 §10): request context, URL tokens, problem details, localisation, OpenAPI; sign-up, email verification, sign-in, mandatory 2FA, recovery codes, sessions, lockout, password reset, tenant password/idle settings; users, roles, invitations; companies (create/archive/switch); company settings; numbering service; period guard; record-lock API + SignalR hub skeleton; background-worker host; dev email/file implementations; isolation/permission test harness.
- Waiting for the owner (not blocking): **chart-of-accounts template approval (D-10)** — `docs/step3/03_Chart_Of_Accounts_Template_DRAFT.md`.

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
| DT-14 | Audit JSON for secret columns | `PasswordHash`, `SecurityStamp`, `TwoFactorSecret`, `CodeHash`, `TokenHash`, `ClientIdEncrypted`, `SecretKeyEncrypted` written as `"***"`; binary columns (logo) omitted | as stated |

## Extra indexes / constraints added beyond the design
- None. (The design's indexes only; ISJSON checks per DT-03 are required by the design's JSON convention.)

## Raw SQL used (where EF Core cannot express something)
| File | Why |
|---|---|
| `database/00_CreateDatabase.sql` | Create the database with collation `Arabic_100_CI_AI_SC_UTF8` and READ_COMMITTED_SNAPSHOT (EF applies the model collation only when it creates the database itself, not in a migration script) |
| `database/90_ApplicationUser.sql` | Application database user `dst_app`: read/write, `DENY DELETE` on core/acc/cfg/trn, `DENY UPDATE` on `core.AuditLogs`, `core.MaintenanceLogs`, `trn.TRN_RateHistory`, `DENY ALTER` on the schemas (verified 8 Oct 2026 on the test database) |
| `tests/…/DatabaseFixture.cs` | `ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON` on the disposable test database |
| Planned (B1) | Number-sequence and e-invoice-counter reads `WITH (UPDLOCK, ROWLOCK)`; worker queue claims `WITH (UPDLOCK, READPAST)` |

## Tools (development only)
`tools/design/parse_design.py` (design → model), `checks.py` (check constraints), `gen_efcore.py` (one-off source generation — do not re-run over hand-edited files), `verify_database.py` + `catalog.sql` (verification report), `selftest_verify.py`.

## Open issues
- **D-10 chart-of-accounts template** waiting for owner approval (not blocking; B2 builds the template mechanism and seeds the approved content later).
- System-job soft deletes: `DeletedBy` is required by CHECK, but the tenant-retention job (G-18) has no acting user. To be decided in B12 (likely a seeded platform "system" account); record-lock expiry will attribute the release to the lock holder.

## Deferred (provider-neutral interfaces with local/dev implementations)
- Hosting provider and data residency (DEF-01) — incl. SignalR scale-out backplane, encryption at rest (TDE), backups.
- Payment gateway (DEF-02).
- Email provider (DEF-02).
- Legal check on anonymising personal data (owner handles).
