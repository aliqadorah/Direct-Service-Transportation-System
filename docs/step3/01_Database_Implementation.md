# Step 3 — Database Implementation (Entity Framework Core)

_Completed 8 Oct 2026. Working mode per `docs/step2/02_Owner_Decisions_Step2.md`: no stop after step 3; the chart-of-accounts template waits for approval (`03_Chart_Of_Accounts_Template_DRAFT.md`)._

## Result
| Item | Where | Status |
|---|---|---|
| Entities (66, one class per table, no prefix, namespaces Core/Trn/Acc/Cfg) | `src/DirectService.Transportation.Domain/{Core,Trn,Acc,Cfg}` | ✓ |
| Fluent configurations (66 × `IEntityTypeConfiguration<T>`, exact names/types/sizes/precision/defaults/order, named PK/FK/UX/IX/CK/DF) | `src/…Infrastructure/Persistence/Configurations/{Core,Trn,Acc,Cfg}` | ✓ |
| DbContext with named query filters `SoftDelete`, `Tenant`, `Company`; collation `Arabic_100_CI_AI_SC_UTF8`; no automatic FK indexes | `Persistence/MasterDbContext.cs` | ✓ |
| Interceptor: soft delete (who/when/why), append-only guard, stamping from server scope, cross-tenant/company write refusal, `core.AuditLogs` rows with old → new JSON (secrets masked), all in one transaction | `Persistence/Interceptors/AuditSaveChangesInterceptor.cs` | ✓ |
| Initial migration (incl. revision 5) | `Persistence/Migrations/…_InitialMasterDatabase.cs` | ✓ applied to `localhost/DirectServiceMaster` |
| Idempotent SQL script for review | `database/MasterDatabase_Idempotent.sql` (6,162 lines; no DROP/DELETE/CASCADE) | ✓ |
| Seed: 22 permission keys (namespaced, AR/EN), TRIAL plan | `Persistence/Seed/ReferenceDataSeed.cs` (migration seed data) | ✓ |
| Deployment scripts (raw SQL, listed in BUILD_STATUS) | `database/00_CreateDatabase.sql`, `database/90_ApplicationUser.sql` | ✓ (app-user denies verified) |
| Design-vs-database verification | `02_Design_vs_Database_Verification.md` — **no differences** | ✓ |
| Integration tests (17) | `tests/…Infrastructure.Tests/Persistence/DataRulesTests.cs` | ✓ 17/17 pass |
| Revision 5 change note | `docs/design/Database_Design_Revision5.md` | ✓ |
| Chart-of-accounts template | `03_Chart_Of_Accounts_Template_DRAFT.md` | ⏸ waiting for owner approval (D-10) |

## How the model was built
`tools/design/parse_design.py` reads `Database_Design.md` into a model (66 tables, 1,410 columns after rev. 5, 118 business FKs, 300 standard FKs, 111 unique and 141 other indexes); `checks.py` derives the 246 check constraints from the rules marked CHECK (plus ISJSON on JSON columns); `gen_efcore.py` generated the entity and configuration sources once — they are now ordinary source code. `verify_database.py` compares the live database with the same model from the `sys` catalog views; `selftest_verify.py` and a reversible rename test prove that it detects differences.

## Re-running
```
dotnet ef database update --project src/DirectService.Transportation.Infrastructure --startup-project src/DirectService.Transportation.Infrastructure
dotnet ef migrations script --idempotent --project src/DirectService.Transportation.Infrastructure --startup-project src/DirectService.Transportation.Infrastructure --output database/MasterDatabase_Idempotent.sql
python tools/design/verify_database.py
dotnet test --project tests/DirectService.Transportation.Infrastructure.Tests
```
