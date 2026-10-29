# How to add a new system to the Direct Service platform

_Draft, 27 Oct 2026 (Stage 1). It describes the target structure of `01_Restructuring_Design.md`. It is **finalised in Stage 5**, when the starter module `src/Modules/_Starter` exists and every step below has been tried on it. Until then, the names of interfaces and files are the designed ones and may still change slightly._

The example system in this guide is **POS** (product code `POS`, schema `pos`). Replace these names with your system's own. Section numbers in brackets (for example §c) refer to `01_Restructuring_Design.md`.

---

## 0. Before you start

Decide and write down (owner approval):

| Item | Example | Rule |
|---|---|---|
| Product code | `POS` | 2–5 capital letters, unique, **never changes** (it is stored in data) |
| Product names | "Direct Service POS" / «دايركت سيرفس لنقاط البيع» | Arabic and English |
| Database schema | `pos` | new schema; tables `POS_<Name>` |
| Data codes | source module `POS`; party types if any | stored in `acc` rows (§e.2, E-19) |
| Permission keys | `pos.sales.view`, `pos.sales.edit` | prefix = product code in lower case |
| Uses Accounting? | yes: posts sales and payments | always through `IAccountingPosting` |
| Shared parties? | customers from the shared register | `IPartyDirectory` (§b) |
| Trial offer? | 14 days, once per tenant | set by staff in the console after release |

Read first: `docs/platform/00_START_HERE.md` (rules) and `01_Restructuring_Design.md` §a.2 (what a module may reference) and §e (names stored in data).

---

## 1. Create the module from the starter

1. Copy `src/Modules/_Starter` to `src/Modules/Pos`. Rename the five projects to `DirectService.Pos.{Domain,Application,Infrastructure,Contracts,UI}` and the namespaces to match.
2. Add the five projects to `DirectService.slnx` under `Modules/Pos`.
3. References (the starter already has them; do not add others):
   - Domain → SharedKernel.
   - Contracts → SharedKernel.
   - Application → own Domain and Contracts, SharedKernel, `Platform.Contracts`, `Accounting.Contracts`.
   - Infrastructure → own Application and Shared.Infrastructure.
   - UI → own Contracts, `Platform.Contracts` and Shared.UI.
4. Fill in the module class (`PosModule : IModule`) in Infrastructure:
   - `Code = "POS"`;
   - `AddServices` (services, workers, contributors);
   - `ConfigureModel` (the `IModelContributor`);
   - `MapEndpoints`.
5. Register it in the host with one line in `DirectService.Web/Program.cs` (`builder.AddModule<PosModule>()`). Add `services.AddPosUi()` to `DirectService.Web.Client/Program.cs`, and add the UI assembly to the router's `AdditionalAssemblies`.
6. Run `dotnet build` and Architecture.Tests. They fail until the ownership rules below are met, which is intended.

---

## 2. Database schema

1. Create entities in `DirectService.Pos.Domain`:
   - inherit `AuditableEntity`;
   - implement `ITenantScoped` / `ICompanyScoped` as appropriate;
   - add **`[StoredName("POS.Sale")]`** and **`[TokenLabel("Sale")]`**.
   - These two values are permanent. The audit log, live refresh, record locks and every URL depend on them (§e, E-1/E-3).
2. One `IEntityTypeConfiguration<T>` per entity in `Infrastructure/Persistence/Configurations`, following ADR-08:
   - `ToTable("POS_Sales", "pos")`;
   - exact types and precision `(18,3)`;
   - named constraints `PK_/FK_/UX_/IX_/CK_/DF_`;
   - filtered unique indexes `WHERE [IsDeleted] = 0`;
   - every FK `NoAction`.
3. Foreign keys to another module (for example `acc.JournalEntries`, `acc.ApArParties`) go in `CrossModuleRelationships.cs`, mapped by entity name. FKs to `core.Tenants`, `core.Companies` and `core.Users` use the anchor types (exception E-1).
4. Add the migration to the **one** migrations project, using the same history table:
   - `dotnet ef migrations add Pos_Initial --project src/Hosts/DirectService.Database --startup-project src/Hosts/DirectService.Database`
   - The migration creates schema `pos` and its tables.
