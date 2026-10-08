# Direct Service Transportation — Build Status

_Last updated: 8 Oct 2026 (step 2 delivered, waiting at checkpoint 1)_

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

## Build documents
| Step | File |
|---|---|
| 1 | `docs/step1/01_Requirements_Inventory.md` — modules, counts, roles, calculations, conflicts C-01…C-11, gaps G-01…G-21, assumptions A-01…A-22 |
| 1 | `docs/step1/02_Traceability_Matrix.md` — requirement → DB objects → code → screens → tests (skeleton) |
| 1 | `docs/step1/03_Owner_Decisions.md` — owner answers D-01…D-18 (approved 8 Oct 2026) |
| 2 | `docs/step2/01_Architecture_and_Build_Plan.md` — architecture, ADR-01…18, revision 5 proposal, collation, API design, security model, build order |

## Completed steps
- **Step 0 — Confirm inputs: done (8 Oct 2026).** All three inputs found and read in full; system, roles, platform, languages and stack confirmed by the owner. Code folder reachable; `assets/logo.png` present.
- **Step 1 — Requirements inventory and traceability matrix skeleton: approved (8 Oct 2026).** Owner decisions in `docs/step1/03_Owner_Decisions.md`.
- **Step 2 — Architecture and build plan: delivered (8 Oct 2026), awaiting approval at checkpoint 1.**

## Current step
- **Checkpoint 1 — waiting for the owner's answers to Q-01…Q-10** in `docs/step2/01_Architecture_and_Build_Plan.md` §1 (architecture approval; revision 5 option R5-A vs R5-B; default idle timeout; absolute session lifetime; password-length floor; archived companies and the plan limit; collation; PDF engine; build order; suggestions S-01/S-02).
- Next: step 3 (database implementation with EF Core) after approval.

## Environment
- The build runs on the owner's computer (Windows, `D:\Direct Service Transportation System`). The cloud workspace cannot reach NuGet.
- Verified 8 Oct 2026: .NET SDK 10.0.401 (runtime 10.0.12); `dotnet-ef` 10.0.12 installed as a global tool (it was missing); SQL Server 2025 Developer (17.0.1000.7) on `localhost` default instance, Windows authentication, sysadmin; server collation `SQL_Latin1_General_CP1_CI_AS`.
- NuGet reachable (package versions checked 8 Oct 2026: EF Core 10.0.12, FluentValidation 12.1.1, ClosedXML 0.105.1, CsvHelper 33.1.0, QRCoder 1.8.0, Microsoft.Playwright 1.63.0, xunit.v3 4.0.1, bunit 2.11.3).
- Node.js not installed (suggestion S-02). The folder is not a git repository (suggestion S-01).

## Decisions made
- All items listed as settled in `Build_Brief.md` (approved).
- `core.Users.Email` unique platform-wide (confirmed 8 Oct 2026).
- Step 1 decisions D-01…D-18 (8 Oct 2026), summary:
  - D-01 permission keys namespaced: module prefix + PRD key (`trn.trips.view`, `acc.acct.post`, `core.users.manage`); UI shows PRD names.
  - D-02 `TRN_Invoices.LayoutType` default → 'Claim' (revision 5).
  - D-03 tariff price and settlement deduction amount stay `> 0` (database rule).
  - D-04 rejected (never accepted) e-invoices can be corrected and resubmitted; "Failed" = 'Rejected' + `LastError`.
  - D-05 editing a posted manual journal entry = reverse + corrected linked entry.
  - D-06 tariff lock: users with masters.manage may deviate (audited).
  - D-07 built-in Company ownership type: no delete/reclassify/rename; English account name required by the application; partner payment details optional (IBAN validated when given); suspended tenant = view, print, export.
  - D-08 tenant-level settings for password policy and idle timeout (revision 5 — exact design proposed in step 2).
  - D-09 `core.Companies.Status` Active/Archived (revision 5 — exact design proposed in step 2).
  - D-10 chart-of-accounts template and default posting-key mapping drafted for owner approval before seeding.
  - D-11 a subscription credit only reduces the invoice it is applied to; nothing carries forward.
  - D-12 seed one TRIAL plan; trial ends unpaid → Suspended → Cancelled after 30 days.
  - D-13 all platform staff use the provider console; tenant data only in an approved support window, read-only, every request audited.
  - D-14 recommendations G-04, G-10…G-18, G-20, G-21 accepted (incl. `ACC.OpeningBalanceDifference` posting key; shortage deduction typed by the user).
  - D-15 AR receivable documents and returns are e-invoiced.
  - D-16 number prefixes TRP-, CRN-, RCV-, ADV-, STL-, PPY-, JV-, BIL-, ARD-, PAY-, RTN-, JEX-; 5 digits.
  - D-17 assumptions A-01…A-22 accepted (A-19 collation proposed in step 2).
  - D-18 short Arabic/English help page per main flow, written at the end of the build.

## Database design revision 5 (approved in principle; exact definitions pending checkpoint 1)
1. `trn.TRN_Invoices.LayoutType` default 'TaxInvoice' → 'Claim' (D-02).
2. Tenant-level password policy and session idle timeout (D-08) — proposal R5-A: 4 columns on `core.Tenants`; alternative R5-B: table `core.TenantSettings`.
3. `core.Companies.Status` VARCHAR(10) NOT NULL DEFAULT 'Active', CHECK IN ('Active','Archived') (D-09).
To be implemented in step 3 and recorded in `docs/design/Database_Design_Revision5.md`. The input copy of the design stays unchanged.

## Extra indexes / constraints added beyond the design
- None yet.

## Raw SQL used (where EF Core cannot express something)
- None yet. Planned (step 2, ADR-08): number-sequence and e-invoice-counter reads `WITH (UPDLOCK, ROWLOCK)`; worker queue claims `WITH (UPDLOCK, READPAST)`; deployment script (not a migration) creating the application database user with `DENY DELETE` on core/acc/cfg/trn and `DENY UPDATE` on `core.AuditLogs` / `core.MaintenanceLogs`. Each will be listed here when written.

## Open issues
- Checkpoint 1 questions Q-01…Q-10 (step 2 §1).

## Deferred (provider-neutral interfaces with local/dev implementations)
- Hosting provider and data residency (DEF-01).
- Payment gateway (DEF-02).
- Email provider (DEF-02).
- Legal check on anonymising personal data (owner handles).
- SignalR scale-out backplane (chosen with hosting, configuration only).
