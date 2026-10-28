# Direct Service platform — Start here

_Draft, 27 Oct 2026 (Stage 1 of the platform restructuring). Finalised after Stage 5. Read this first in any new session, then `BUILD_STATUS.md`._

## What the platform is
**Direct Service** is one platform for several business systems.
- **One application:** one website, one sign-in, one browser app.
- **One SQL Server database:** `DirectServiceMaster`, collation `Arabic_100_CI_AI_SC_UTF8`.
- **One provider console**, used by platform staff.

Customers subscribe to one or more **products**. The side menu shows the products the customer has, and any product with a free-trial offer appears as "Start free trial".

| Product | Code | Status |
|---|---|---|
| Direct Service Transportation | `TRN` | live (built B1–B12, DT-01…DT-280). Includes Accounting |
| Direct Service Accounting | `ACC` | live as part of Transportation; can be subscribed alone after Stage 5 |
| next system (maybe POS) | — | not decided; see `02_How_To_Add_A_New_System.md` |

Accounting is a **shared module**: every system posts into it through its posting contract.

## Where the work stands
| Stage | Content | State |
|---|---|---|
| 1 | Design: `01_Restructuring_Design.md` | done; reviewed, owner decisions recorded in section k (28 Oct 2026) |
| 2 | Shared + Platform split (behaviour unchanged) | in progress: session 1 (step 0 — every stored name pinned, golden tests in `tests/DirectService.Architecture.Tests`) done 28 Oct 2026; sessions 2–8 planned in `BUILD_STATUS.md` |
| 3 | Accounting module + posting contract | not started |
| 4 | Transportation module, UI composition, host rename, menu by product | not started |
| 5 | Products, subscriptions, per-product trials, manual activation, starter module, guides | not started |

Restore point before Stage 2: tag `pre-modules-final-2026-10-27`.

Until Stage 4 is done the code still lives in `src/DirectService.Transportation.*` (see `docs/step9/Setup_and_Run.md`).

## Layout (target)
```
src/Shared/     DirectService.SharedKernel · DirectService.Shared.Infrastructure · DirectService.Shared.UI
src/Platform/   DirectService.Platform.{Domain,Application,Infrastructure,Contracts,UI}
src/Modules/Accounting/      DirectService.Accounting.{Domain,Application,Infrastructure,Contracts,UI}
src/Modules/Transportation/  DirectService.Transportation.{Domain,Application,Infrastructure,Contracts,UI}
src/Hosts/      DirectService.Web (API, SignalR, workers, sign-in pages) · DirectService.Web.Client (WASM app) · DirectService.Database (migrations)
tests/          Architecture · Platform · Accounting · Transportation · Api · UI (.Tests) · DirectService.Testing · e2e
```

## Module rules (enforced by Architecture.Tests)
- A module uses another module **only through its Contracts**. For example, Transportation posts to accounting through `IAccountingPosting`; it never touches `acc` tables.
- Everyone may use Shared and `Platform.Contracts`. Shared uses nothing from Platform or the modules.
- The browser app talks **only to the REST API** (`/api/v1`). The UI libraries reference Contracts and Shared.UI, never Application, Infrastructure or Domain.
- **Never change a stored name.** This covers:
  - entity stored names (`TRN.Trip`) and URL token labels;
  - setting, posting, permission and notification keys;
  - document types, source and party codes;
  - Data Protection names, cookies and localStorage keys;
  - migration ids, tables, schemas and routes.

  `01_Restructuring_Design.md` §e lists them all, and golden tests guard them.
- One composed `MasterDbContext`, one migration history. New systems **add** a schema and tables. Nothing is renamed or moved.

## Key documents
| Document | What it holds |
|---|---|
| `BUILD_STATUS.md` | current state, "Resume here", decisions log (DT-xx), launch checklist |
| `docs/platform/01_Restructuring_Design.md` | placement of everything, contracts, database, stored names, products/trials, stages, owner decisions |
| `docs/platform/02_How_To_Add_A_New_System.md` | step-by-step guide for a new system |
| `docs/step2/01_Architecture_and_Build_Plan.md` | ADR-01…18, API conventions, security model |
| `docs/design/Database_Design_Revision6.md` (+ Revision5, `docs/inputs/Database_Design.md`) | database design |
| `docs/step9/Setup_and_Run.md` | setup, configuration, run, tests, hosting |
| `docs/step7/Design_System.md` | tokens, components |
| `docs/api/openapi-v1.json` | exported API (Redocly-linted) |

## Working rules
1. **Commit locally only. The owner pushes.** Never push, never wait for GitHub runs, never start background "wait" tasks.
2. **Never commit** `src/DirectService.Transportation.Web/appsettings.json` (the owner's local file) or anything under `docs/feedback/`. Stage files by name.
3. After each item: update **`BUILD_STATUS.md`** and the **DT log** ("Decisions taken without asking", one DT row per choice made without asking), then commit with the DT numbers in the message.
4. Testing in owner feedback rounds:
   - **No E2E unless the owner asks.**
   - Run the affected API and UI test classes and one focused visual check (`VISUAL_ONLY`).
   - UI tests send every event through **`UiTestBase.Act`**.
5. **Run tests in the foreground with time limits.** Always set a timeout (and `--global-timeout` for Playwright). Never use endless waits or background polling.
   - Run `dotnet build-server shutdown` before the Api tests on this PC (low memory).
   - The 500,000-trip performance test is explicit only.
6. **Arabic and English everywhere:**
   - every text, error message (both messages required by a unit test) and help page;
   - RTL and LTR;
   - **light and dark** themes;
   - **colour tokens only** (`tokens.css`), never colour literals.
7. Never edit files that contain Arabic with PowerShell `Get-Content`/`Set-Content`. Use the Edit/Write tools.
8. There is no Python on the PC: use the Edit tool or node scripts.
9. Git uses `autocrlf=true`: the repository holds LF.
10. In Git Bash, prefix `VISUAL_ONLY` values carefully (a leading `/` is mangled).
11. Database changes need an approved design revision. Never run `tools/design/gen_efcore.py`. Regenerate `database/MasterDatabase_Idempotent.sql` after each migration.
12. Real blockers and new owner decisions: stop and ask. Smaller ambiguities: choose the recommended option, record it as a DT row, and continue.