5. If POS posts with its own source module code or party types, the same migration widens the CHECK constraints `CK_JournalEntries_SourceModule`, `CK_PendingPostings_SourceModule`, `CK_EntryLines_PartyModule` (and `CK_Accounts_ControlPartyType` if POS has a control-party type). Write a `Down` that restores them, and refuse it (`THROW`) once POS rows exist.
6. Regenerate `database/MasterDatabase_Idempotent.sql`, and add `DENY DELETE` / `DENY ALTER` on schema `pos` to `database/90_ApplicationUser.sql`.
7. Record the new tables in a database design revision document under `docs/design/` (owner approval before the migration is applied anywhere but development).

Never rename or move an existing table, column or row; a new system only adds.

---

## 3. Permissions and the product

1. Declare the keys in `DirectService.Pos.Contracts/Security/PosPermissionKeys.cs`.
2. Register them with `IPermissionCatalog`:
   - group names (AR/EN);
   - descriptions;
   - which preset roles get them (Owner, Administrator, Accountant, Dispatcher, Viewer).
3. Seed them with fixed Ids and PublicIds: a class implementing `IPermissionSeedProvider` (`DirectService.Platform.Contracts.Security`, one `PermissionSeed` row per key), registered in DI and passed to `PlatformModelContributor` in `MasterModel`; the Platform contributor writes every module's rows with `HasData` (Stage 2 session 11, DT-337; Transportation's is `Persistence/Seed/ReferenceDataSeed.cs`). Never change these Ids later.
4. Every POS route group calls `.RequirePermission(...)` per endpoint and `.RequireProduct("POS")` on the group. Architecture.Tests fail on a POS endpoint without either.
5. The product row itself (`core.Products`: code, names, status `ComingSoon` → `Available`, `IncludesProductCodes` = `ACC` if POS needs the accounting features) is added by a data step in the POS migration. Its trial offer row starts **off**.

---

## 4. Posting to Accounting

POS never touches `acc` tables. It uses `Accounting.Contracts` (§c):

1. **Mapping keys.** Implement `IPostingKeyProvider` with product code `POS`:
   - keys `POS.SalesRevenue`, `POS.CashOverShort`, …;
   - AR/EN names;
   - normal side;
   - a proposed template account code.
   - Keys appear on the mapping screen for companies that have POS. `EnsureReadyAsync("POS")` refuses posting while one is unmapped.
2. **Chart template.** Optionally implement `IChartTemplateExtension`, which adds POS accounts and mappings when POS is first activated for a company.
3. **Posting a document.** Inside the document's own transaction (`db.InTransactionAsync`):
   ```csharp
   var entry = await posting.PostOrPendAsync(new PostingDocument(
       new DocumentRef("POS", "Sale", sale.Id), sale.SaleDate, $"Sale {sale.Number}", sale.Reference,
       [ PostingLineSpec.Debit(cashBoxAccountId, sale.Total),
         PostingLineSpec.CreditKey("POS.SalesRevenue", sale.Net, taxCodeId: sale.TaxCodeId) ]), ct);
   ```
   - The document, its entry, its number and its audit rows commit together.
   - A technical failure leaves the sale saved and the entry in pending postings.
4. **Retries and links.** Implement `IPostingSourceHandler` for (`POS`, `Sale`) with `RebuildAsync`, `AttachAsync` and `DescribeAsync`.
5. **Void or edit.** Call `ReverseAsync(new DocumentRef("POS","Sale",id), …)`, then post again for an edit.
6. **Parties.**
   - Customers come from `IPartyDirectory` (shared register).
   - POS-only party types need an `IPartyProvider` and the CHECK change of step 2.5.
7. **Numbering.** Use `INumberingService` with document type `POS.Sale` (stored, permanent) inside the transaction.

---

## 5. Application, API and contributions

1. Services in `DirectService.Pos.Application` follow the module pattern in `BUILD_STATUS.md` "Resume here":
   - `IRequestContext`;
   - `IChangeContext.SetAction` for audited actions;
   - `ExpectRowVersion`;
   - `Fail.*` with codes and AR/EN messages in the module's error catalogue (unit-tested), given to the shared engines through an `IErrorTextProvider` (SharedKernel; Transportation's: `TransportationErrorTexts`);
   - idempotent creates with `clientId`;
   - URL tokens only.
2. Endpoints in `Infrastructure/Http/PosEndpoints.cs` under `/api/v1/companies/{company}/pos/…`. Add every route to the module's API summaries (test-enforced).
3. Register what the platform should know, only as needed:
   - `ISearchProvider` (global search);
   - `IDashboardContributor` (figures and alerts);
   - `IReportSourceProvider` / `IStandardReportProvider` (report builder and catalogue);
   - `INotificationTypeProvider` (types `POS.*`);
   - `IAuditVocabularyProvider` (entity, field and action labels AR/EN);
   - `ICompanyProvisioner` (number sequences, defaults for a new company);
   - `IDemoDataContributor`;
   - `ISetupStepProvider`;
   - `IUsageMeter` (plan limits such as `POS.Registers`; a new measure in `UsageMeasures`);
   - `ITenantDataEraser` (personal-data columns);
   - `IPermissionCatalog` (the module's permission keys and preset-role keys; the rows themselves through `IPermissionSeedProvider`, section 3).

   The Platform interfaces are in `DirectService.Platform.Contracts.Modules` (added in Stage 2 session 12, DT-346); Transportation's implementations are in `Transportation.Application/Modules` and `CompanyProvisioningService` — copy their shape.
4. Background work: a worker in `Infrastructure/Workers`, claiming rows with `UPDLOCK, READPAST`, switched by `Jobs:Enabled`.

---

## 6. Screens, menu and help

1. Pages in `DirectService.Pos.UI/Pages`:
   - routes under `/c/{Company}/pos/…`;
   - only Shared.UI components;
   - colour tokens only (no colour literals);
   - Arabic and English text in `Localization/UiText.Pos.cs`;
   - RTL / LTR and light / dark checked.
2. Menu: implement `INavContributor` with product code `POS`, the group (icon, AR/EN label) and items with their permissions. The menu shows it automatically when the tenant has POS (Full, Trial or ReadOnly), and as "Start free trial" under "More systems" while an offer is on.
3. Links: `ILinkResolver` for POS entities (search hits, notifications, audit log, journal source links).
4. Help: `IHelpTopicProvider` with one short AR/EN page per main flow (D-18 style).
5. Read-only state: write buttons respect the product state from `AppSession`. The API refuses writes anyway (`product.readOnly`).

---

## 7. Tests

| Project | What to add |
|---|---|
| `tests/DirectService.Pos.Tests` (new) | unit tests of calculations and rules; DB-rule tests with `DatabaseFixture` from `DirectService.Testing` |
| `DirectService.Api.Tests/Pos/` | API flows: create / edit / void with the posted entry checked line by line; pending posting + retry; permissions; product gating (no product → 403, trial → allowed, ended → read-only). The isolation sweep and endpoint-coverage tests include POS endpoints automatically |
| `DirectService.UI.Tests/Pos/` | bUnit page tests (every event through `UiTestBase.Act`) |
| Architecture.Tests | nothing to write: they check POS references, prefixes, stored names and endpoints. Add the POS golden names with `--update-golden` **once**, review the diff, commit |
| e2e | only when the owner asks |

Run tests in the foreground with time limits (see `00_START_HERE.md`). Add the new test project to the CI loops in `.github/workflows/ci.yml`.

---

## 8. Put the product on sale

1. Release the code with the product `ComingSoon` (visible to staff only).
2. Console → **Plans**: add POS to one or more plans (or create a POS plan) and set its limits.
3. Console → **Products**: set POS to `Available`, then turn its **free-trial offer** on with its length when ready. Tenants without POS then see "Start free trial".
4. Until the payment gateway is connected, staff activate, renew, extend and change plans from Console → Tenants → Subscription, recording the payment method and reference.
5. Update `00_START_HERE.md` (product list), `BUILD_STATUS.md` and the DT log.

---

## 9. Checklist

- [ ] Product code, schema, data codes and permission keys approved and written in the design revision.
- [ ] Every entity has `[StoredName]` and `[TokenLabel]`; golden names committed.
- [ ] Migration adds only; CHECKs widened if needed; idempotent script and `90_ApplicationUser.sql` updated.
- [ ] No reference to another module except its Contracts (Architecture.Tests green).
- [ ] Every posting goes through `IAccountingPosting` inside the document's transaction; handler registered; keys provided.
- [ ] Every endpoint has a permission and the product; API summaries complete; OpenAPI exported and linted.
- [ ] Menu, help, links, search and audit vocabulary contributed; AR/EN; light/dark; RTL/LTR.
- [ ] Tests green; visual check done; BUILD_STATUS and DT log updated; committed locally (the owner pushes).
