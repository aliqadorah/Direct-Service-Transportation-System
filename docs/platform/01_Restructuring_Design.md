# Direct Service platform — Restructuring design (Stage 1)

_Prepared 27 Oct 2026. Design only: no code, project, migration or configuration was changed. Based on the code at commit `d0ef83d` (restore tag `pre-modules-final-2026-10-27`), `BUILD_STATUS.md`, the ADRs in `docs/step2/01_Architecture_and_Build_Plan.md`, `docs/design/Database_Design_Revision6.md` and `docs/step9/Setup_and_Run.md`._
_Status: **reviewed by the owner; decisions recorded in section k (28 Oct 2026)**. Stage 2 session 1 (step 0, the pins of section e) done 28 Oct 2026 — see e.4._

Contents
- a. Inventory and placement
- b. Shared parties
- c. Accounting posting contract
- d. Database and DbContext
- e. Names stored in data (critical)
- f. Products, subscriptions and trials
- g. UI composition
- h. Rename plan
- i. Tests
- j. Stage plan
- k. Owner decisions needed

Terms used in this document:
- **Platform** — tenants, companies, users, sign-in, roles and permissions, plans, products, subscriptions, the provider console and system email.
- **Shared** — technical building blocks with no business meaning of their own: audit, record locks, numbering, files, printing, spreadsheets, URL tokens, custom fields, column views, the report-builder engine and the design system.
- **Module** — a business system: Accounting, Transportation, and later others.
- **Product** — what a customer subscribes to. Codes: `TRN` = Direct Service Transportation, `ACC` = Direct Service Accounting.

---

## Summary of the main points

1. **The code is already layered, but modules are not separated.** One Domain, one Application and one Infrastructure project hold everything.
   - 27 accounting, platform and shared service files read transport tables directly. Examples: platform onboarding creates demo trips; the tenant lifecycle erases drivers; global search and the accounting dashboard read driver balances.
   - 14 transport service files write accounting tables directly.
   - The split therefore needs **contribution interfaces** (section a.3) as well as the posting contract.
2. **The biggest risk is not the database. It is names computed from code:**
   - The audit log entity type is computed from the namespace + the class name (`TRN.Trip`). It is also used for live refresh, record locks and history text.
   - URL tokens are keyed by the class name (`"DST-URL:" + Type.Name`).
   - The Data Protection key folder sits inside the Web project folder.
   - Section e pins every one of them **before** the first file moves (Stage 2, step 0), and adds golden tests that fail if any of them changes.
3. **Database:** one composed `MasterDbContext` (it keeps its name). Each module registers its own configurations. The migration history stays the same (`dbo.__EFMigrationsHistory`, the same 20 migration ids). No table is renamed and no data is moved. Stages 2–4 add **no** migration; each stage proves this with `has-pending-model-changes`.
4. **Products and trials (Stage 5):** six new `core` tables:
   - `Products`
   - `ProductTrialOffers`
   - `PlanProducts`
   - `PlanLimits`
   - `TenantProducts`
   - `SubscriptionActions`

   The tenant's existing plan, status and dates stay authoritative for plan-covered products. Each product gets a trial offer that staff can turn on and off. Staff can activate, renew, extend and change plans by hand, recording the payment. Existing tenants receive TRN + ACC and keep their current trial or plan.
5. **Estimate:** about 240–320 hours over Stages 2–5 (section j).

---

## a. Inventory and placement

Placement codes: **S** = Shared, **P** = Platform, **A** = Accounting, **T** = Transportation. ⚑ = unclear today; a proposed answer is given in a.4.

### a.1 Target layout (approved) and the extra project proposed

```
src/Shared/      DirectService.SharedKernel            request context, scopes, AppException, TextNormalizer, Money, module/product
                                                       abstractions (IModule), [StoredName]/[TokenLabel] attributes
                 DirectService.Shared.Infrastructure   MasterDbContext (composed), interceptors, query filters, numbering, record locks,
                                                       URL tokens, audit, files, printing (Chromium), spreadsheets, exports, search engine,
                                                       report-builder engine, custom/standard fields, column views, import batches
                 DirectService.Shared.UI               design system (Ui/, tokens.css, app.css, icons, fonts), shell (AppShell, SideNav,
                                                       TopBar, CommandBar), Api client, AppSession, Loc + common UiText, help shell
src/Platform/    DirectService.Platform.{Domain,Application,Infrastructure,Contracts,UI}
src/Modules/Accounting/      DirectService.Accounting.{Domain,Application,Infrastructure,Contracts,UI}
src/Modules/Transportation/  DirectService.Transportation.{Domain,Application,Infrastructure,Contracts,UI}
src/Hosts/       DirectService.Web          one server: API, SignalR, workers, sign-in pages (static SSR), OpenAPI, health
                 DirectService.Web.Client   one WASM app: references the module UI libraries + Shared.UI
                 DirectService.Database     ⚑ proposed: EF migrations + design-time factory (sees every module), decision K-08
tests/           Architecture.Tests (new), Platform.Tests, Accounting.Tests, Transportation.Tests, Api.Tests, UI.Tests, e2e,
                 DirectService.Testing (new helper library: DatabaseFixture, fakes; not a test project)
```

Where the API endpoints and workers live: each module's **Infrastructure** project gets an `Http/` folder (`Map<Module>Endpoints`) and a `Workers/` folder. The host only calls `app.MapModules()`. This keeps a module self-contained, which matters for "how to add a new system". In Stage 2–3 the endpoint files may stay in the host and move in Stage 4; the routes do not change in either case.

### a.2 Module rules (enforced by Architecture.Tests, section i)

| Project kind | May reference |
|---|---|
| `*.Domain` | SharedKernel only |
| `*.Contracts` | SharedKernel only (WASM-safe: no EF, no ASP.NET) |
| `*.Application` | its own Domain and Contracts, SharedKernel, `Platform.Contracts`, **other modules' Contracts**. No exception since Stage 2 session 14 (the temporary Transportation.Application → Platform.Application reference of DT-343 is gone, DT-365; the compiled assembly is checked too). Test: `LayerRuleTests.Module_application_references_its_domain_and_contracts_and_the_platform_contracts` |
| `*.Infrastructure` | its own Application, Shared.Infrastructure, ASP.NET Core framework. Exception E-1: `Platform.Domain` anchor types `Tenant`, `Company` and `User`, for **FK mapping only** (K-09); a module's Domain may hold them as foreign-key navigation properties (a.4, DT-338). Test: `LayerRuleTests.Modules_use_only_the_platform_anchor_types_for_foreign_keys_E1` |
| `*.UI` | its own Contracts, `Platform.Contracts`, Shared.UI |
| Shared.* | nothing from Platform or the modules |
| `Shared.Contracts` (added in Stage 2 session 6, owner decision DT-309) | nothing at all — browser-safe DTOs of the shared engines; SharedKernel may reference it, and the module Contracts and UI use it |
| Web.Client | the UI projects, Shared.UI, Contracts |
| Web | everything (composition root) |

A module never touches another module's tables with `Set<T>()`. This is guaranteed at compile time: the other module's Domain assembly is not referenced. Foreign keys that cross modules are configured by entity name in one file per module (`CrossModuleRelationships.cs`), so the database model and its constraints stay exactly as they are (section d).

### a.3 Contribution interfaces (how Shared/Platform/Accounting use module data without referencing it)

Defined in SharedKernel (for Shared), Platform.Contracts or Accounting.Contracts. Each module registers its implementations in its `IModule.AddServices`. Stage 2 session 12 (DT-346) added the Platform ones to `Platform.Contracts/Modules` — `ICompanyProvisioner`, `IDemoDataContributor`, `ISetupStepProvider`, `IUsageMeter` (no separate `ILimitCheck`: the plan limit compares the meters' counts), `ITenantDataEraser`, `IPermissionCatalog`, `INotificationTypeProvider` — with the Transportation implementations in `Transportation.Application/Modules`; session 13 added `IHealthFigureSource` (the module's figures on the console's health page, DT-355) and `IPermissionCatalog.SupportSessionKeys` (the view keys of a support session, DT-354), and `IUserNotifier` (implemented by the module's notification service until it moved, DT-353); session 14 moved `IUserNotifier` and `PlatformNotifications` to `Platform.Contracts/Collaboration` (the platform's `NotificationService` implements it; `HoldersAsync`, `KeepOneAsync`, `ClearAsync` added, DT-360), added `IPlanLimitCheck` (`Platform.Contracts/Modules/PlatformServices.cs`: the platform services a module calls, DT-358) and extended `INotificationTypeProvider` with the parameters of older texts, the module's links, the parameters worked out when shown (the set-up count) and the older notifications merged into one (DT-360); `AuditVocabularies` (SharedKernel) combines the `IAuditVocabularyProvider`s for the describer and the platform's audit log (DT-362); and `IErrorTextProvider` (SharedKernel) for the module's error catalogue.

| Interface | Owner | Replaces today's direct reads in | Implemented by |
|---|---|---|---|
| `IAccountingPosting` (+ `IPostingSourceHandler`) | Accounting.Contracts | PostingEngine used from trips, invoices, cash (section c) | Accounting; handlers in T |
| `IPostingKeyProvider` | Accounting.Contracts | `PostingKeys` constants, mapping screen, banner | A (ACC.* keys), T (TRN.* keys) |
| `IPartyProvider` | Accounting.Contracts | `PartyResolver`, `PostingEngine.PartyMatches`, statements | A (ApArParty, Customer role), T (Customer, Driver, Partner) |
| `IPartyBalanceSource` | Accounting.Contracts | `AccountingDashboardService` → driver/partner balances | T |
| `IReconciliationSource` | Accounting.Contracts | RPT-23 sub-ledger lines (drivers, partners, customers) | T |
| `IPeriodCloseCheck` | Accounting.Contracts | `PeriodService` (draft invoices before close) | T |
| `ICashMovementSource` | Accounting.Contracts | `CashBankService` reading receipts, advances, settlements, partner payments | T |
| `IEInvoiceDocumentSource` | Accounting.Contracts | `EInvoiceService`, `EInvoiceDocumentBuilder` for invoices / credit notes | T (A has its own for receivables) |
| `IChartTemplateExtension` | Accounting.Contracts | `ChartTemplate` transport accounts 51xx, 1104…, TRN mappings | T |
| `ICompanyProvisioner` | Platform.Contracts | `CompanyProvisioningService` (invoice layouts, ownership types) | A, T |
| `IDemoDataContributor` | Platform.Contracts | `OnboardingServices` demo company (drivers, sites, tariffs, trips) | A, T |
| `ISetupStepProvider` | Platform.Contracts | setup checklist steps | A, T |
| `IUsageMeter` / `ILimitCheck` | Platform.Contracts | `PlanLimits.EnsureTripsAsync`, console usage (trip counts) | T |
| `ITenantDataEraser` | Platform.Contracts | `TenantLifecycleService.DeleteTenantAsync` (assembly scan) and `AnonymiseAsync` (drivers, customers, partners) | S (generic, via the EF model), T (personal-data columns) |
| `IPermissionCatalog` | Platform.Contracts | `PermissionKeys.All`, `PresetRoles` | P, A, T |
| `INotificationTypeProvider` | Platform.Contracts | `NotificationTexts.Catalog`, `NotificationLinks` | P, A, T |
| `ISearchProvider` | SharedKernel | `GlobalSearchService` (10 entity types) | A, T |
| `IReportSourceProvider` / `IStandardReportProvider` | SharedKernel | `ReportSources`, `StandardReportService.Catalog` | A, T |
| `IDashboardContributor` | SharedKernel | `DashboardService` figures and alerts, accounting dashboard reminders | A, T |
| `IAuditVocabularyProvider` | SharedKernel | `AuditVocabulary` (70 entities, 97 actions, 256 fields, 111 codes) and `AuditDescriber` | P, A, T |
| `ISettingsSection` | SharedKernel | `CompanySettingsService` operations keys (TRN.*) — not needed yet: since Stage 2 session 14 the operations section is the module's `OperationSettingsService` at the same routes (DT-363); the interface comes with the settings screen's composition (Stage 4) | T |
| `IFieldCatalogProvider` | SharedKernel | `FieldCatalog` (TRN.Trip/Driver/Customer) | T |
| `IImportTemplateProvider` | SharedKernel | `ImportTemplateService` | A, T |
| `IExportJobProvider` / `IExportNotifier` (added Stage 2 session 10, DT-334) | SharedKernel | `ExportService` kinds standard / audit-log / company and its notifications | T (P for the notifications later) |
| `INavContributor`, `IHelpTopicProvider`, `ILinkResolver` (UI) | Shared.UI | `NavModel`, `HelpTopics`, `Layout/Links` | P, A, T |

### a.4 Tables (by schema) and entities

No table is renamed. "Owner" = the project whose Domain holds the entity and whose Infrastructure holds its `IEntityTypeConfiguration`.

**core (20 tables)**

| Table | Owner | Reason |
|---|---|---|
| Tenants, Companies, Users, UserSessions, UserRecoveryCodes, UserCompanyRoles, Roles, RolePermissions, Permissions, Invitations, TermsAcceptances | P | identity, tenancy, access |
| Plans, SubscriptionInvoices | P | subscription |
| SupportAccessGrants, Announcements, SystemEmailSettings, EmailOutbox, Notifications | P | provider console, system email, user notifications |
| AuditLogs | S | written by the interceptor for every module; append-only |
| MaintenanceLogs ⚑ | A | used only by the accounting maintenance zone (`acc.acct.maint`) although it sits in `core`. Placement follows use; the schema stays (K-11) |

**cfg (10 tables)** — all **S** except one:

| Table | Owner | Note |
|---|---|---|
| CompanySettings | S | key/value store. Each key belongs to the module of its prefix (`TRN.*` → T section, `CORE.*` → P, `ACC.*` → A) |
| NumberSequences | S | numbering service. Document types per module (pinned values, section e) |
| RecordLocks, ImportBatches, TableColumnViews, CustomFieldDefinitions, StandardFieldConfigs, SavedReports, DashboardPins | S | generic engines; their content is keyed by module-owned strings |
| EInvoiceSettings ⚑ | A | the national e-invoicing connector serves transport invoices and accounting receivables (K-10) |

**acc (21 tables)** — all **A**: Accounts, ApArDocumentLines, ApArDocuments, ApArEInvoiceSubmissions, ApArParties, ApArPayments, ApArReturns, BankAccounts, BudgetLines, Budgets, CashBoxes, CostCentres, EntryLines, JournalDrafts, JournalEntries, JournalExportBatches, JournalTemplates, PendingPostings, Periods, PostingKeyMappings, TaxCodes.

**trn (25 tables + 2 views)** — all **T**:
- Tables: TRN_Advances, TRN_CreditNoteLines, TRN_CreditNotes, TRN_Customers ⚑ (section b), TRN_Drivers, TRN_EInvoiceSubmissions, TRN_InvoiceLayouts, TRN_InvoiceLines, TRN_Invoices, TRN_LoadingSites, TRN_Materials, TRN_MonthlySiteRates, TRN_OwnershipTypes, TRN_PartnerOverpayments, TRN_PartnerPayments, TRN_Partners, TRN_PreviousBalanceItems, TRN_RateHistory, TRN_ReceiptAllocations, TRN_Receipts, TRN_SettlementDeductions, TRN_Settlements, TRN_Tariffs, TRN_Trips, TRN_UnloadingSites.
- Views: TRN_PartnerPaymentBoundary, TRN_TripPartnerCoverage.

Domain support types: `Common/AuditableEntity` and `ScopeInterfaces` (`ITenantScoped`, `ICompanyScoped`, `IAppendOnly`) → SharedKernel.

Cross-module navigation properties are removed and replaced by the id plus a contract call. The foreign keys stay in the model (by name), so the database is unchanged:

| Today | Becomes |
|---|---|
| `Customer.ApArParty` | `ApArPartyId` + `IPartyProvider` |
| `Invoice/Trip/Receipt/... → JournalEntry` | `JournalEntryId` |
| `Receipt → CashBox/BankAccount` | ids |
| `JournalEntry.VoidedByUser`, `TableColumnView.User`, other `User` navigations | allowed through exception E-1 |

### a.5 Application services (`src/DirectService.Transportation.Application`, 144 files)

| Folder | Files → placement |
|---|---|
| Abstractions | `IAppDbContext`, `IRequestContext`, `DataScope`, `AppException`, `DbExtensions`, `SqlJson`, `SqlText` → **S** (SharedKernel / Shared.Infrastructure); `IUrlTokenService`, `IEntityResolver`, `INumberingService`, `IRecordLockGuard`, `IAfterCommit`, `IRealtimeNotifier`, `Printing`, `Spreadsheets` → **S**; `IEmailSender` → **P**; `IPeriodGuard`, `EInvoicing` abstractions → **A** (Contracts); `IMonthlyRateLock`, `LockedMonthlyRate` → **T** |
| Accounting | AccountingDashboardService, CashBankService, ChartImportService, ChartOfAccountsService, ChartTemplate, JournalDraftService, JournalExportService, JournalImportService, JournalService, JournalTemplateService, MaintenanceService, OpeningBalanceService, PartyResolver, PendingPostingService, PeriodService, PostingEngine, PostingKeyService, TaxCodeService → **A**. Exceptions: `ImportSupport` → **S** (also used by trips, masters and tariffs); `PostingKeys` split into `ACC.*` (A) and `TRN.*` (T), `AccountingCodes` → Accounting.Contracts |
| ApAr | AgeingService, ApArDocumentService, ApArPartyService, ApArPaymentService, ApArPrintService, ApArReturnService, ApArSupport → **A** |
| Audit | AuditDescriber, AuditVocabulary → **S** engine + per-module vocabulary providers |
| Budgets | BudgetImportService, BudgetMath, BudgetReportService, BudgetService → **A** |
| Cash | AccountStatementService (driver/partner statements), AdvanceService, CashSupport, PartnerPaymentService, PreviousBalances, ReceiptService, SettlementService, VoucherPrintService → **T** |
| Collaboration | ExportService, GlobalSearchService (engine), RecordLockService → **S**; AuditLogService (viewer), NotificationService, NotificationTexts, NotificationLinks → **P** (types contributed per module) |
| Common | AmountInWords, AuditTrail, BrandFonts, DocumentPrinter, ErasedRecords, IssuedNames, PrintMode, TextNormalizer → **S**; MasterIds → **T** |
| Configuration | ColumnViewService, CustomFieldService, StandardFieldService, FieldCatalog (engine) → **S**; CompanySettingsService → **P** (company profile, logo, market) with T's operations section; ConfigurationKeys → split by prefix, values pinned |
| EInvoicing ⚑ | EInvoiceService (+ `.ApAr`), EInvoiceSettingsService, EInvoiceDocumentBuilder (UBL engine) → **A**; the invoice / credit-note → document mapping → **T** (`IEInvoiceDocumentSource`) |
| Identity | AccessService, AccountService, AuthService, EmailChangeService, GrantGuard, InvitationService, MeService, RoleService, SessionService, SignInThrottle, TenantPasswordValidator, TotpCode, TwoFactorSettingsService, UserAdminService, UserRowSave → **P** |
| Imports | ImportTemplateService → **S** (templates contributed) |
| Invoicing | CreditNoteService, InvoiceCalculator, InvoiceLayoutService, InvoicePrintService, InvoiceService, InvoicingPostingHandlers → **T** |
| Ledger | LedgerReportExports, LedgerReportService → **A** |
| MasterData | CustomerService ⚑ (section b), DriverService, ImportMapping, MasterDataSupport, MasterExchangeService, OwnershipTypeNames, PartnerService, SimpleListServices → **T**. `MasterDataSupport` has generic parts (find by token, idempotent create, reference counts) that go to **S** as `RecordSupport` |
| Notifications | EmailLayout, EmailTemplates → **P** |
| Platform | Billing, CompanyProvisioningService, ConsoleService, OnboardingServices, PersonalDataService, ProviderSetupService, StaffAccountService, StaffService, SubscriptionService, SystemEmail, SystemEmailService, TenantLifecycleService (+ SupportAccessService), TenantService → **P**; CompanyExportService → **P** using the EF model (all modules' tables) |
| Pricing | MonthlyRateService, PricingExchangeService, PricingResolver, PricingRules, RateHistoryLog, TariffService → **T** |
| Reports | ReportBuilderService, ReportExporter, SavedReportService, DashboardService (shell) → **S**; ReportSources and StandardReportService split by source/report (a.9) |
| Trips | TripCalculator, TripExportService, TripImportService, TripPostingHandler, TripService → **T** |

### a.6 Infrastructure (`src/DirectService.Transportation.Infrastructure`)

| Item | Placement |
|---|---|
| Persistence: `MasterDbContext`, `QueryFilters`, Interceptors (audit / stamping / soft delete / append-only, live change), `NumberingService`, `EntityResolver`, `DesignTimeDbContextFactory` | **S** (factory → Database project) |
| Persistence/Configurations/Core | **P** (AuditLog → **S**, MaintenanceLog → **A**) |
| Persistence/Configurations/Acc | **A** |
| Persistence/Configurations/Cfg | **S** (EInvoiceSettings → **A**) |
| Persistence/Configurations/Trn | **T** |
| Persistence/Seed/ReferenceDataSeed | split. Permissions rows are seeded by the module that owns the key (same Ids and PublicIds, same `HasData` values); the TRIAL plan → **P** |
| Persistence/Migrations (20 + snapshot) | Database project (section d) |
| `MonthlyRateLock` | **T** |
| Billing/FakePaymentGateway (+ `SqlPlatformLock`, `PlatformNumbers`) | **P** |
| EInvoicing/JoFotaraConnector, EInvoiceStore | **A** (EInvoiceStore's claim on `TRN_EInvoiceSubmissions` goes behind `IEInvoiceDocumentSource`) |
| Email (EmailOutbox, EmailTransports, MimeMessageBuilder) | **P** |
| Files (LocalFileStore, SpreadsheetService) | **S** |
| Identity (UserStore, ReplaySafeAuthenticatorTokenProvider, StaffInvitationTokenProvider) | **P** |
| Printing (ChromiumPdfRenderer, QrCodeRenderer) | **S** |
| Security/UrlTokenService | **S** |

### a.7 Endpoint groups (`src/DirectService.Transportation.Web/Endpoints`, 521 operations)

| File | Routes | Ops | Placement |
|---|---|---|---|
| AuthEndpoints | /auth | 16 | P |
| AccountEndpoints | /me, /users, /roles, /invitations, /tenant (security) | 47 | P |
| PlatformEndpoints | /platform/*, /tenant (subscription, demo), /setup-checklist | 52 | P |
| CompanyEndpoints | /settings (profile, logo, market, operations, number-sequences), /locks | 14 | P (profile) + S (numbering, locks) + T (operations keys) |
| CollaborationEndpoints | /audit-log, /exports, /notifications | 9 | S / P |
| AccountingEndpoints | /accounting/* (accounts, journal, drafts, templates, periods, opening balances, cost centres, cash boxes, bank accounts, tax codes, posting keys, pending postings, dashboard) | 85 | A |
| ApArEndpoints | /apar/* incl. receivables e-invoicing | 33 | A |
| BudgetEndpoints | /accounting/budgets, budget-vs-actual | 16 | A |
| LedgerReportEndpoints | /accounting/reports/*, /accounting/maintenance | 23 | A |
| EInvoicingEndpoints | /einvoice-submissions (T), /settings/e-invoicing (A) | 8 | split |
| InvoicingEndpoints | /invoices, /credit-notes, /invoice-layouts | 23 | T |
| CashEndpoints | /receipts, /advances, /settlements, /partner-payments | 31 | T |
| MasterDataEndpoints | /drivers, /customers, /partners, /ownership-types, /materials, /loading-sites, /unloading-sites (T); /custom-fields, /standard-fields, /column-views (S) | 73 | T + S |
| PricingEndpoints | /tariffs, /monthly-rates | 18 | T |
| TripEndpoints | /trips/* | 14 | T |
| ReportEndpoints | /reports (catalogue, standard, builder, saved, pins), /dashboard | 20 | S (engine) with A/T reports |

Host-only (stays in `DirectService.Web`): Pipeline (ApiSummaries → per-module summaries, Authorization, ContextMiddleware, DatabaseVersion, ExpiredFormMiddleware, Filters, OpenApiSetup, ProblemWriter), RateLimits, Realtime/LiveHub, Security (AuthFlow, AuthSetup, DataProtectionSetup), Components/Account (SSR sign-in pages), Program.

**Every route stays exactly as it is.** The one deliberate addition is `GET /api/v1/accounting/tax-codes` (alias). The old `/settings/tax-codes` API route keeps working (section f.8).

### a.8 Background workers, notification types, permissions

| Worker | Placement |
|---|---|
| EInvoiceWorker | A (drives both submission tables through `IEInvoiceDocumentSource`) |
| PendingPostingWorker | A |
| EmailOutboxWorker, SessionCleanupWorker, SupportWindowWorker, TenantLifecycleWorker | P |
| ExportWorker, LockExpiryWorker | S |

| Notification type (stored) | Owner |
|---|---|
| `EINV.Rejected`, `EINV.SetupError` | A |
| `ACC.PostingFailed` | A |
| `CORE.ExportReady`, `CORE.ExportFailed` | S |
| `CORE.InvitationAccepted`, `CORE.SupportRequested`, `CORE.PaymentFailed` | P |

| Permission key (stored, unchanged) | Product | Note |
|---|---|---|
| `trn.trips.view/edit/void`, `trn.inv.view/issue/void`, `trn.rcpt.manage`, `trn.drv.view/advance/settle`, `trn.masters.manage` | TRN | |
| `acc.je.view/export`, `acc.acct.view/post/postentry/apar/setup/maint`, `acc.budget.view/manage/approve` | ACC | |
| `core.settings.manage`, `core.users.manage`, `core.tenant.manage` | platform (always) | |
| `trn.reports.view` ⚑ | shared | today it opens the report builder, saved reports and the financial-reports catalogue too. It is kept with its code and becomes a cross-product key ("Reports"), so an Accounting-only tenant can use the builder (K-12) |

Preset roles (Owner, Administrator, Accountant, Dispatcher, Viewer) keep their names and grants. Keys of products the tenant does not have are hidden in the role editor and never effective (section f.6).

### a.9 Reports and report-builder sources

| Reports | Placement |
|---|---|
| RPT-01 trips … RPT-11 monthly-summary, RPT-12 customer-statement (invoice-based), RPT-13 driver-statement, RPT-14 partner-statement, RPT-19 driver-balances, RPT-20 partner-balances, RPT-21 registers, RPT-22 e-invoicing | T |
| RPT-15 account-statement, RPT-16 cash-bank-statement, RPT-17 receivables-ageing ⚑ (uses transport invoices + AR documents), RPT-18 payables-ageing, RPT-24 supplier-statement, RPT-23 reconciliation (with `IReconciliationSource` lines from T) | A |
| Ledger reports (trial balance, P&L, balance sheet, GL, cost centres, budget vs actual) | A |

RPT-17 stays in A. Its transport-invoice part comes from `IReconciliationSource`/`IPartyBalanceSource`; with only Accounting it shows AR documents only.

Builder sources:
- `trips`, `invoices`, `credit-notes`, `receipts`, `advances`, `settlements`, `partner-payments`, `drivers`, `customers` → T.
- `journal-lines` → A.
- Keys pinned (stored in `cfg.SavedReports.Source`).

Dashboard:
- **Company home** (`DashboardService`): the shell is S. KPI figures and transport alerts are T. Cash/bank cards and pending postings are A.
- **Accounting dashboard**: A. Driver/partner figures and the reminders for overdue invoices, rejected e-invoices, unallocated receipts and carried advances are contributed by T and are absent when TRN is not active.

### a.10 UI (`src/DirectService.Transportation.Web.Client`, 240 files)

| Area | Placement |
|---|---|
| `Ui/*` (≈ 75 components: DataGrid, Pager, Modal, fields, StateView, RecordHistory, RecordLockGuard, ColumnChooser, SavedViews …), `wwwroot/css` (tokens, app, fonts), `img/icons.svg`, `js/theme.js`, `js/ui.js` | Shared.UI |
| Layout: AppShell, BareLayout, CommandBar, CommandResult, CompanyLayout, TopBar, SideNav, NavModel (composer), NotificationsPanel, NoAccessView, NotFoundView, Links (composer) | Shared.UI |
| Layout: ConsoleLayout, TenantBanners, SupportEndedView, SystemEmailAlert | Platform.UI |
| Layout: PostingBanner | Accounting.UI |
| Services: Api, ApiProblem, AppSession, ClientServices, Downloads, FormConfig, ListQuery, LiveUpdates, ShellState, UiJs | Shared.UI |
| Services: AccountingLookups, MoneyAccounts | Accounting.UI |
| Services: TripLookups | Transportation.UI |
| Localization: Loc, UiText (common) | Shared.UI |
| Localization: UiText.Platform | Platform.UI |
| Localization: UiText.Accounting, UiText.Budgets | Accounting.UI |
| Localization: UiText.Billing | Transportation.UI |
| Localization: UiText.Settings, UiText.Reports | split by key |
| Pages root: Dashboard (shell), PrintPreview, NotFoundPage, DesignGallery | Shared.UI |
| Pages root: Home, Profile, SetupChecklist | Platform.UI |
| Pages/Accounting (34: chart, journal, templates, drafts, AP/AR, cash & banks, periods, budgets, P&L, BS, TB, statement, maintenance, opening balances, automatic entries, dashboard) | Accounting.UI |
| Pages/Billing (32: invoices, credit notes, receipts, advances, settlements, partner payments, driver/partner accounts, e-invoicing list) | Transportation.UI |
| Pages/Trips (10), Pages/Masters (16 incl. tariffs, monthly rates, import) | Transportation.UI |
| Pages/Console (9), Pages/Tenant (4) | Platform.UI |
| Pages/Settings: AuditLog, CompanySettings, UsersRoles | Platform.UI |
| Pages/Settings: Numbering, FieldConfiguration | Shared.UI |
| Pages/Settings: AccountMapping, EInvoiceSettings, TaxCodes (→ /accounting/tax-codes) | Accounting.UI |
| Pages/Settings: InvoiceLayoutDesigner | Transportation.UI |
| Pages/Reports: ReportsCatalog, ReportBuilder, SavedReports, PinCard, ExportDownload, ReportProblems | Shared.UI |
| Pages/Reports: Reconciliation | Accounting.UI |
| Pages/Help: HelpIndex, HelpPage; Console help | Shared.UI shell |
| Help topics: setup, profile, console-staff | P |
| Help topics: month-end, budget, journal-templates, accounting-dashboard | A |
| Help topics: monthly-rates, quick-entry, trip-import, trip-edit, invoicing, e-invoicing, credit-notes, receipts, driver-accounts, partner-payments | T |
| Help topics: concurrent-work, fields | S |
| `wwwroot/DirectService.Transportation.Web.Client.lib.module.js` | renamed with the client assembly (section e, item E-21) |

### a.11 Test classes → see section i.2.

### a.12 Unclear placements (⚑) and proposed answers

| # | Item | Proposed answer |
|---|---|---|
| U-1 | `trn.TRN_Customers` / Customer | Stays T as the transport customer **profile**. The shared customer is the accounting party (section b) |
| U-2 | E-invoicing engine, `cfg.EInvoiceSettings`, `acc.ApArEInvoiceSubmissions` | A ("tax compliance" serves every system). `trn.TRN_EInvoiceSubmissions` stays T behind `IEInvoiceDocumentSource` |
| U-3 | `core.MaintenanceLogs` | A (only the maintenance zone uses it); the schema stays `core` |
| U-4 | `trn.reports.view` | Cross-product key "Reports"; code unchanged |
| U-5 | Cash boxes / bank accounts vs receipts | Cash boxes and banks are A. Receipts, advances, settlements and partner payments are T documents that post to them |
| U-6 | Customer statement RPT-12 vs supplier statement RPT-24 | RPT-12 stays T (invoice-based); an ACC-only tenant uses the account statement with the party filter |
| U-7 | Chart template (51xx transport accounts) | A owns a **general** template; T adds its accounts and TRN mappings through `IChartTemplateExtension` when TRN is first activated for a company (K-13) |
| U-8 | Company settings "operations" (TRN.* keys) | A T settings section shown inside Company settings |
| U-9 | Masters menu (no permission today, always shown) | Belongs to TRN; Customers also reachable from Accounting > Parties for ACC-only tenants |
| U-10 | Audit log viewer | P (screen); vocabulary from every module |
| U-11 | `CompanyExportService`, tenant soft-delete-all | P, driven by the **EF model** (every configured entity) instead of an assembly scan, so new modules are included automatically |

---

## b. Shared parties

**Today there are two registers:**
1. `trn.TRN_Customers` — transport customers with transport fields (custom fields `TRN.Customer`, tariffs, invoice layout, payment terms). Each one is optionally linked to an accounting party through `ApArPartyId`.
2. `acc.ApArParties` — accounting parties with role `Customer` or `Supplier`, each with a control account.

Journal lines name a party by `PartyModule` + `PartyType` + `PartyId`, for example `TRN/Customer/17`, `TRN/Driver/4` or `ACC/ApArParty/9`. A database CHECK limits `PartyModule` to `'TRN','ACC'`.

**Design (recommended, K-07):**
- **The shared customer/supplier register is `acc.ApArParties`, owned by Accounting**, and exposed to every system through `Accounting.Contracts.IPartyDirectory`, which can find, create, get, ensure a party for a profile, and return its balance.
  - Accounting-only tenants already work this way.
  - A future POS uses the same parties.
- **`trn.TRN_Customers` stays in Transportation** as the transport profile of a customer, linked to its party by `ApArPartyId`.
  - From Stage 3, saving a transport customer **ensures** the linked party: it creates one when it is missing, using the code that already exists for receivables (`ApArPartyService` "the accounting party of a transport customer").
  - Existing unlinked customers are linked **at once in Stage 3** by a one-time, idempotent data step (owner decision K-07b, 28 Oct 2026). It inserts new party rows and fills the empty `ApArPartyId`; running it again changes nothing; nothing is moved or renamed.
- **Historic and new journal lines keep their party values.** A transport customer's lines stay `TRN/Customer/<id>`; nothing is re-pointed. Statements, ageing and balances ask `IPartyProvider` for "all party references of this party". That returns `ACC/ApArParty/9` plus `TRN/Customer/17` when the two are linked, so one customer shows one statement.
- **Suppliers** are already `acc.ApArParties` (role Supplier) and become shared without change.
- **Drivers and partners stay in Transportation** (`trn.TRN_Drivers`, `trn.TRN_Partners`). They remain accounting parties through the `IPartyProvider` that Transportation registers for module `TRN`, types `Driver` and `Partner`. That provider supplies:

| What | Used by |
|---|---|
| name and URL token | journal form, statements, audit text |
| active flag | party pickers |
| control-party type (`Driver` / `Partner`, matching `acc.Accounts.ControlPartyType`) | control-account check (FR-106) |
| `IPartyBalanceSource` (balances, previous-balance lines) | accounting dashboard, RPT-19/20 figures |
| `IReconciliationSource` (sub-ledger totals for 1104 / driver payable / partner payable) | RPT-23 |

  Accounting treats `PartyModule`, `PartyType` and `ControlPartyType` as **opaque codes registered by providers**. It no longer hard-codes "Driver" and "Partner"; `PostingEngine.PartyMatches` asks the provider. Settlements and partner payments (including DT-226/228/230/271/276/277 rules) stay entirely in T.
- **A new system with its own party types** (for example a POS "Member") registers a provider and widens two CHECK constraints in its own migration (`CK_EntryLines_PartyModule`, `CK_Accounts_ControlPartyType`). This is the only database change a new system needs in `acc`.

---

## c. Accounting posting contract

The current `PostingEngine` already does everything required (ADR-09). The contract wraps it so that other modules depend only on `Accounting.Contracts`. Behaviour, messages and error codes stay exactly as they are.

```csharp
// DirectService.Accounting.Contracts.Posting
public sealed record DocumentRef(string Module, string Type, long Id);          // stored as SourceModule / SourceType / SourceId
public sealed record PartyRef(string Module, string Type, long Id);             // stored as PartyModule / PartyType / PartyId
public sealed record PostingLineSpec(
    string? MappingKey, long? AccountId,          // exactly one: a posting key (resolved by mapping) or an explicit account (cash box, bank)
    decimal Debit, decimal Credit,                // one of them > 0; amounts already rounded to the currency's decimals
    long? CostCentreId = null, PartyRef? Party = null,
    string? Note = null, string? Reference = null,
    long? TaxCodeId = null, long? TaxAccountId = null);   // tax line added by the engine (DT-255)
public sealed record PostingDocument(
    DocumentRef Source, DateOnly EntryDate, string Description, string? Reference,
    IReadOnlyList<PostingLineSpec> Lines);
public sealed record PostedEntry(long JournalEntryId, string Number, string Token, bool Pending);

public interface IAccountingPosting
{
    Task EnsureReadyAsync(string productCode, CancellationToken ct);            // FR-094: every mapping key of that product mapped
    Task<PostedEntry?> PostOrPendAsync(PostingDocument doc, CancellationToken ct);   // document's own transaction required
    Task<PostedEntry> PostAsync(PostingDocument doc, CancellationToken ct);          // business failure → refusal (FR-098)
    Task<PostedEntry> ReverseAsync(DocumentRef source, string description, string? voidReason, CancellationToken ct);
    Task<int> CurrencyDecimalsAsync(CancellationToken ct);
}
public interface IPostingSourceHandler      // registered per (Module, Type) by the owning module
{
    string Module { get; } string Type { get; }
    Task<PostingDocument?> RebuildAsync(long sourceId, CancellationToken ct);  // retry of a pending posting
    Task AttachAsync(long sourceId, long journalEntryId, CancellationToken ct); // store JournalEntryId on the document
    Task<(string Number, string Token, string Route)> DescribeAsync(long sourceId, CancellationToken ct); // links in lists/audit
}
public interface IPostingKeyProvider
{
    string ProductCode { get; }
    IReadOnlyList<PostingKeyDef> Keys { get; }  // key, AR/EN name, normal side, control-party type, template account code
}
```

**Same-transaction guarantee (NFR-04).**
- Every module uses the one scoped `MasterDbContext` (section d), so the document, its entry, the number, the audit rows and the queue rows commit in one `SaveChanges`/transaction.
- `PostAsync`, `PostOrPendAsync` and `ReverseAsync` throw if there is no ambient transaction. This is today's `RequireTransaction()`, unchanged.
- `PostOrPendAsync` keeps the savepoint: a technical failure rolls back to it, the document is saved, and an `acc.PendingPostings` row is written (`UX_PendingPostings_Source` = one open row per document). The pending-posting worker calls the registered handler's `RebuildAsync`.

**Reversal and void.**
- `ReverseAsync(source)` finds the current un-reversed entry of that source, posts the mirror entry and links both ways (`ReversesEntryId` / `ReversedByEntryId`).
- An edit is reverse + post, as today. Manual entries keep D-05.

**Idempotency.**
- `PostAsync` for a `DocumentRef` that already has a posted, un-reversed entry returns that entry instead of posting a second one. This is checked inside the transaction while the document row is locked (`UPDLOCK` on the journal-number sequence, already taken).
- A repeated create of the document itself is idempotent through its `PublicId` (API-05), as today.

**Account mapping keys and their owners.**
- Each product owns its keys through `IPostingKeyProvider`. The stored strings stay exactly as they are (section e, E-12):
  - TRN (10): `TRN.DriverWageExpense`, `TRN.DriverPayable`, `TRN.PartnerCostExpense`, `TRN.PartnerPayable`, `TRN.TradeReceivables`, `TRN.DiscountAllowed`, `TRN.TransportRevenue`, `TRN.OtherRevenue`, `TRN.DriverAdvances`, `TRN.DriverDeductions`.
  - ACC: `ACC.SalesTaxOutput`, `ACC.SalesTaxInput`, `ACC.OpeningBalanceDifference`, `ACC.SupplierDeductions`.
- A key's prefix must equal its product code (architecture test). The mapping screen and the posting banner list only the keys of products the tenant has (active, trial or read-only).
- `EnsureReadyAsync(productCode)` checks only that product's keys plus ACC's.
- New keys of a new product appear unmapped until the company maps them. The product's template extension proposes accounts.

**Document numbering** stays in Shared (`INumberingService`, `UPDLOCK`). Document types are owned by the module (`TRN.Invoice` …, `ACC.JournalEntry` …).

**Accounting features that are transport-specific and hidden when only Accounting is active:**

| Feature | Behaviour without TRN |
|---|---|
| TRN posting keys on the mapping screen and banner | not listed, not required |
| Driver / Partner party types in the journal form and statement filters; control-account types Driver / Partner in the chart | not offered (existing data still displayed) |
| Accounting dashboard: owed to/by drivers and partners, reminders OverdueInvoices, RejectedEInvoices (invoices), UnallocatedReceipts, CarriedAdvances | absent |
| RPT-23 driver / partner / transport-customer lines | absent; receivables/payables of AR/AP documents remain |
| RPT-17 receivables ageing — transport invoices | AR documents only |
| Cash & banks: receipts / advances / settlements / partner payments as movements | only journal and AP/AR movements |
| Chart template accounts 51xx "Direct transport costs", driver/partner control accounts and their 13 TRN mappings | not created for new companies (general template) |
| Period pre-close check "draft invoices" | absent |
| Journal form notes DT-280 (driver advances / partner payments) | absent |
| E-invoicing of transport invoices / credit notes | absent; receivables e-invoicing remains |

The 4103 / 4105 template change (owner decision: "Other income"; 5206 stays Operating) is done in Stage 3, for **new** companies only. Existing companies keep their own setting.

---

## d. Database

**Principles:**
- One database, `DirectServiceMaster`, with collation `Arabic_100_CI_AI_SC_UTF8`.
- The schemas `core`, `acc`, `cfg` and `trn` are kept.
- No table, column, constraint or index is renamed. No data is moved.
- Stages 2–4 change **no** database object.

**Recommendation: one composed DbContext.**

| | One composed `MasterDbContext` (recommended) | One DbContext per module sharing a transaction |
|---|---|---|
| NFR-04 (document + entry in one transaction) | automatic: one context, one `SaveChanges` | needs a shared `DbConnection` + `UseTransaction` on every context and two `SaveChanges`. A failure between them must roll back both. Savepoints span contexts awkwardly |
| Interceptors (audit, stamps, soft delete, live changes, append-only guard) | one set, as today | registered per context; audit ordering across contexts must be designed |
| Cross-module FKs (e.g. `TRN_Invoices.JournalEntryId → acc.JournalEntries`) | stay in the model | must become "external" references in one of the contexts; risk of migration diffs dropping FKs |
| Migration history | unchanged: one history table, same 20 ids | needs a history table per context, or a split of the existing history. **Breaks continuity** |
| Query filters (Tenant/Company/SoftDelete) | one definition | duplicated per context |
| Module isolation | by assembly references (modules cannot see each other's entity types) + architecture tests | by context, plus the same tests |
| Cost of the change | small | large, risky, no benefit for one database |

**How the composed context works:**
1. `MasterDbContext` (same class name, moved to `DirectService.Shared.Infrastructure`) receives `IEnumerable<IModelContributor>` through DI. Each module's Infrastructure registers one contributor, which applies the configurations of its assembly and its `CrossModuleRelationships`.
2. The model contains **every** module's tables, always. Subscription state never changes the model: it is checked at run time (section f). One model also means one cached model per process.
3. Named query filters (`SoftDelete`, `Tenant`, `Company`) are applied by Shared for every entity implementing the scope interfaces, exactly as today.
4. Interceptors stay in Shared.Infrastructure. The audit entity name and URL token labels come from the pinned attributes (section e), not from namespaces.

**Migrations history continuity:**
- The history table stays `dbo.__EFMigrationsHistory`. EF identifies a migration by its **id** (the `[Migration("20261008105537_InitialMasterDatabase")]` attribute), not by its namespace or assembly.
- The 20 migrations and the snapshot move to the Database project (K-08; alternative: inside `DirectService.Web`). `MigrationsAssembly("DirectService.Database")` and `MigrationsHistoryTable("__EFMigrationsHistory", "dbo")` are set explicitly, so nothing depends on defaults.
- The `[DbContext(typeof(MasterDbContext))]` attributes keep pointing to the same class.
- The snapshot and Designer files contain 649 CLR type-name strings (`DirectService.Transportation.Domain.Trn.Trip`, …). They are updated mechanically to the new namespaces in the same stage that moves the types.
- **Proof in every stage:**
  - `dotnet ef migrations has-pending-model-changes` reports none.
  - A throw-away `migrations add StageN_Check` produces empty `Up`/`Down` and is deleted.
  - `database/MasterDatabase_Idempotent.sql` regenerated from the moved migrations is byte-identical apart from comments, compared with the copy at the restore tag.
  - `DatabaseVersion.cs` (start-up check) still finds 0 pending migrations on the development database.
- **No re-creation:** existing databases see no new migration in Stages 2–4. Stage 5 adds `Rev7_ProductsAndSubscriptions` (new tables only, section f).

**A new system's schema (plan):**
- A new schema per system, e.g. `pos`, created by the system's first migration, with prefixed tables (`POS_*`, following the `TRN_*` convention).
- The new migrations live in the same Database project and the same history.
- The system registers its `IModelContributor`.
- The deployment script `90_ApplicationUser.sql` gets `DENY DELETE` / `DENY ALTER` on the new schema.
- If the system has its own party types or source module code, the same migration widens `CK_EntryLines_PartyModule`, `CK_JournalEntries_SourceModule`, `CK_PendingPostings_SourceModule` (and `CK_Accounts_ControlPartyType` if needed).
- A future **shared Items/Products module** (for POS and others) would follow the same pattern with its own schema (e.g. `itm`), as a Shared-level module that systems use through its Contracts. Nothing is built now.

---

## e. Names stored in data (critical)

Rule for the rename and the split: **every value below stays byte-for-byte identical.** Each value that today is computed from a code name is replaced in **Stage 2, step 0** (before any file moves) by an explicit, pinned value. Golden tests captured at the restore tag then fail if any value changes (section i).

### e.1 Values computed from code names — must be pinned (HIGH risk)

| # | Item (today) | Current value | Stored / used in | Pin |
|---|---|---|---|---|
| E-1 | `AuditSaveChangesInterceptor.EntityTypeName` = last namespace segment upper + `.` + class name | `CORE.User`, `ACC.JournalEntry`, `CFG.SavedReport`, `TRN.Trip` … (78 entity types) | `core.AuditLogs.EntityType`; SignalR `recordChanged` / `lockChanged.entityType` (compared by ~20 pages' `LiveRefresh`); `EntityResolver` (record-lock API); `AuditDescriber.Types`; `IssuedNames` (FR-163 printed names); `ErasedRecords`; print-copy marks; `CompanyExportService.RecordType` | `[StoredName("TRN.Trip")]` on every entity; the interceptor, EntityResolver and AuditDescriber read the attribute. **Moving `Customer` to another namespace must still give `TRN.Customer`.** Golden list of all 78 names; architecture test: every `AuditableEntity` has the attribute and the value equals the golden list |
| E-2 | `EntityResolver` / `AuditDescriber` scan `typeof(AuditableEntity).Assembly` (and hard-code `Acc`/`Cfg`/`Core`/`Trn`) | — | lock and audit resolution | Build the map from the EF model's entity types (all modules) plus the attribute; no assembly scan |
| E-3 | `UrlTokenService` per-type key: HKDF info `"DST-URL:" + Type.Name` | `DST-URL:Trip`, `DST-URL:User`, `DST-URL:Company` … | every URL id; bookmarks; `core.Notifications.Link` (e-invoice submission tokens); `cfg.SavedReports.Definition` (Ref filter tokens → pins); emailed links (verify email, reset, email change, staff invitation) and `core.EmailOutbox.PayloadEncrypted`; localStorage `dst.theme.{userToken}`, `dst.nav.{userToken}` | `[TokenLabel("Trip")]` on every entity; the service uses the attribute, never `Type.Name`. Golden test: a fixed key + fixed GUID gives the same 22-character token per type as at the restore tag |
| E-4 | `core.EmailOutbox.Template` = `nameof(EmailTemplates.X)` | VerifyEmail, TwoFactorChanged, ConfirmEmailChange, EmailChangeAsked, EmailChanged, PasswordReset, Invitation, StaffInvitation, SupportDecision, SupportEnded, TenantDeleted, TestEmail, SystemEmailFailures, SecretExpiring | outbox rows; resend; per-recipient and test-mail throttles; console filter | string constants `EmailTemplateNames.*` with these exact values; `nameof` removed |
| E-5 | `CompanyExportService.RecordType` matches stored type values to CLR class names | — | company export ZIP id resolution | match through the E-1 map |
| E-6 | Audit JSON keys = C# property names | `"Name"`, `"Status"`, `"SavedReportId"` … | `core.AuditLogs.OldValues/NewValues`; `AuditVocabulary.Fields` (256), `Hidden`, `EntityFields` (50, `"TRN.Driver.PartnerId"`) | Stages 2–4 rename **no entity property** (architecture test compares property lists with the golden list) |
| E-7 | EF snapshot / Designer CLR names | 649 strings `DirectService.Transportation.Domain.*` | migration metadata only | updated mechanically; `has-pending-model-changes` must be clean (section d) |

### e.2 Fixed string keys stored in the database (pin the literals; never "tidy" them)

| # | Store | Values (unchanged) |
|---|---|---|
| E-8 | `core.AuditLogs.EntityType` written by `audit.Record(...)` (~35 `EntityType` constants) and `Action` | TRN.*/ACC.*/CFG.*/CORE.* (e.g. `CORE.SubscriptionInvoice`, `CORE.ProviderSetup`, `CORE.MaintenanceLog`); ~97 actions (Create, Update, Void, Archive, Restore, Approve, Print, Anonymise, SupportAccess, SettingChange, …) |
| E-9 | `cfg.RecordLocks.EntityType` | values from E-1 sent by the client's `RecordLockGuard EntityType="TRN.Trip"` etc. |
| E-10 | `cfg.ImportBatches.EntityType` | as E-1 (`ACC.Account`, `ACC.JournalEntry`, `TRN.Trip`, master types, budgets) |
| E-11 | `cfg.CustomFieldDefinitions.EntityType`, `cfg.StandardFieldConfigs.EntityType` | `TRN.Trip`, `TRN.Driver`, `TRN.Customer` — **stay `TRN.Customer` even with shared parties** |
| E-12 | `acc.PostingKeyMappings.PostingKey` | the 14 keys listed in section c |
| E-13 | `cfg.NumberSequences.DocumentType` | TRN.Invoice, TRN.CreditNote, TRN.Receipt, TRN.Advance, TRN.Settlement, TRN.PartnerPayment, ACC.JournalEntry, ACC.SupplierBill, ACC.Receivable, ACC.ApArPayment, ACC.ApArReturn, ACC.JournalExport |
| E-14 | `cfg.CompanySettings.SettingKey` | TRN.BillOnUnloadedWeight, TRN.PayDriverOnLoadedWeight, TRN.ShortageTolerancePct, TRN.LockTariffs, TRN.InvoiceApproval, TRN.AccrueDriverWage, CORE.PhonePattern, CORE.IbanPattern, CORE.IsDemo, `CORE.Setup.`+step, ACC.SupplierDeductionAttempted |
| E-15 | `cfg.TableColumnViews.ViewKey` | `TRN.Trips` (format regex `^[A-Z]{2,5}\.` kept) |
| E-16 | `cfg.SavedReports.Source` (+ field keys in `Definition`) | trips, invoices, credit-notes, receipts, advances, settlements, partner-payments, journal-lines, drivers, customers |
| E-17 | `cfg.DashboardPins` | SavedReportId, Measure (`rows`, `sum:<field>`, index), WarnWhen Above/Below — no type names. Standard report keys (`trips` … `reconciliation`, `supplier-statement`) appear only in URLs and stay |
| E-18 | `core.Notifications.NotificationType` / `Message` / `Link` | EINV.Rejected, EINV.SetupError, ACC.PostingFailed, CORE.ExportReady, CORE.ExportFailed, CORE.InvitationAccepted, CORE.SupportRequested, CORE.PaymentFailed; Message = parameter JSON `{"p":{…}}`; Link paths `/einvoice-submissions/{token}`, `/apar/e-invoice-submissions/{token}`, `/ledger/pending-postings`, `/settings/e-invoicing`, `/tenant/support-access`, `/tenant/subscription`, `/exports/{guid}` |
| E-19 | `acc.JournalEntries.SourceModule/SourceType`, `acc.PendingPostings.SourceModule/SourceType`, `acc.EntryLines.PartyModule/PartyType`, `acc.Accounts.ControlPartyType` (+ DB CHECKs `IN ('TRN','ACC')`, `IN ('Customer','Driver','Partner','Supplier')`) | modules TRN / ACC; types Trip, Invoice, CreditNote, Receipt, Advance, Settlement, PartnerPayment, SupplierBill, Receivable, ApArPayment, ApArReturn, Manual, Opening; parties Customer, Driver, Partner, ApArParty. **`TRN` here is a data code, not a namespace; it never changes** |
| E-20 | `core.Permissions.PermissionKey` / `GroupName` (seeded with fixed Ids and PublicIds), role names, `core.Plans.Code` | 26 keys (section a.8); groups Trips, Invoicing, Receipts, Drivers & partners, Journal, Accounting, Reports & master data, Administration, Tenant; roles Owner … Viewer; plan `TRIAL`. Also: `trn.TRN_RateHistory.SourceType` Tariff / MonthlySiteRate; e-invoice `DocumentType` Invoice / CreditNote; `cfg.EInvoiceSettings.Connector` JoFotara |

### e.3 Sign-in, cryptography, browser and files (nobody signed out, no link broken)

| # | Item | Current value | How it stays unchanged |
|---|---|---|---|
| E-21 | Blazor JS initializer file | `wwwroot/DirectService.Transportation.Web.Client.lib.module.js` (removes `#app-boot`) | **must be renamed** to `DirectService.Web.Client.lib.module.js` together with the client assembly (Stage 4), otherwise the boot overlay never goes away. E2E/visual check in Stage 4 |
| E-22 | Data Protection application name | `DirectService.Transportation` (const in `DataProtectionSetup.cs`) | kept as a **literal** forever (it isolates the key ring; changing it invalidates cookies, antiforgery tokens, the protected 2FA secrets, e-invoicing credentials, the Microsoft 365 secret and outbox payloads) |
| E-23 | Protector purposes | `DST.Users.TwoFactorSecret.v1`, `DST.EInvoice.Credentials.v1`, `DST.SystemEmail.ClientSecret.v1`, `DST.SystemEmail.Outbox.v1` | literals kept |
| E-24 | Data Protection key folder | `DataProtection:KeysPath` default `.local/keys` **relative to the content root** = `src/DirectService.Transportation.Web/.local/keys` in development; `/keys` in the container | Stage 4 (host rename): the folder is **copied** to `src/Hosts/DirectService.Web/.local/keys` before the first run, and a start-up check is added: if the key ring is empty while `core.Users` has rows, log an error naming the expected folder (Development) / refuse to start (Production). The old folder is kept until the owner confirms sign-in still works. Container path `/keys` unchanged |
| E-25 | Auth schemes, cookies, header, claims | schemes `dst`, `dst-mfa` (part of the cookie protector purpose); cookies `__Host-dst`, `__Host-dst-mfa`, `__Host-dst-af`; header `X-XSRF-TOKEN`; claims `dst:sid`, `dst:stamp`, `dst:stage` | literals kept |
| E-26 | Identity token providers | `ReplaySafeAuthenticator`, `StaffInvitation` (provider + purpose), `Default` | literals kept |
| E-27 | SignalR | path `/hubs/live`; group `co:{companyId}`; events `recordChanged`, `lockChanged`, `notification`, `supportEnded` | literals kept |
| E-28 | localStorage | `dst.theme` (before sign-in), `dst.theme.{userToken}`, `dst.nav.{userToken}`; legacy `dst.density` removed | literals kept; user token stable through E-3 |
| E-29 | User secrets | `UserSecretsId` `dst-web-7f3e2a1c` (Web), `dst-infrastructure-designtime` (design-time factory) | the same ids are put in the renamed projects (`DirectService.Web`, `DirectService.Database`), so `UrlTokens:Key` and the connection string are still found |
| E-30 | Environment variable names | `DST_` prefix (design-time), `DST_TEST_DB`, `DST_API_TEST_DB`, `UrlTokens__Key`, `DataProtection__*`, `Printing__NoSandbox` | kept |
| E-31 | URL token master key | `UrlTokens:Key` | unchanged (changing it breaks every link) |
| E-32 | Embedded resources | logo `DST.Email.Logo.png` (LogicalName); fonts `fonts/<file>` (LogicalName) read with `typeof(X).Assembly` | LogicalNames kept; the reading code moves together with the resource (logo → Platform.Infrastructure, fonts → Shared.Infrastructure); a test loads both |
| E-33 | File store | `Files:Root` default `<bin>/App_Data/files`; exports `exports/{tenant}/{company}/{userPid:N}/{job:N}/{file}` (in-memory job queue, files purged after 7 days) | path format kept; dev files under the old bin folder simply expire |
| E-34 | Mail drop folder | `.local/mail` relative to the working directory; e2e `.local/e2e/mail` | kept |
| E-35 | Launch profile | `DirectService.Transportation.Web` (`launchSettings.json`, `.claude/launch.json`) | **renamed** to `DirectService.Web` in Stage 4 together with both files and the docs ("any other profile name silently runs as Production") |
| E-36 | Routes and API paths | `/c/{company}/…`, `/console/…`, `/tenant/…`, `/api/v1/…` (521 operations) | unchanged; one redirect added (`/c/{c}/settings/tax-codes` → `/c/{c}/accounting/tax-codes`) |
| E-37 | OpenAPI document | title "Direct Service Transportation API"; schema ids = C# DTO names | title → "Direct Service API" (cosmetic, K-18); DTO names unchanged in Stages 2–4, so `docs/api/openapi-v1.json` stays identical apart from the title (diff checked) |

Golden data for E-1, E-3, E-4, E-6, E-8…E-20 is generated once from the restore tag into `tests/Architecture.Tests/Golden/*.json` and compared on every build.

### e.4 Where each value is pinned (Stage 2 step 0, done 28 Oct 2026, DT-281 … DT-285)

Every value kept its byte-for-byte value; the golden files in `tests/DirectService.Architecture.Tests/Golden/` were generated from the restore tag by the tag's own code (`tools/golden-gen`), and the 22 architecture tests compare the current code with them. Paths below are relative to `src/` unless they start with `tests/`. "Pinned in" = the single place the value is defined; "not stored" = the item turned out not to be a value stored in data or used as a key.

| # | Pinned in | Notes |
|---|---|---|
| E-1 | `[StoredName("TRN.Trip")]` on all 78 entities (`DirectService.Transportation.Domain/{Acc,Cfg,Core,Trn}/*.cs`; since Stage 2 the cfg entities and `AuditLog` are in `Shared/DirectService.SharedKernel`, the core entities except `MaintenanceLog` in `Platform/DirectService.Platform.Domain` — session 11, DT-336), read by `Domain/Common/StoredNames.cs` | used by `AuditSaveChangesInterceptor.EntityTypeName` (audit log, append-only / delete messages, `LiveChangeInterceptor` live refresh), `EntityResolver` (record locks), `AuditDescriber`, `CompanyExportService`, tenant deletion. A type without its pin throws; it never falls back to the code name |
| E-2 | `Application/Abstractions/EntityTypeMap.cs` | stored name → type from the **EF model** (all modules) + the pins; replaces the assembly scans in `EntityResolver`, `AuditDescriber` and `TenantLifecycleService.DeleteTenantAsync`. Golden: the same 73 / 76 / 68 types as at the tag |
| E-3 | `[TokenLabel("Trip")]` on all 78 entities; prefix `UrlTokenService.InfoPrefix` = `DST-URL:` (`Infrastructure/Security/UrlTokenService.cs`) | golden: a fixed key + GUID gives the tag's token for every type |
| E-4 | `Application/Notifications/EmailTemplateNames.cs` (14 constants) | `nameof` removed from `EmailTemplates`, `SystemEmailService`, `EmailOutbox`. Golden: each template method still stores its name (`VerifyAddedEmail` stores `VerifyEmail`) |
| E-5 | `CompanyExportService.RecordType` → `StoredNames.ShortOf` | the type column value without its module (`Trip` of `TRN.Trip`), never the class name |
| E-6 | not a pin — guarded by the golden property list of every entity | test `No_entity_property_is_renamed` |
| E-7 | not stored (migration metadata only) | unchanged in this session; `has-pending-model-changes` clean |
| E-8 | `EntityType` constants of the services (golden "codes": 26 values) and the E-1 pins | the ~97 action names are literals passed to `audit.Record` (never computed from code names); left as they are |
| E-9 | E-1 pins (server); 28 literals in the browser app (`RecordLockGuard EntityType="TRN.Trip"`, `LiveRefresh`, links) | test: every such literal is a golden stored name or view key |
| E-10 | the services' `EntityType` constants (`TripService`, `TariffService`, `BudgetService`, accounting and master-data imports) passed to `ImportSupport.Batch` | golden "codes" (same values as E-1) |
| E-11 | `Application/Configuration/FieldCatalog.cs` (`Trip`, `Driver`, `Customer` constants) | golden |
| E-12 | `Application/Accounting/PostingKeys.cs` | golden (14 keys) |
| E-13 | `Application/Configuration/ConfigurationKeys.cs` (`DocumentTypes`) | golden |
| E-14 | `Application/Configuration/ConfigurationKeys.cs` (`SettingKeys`) | golden |
| E-15 | `ViewKey="TRN.Trips"` in `Web.Client/Pages/Trips/TripListView.razor` | golden |
| E-16 | `Application/Reports/ReportSources.cs` (source keys and field keys) | golden: the 10 sources; every field key of the tag still exists |
| E-17 | not stored as type names (pins hold ids and measures; standard report keys appear in URLs only) | nothing to pin |
| E-18 | `Application/Collaboration/NotificationService.cs` constants + `NotificationTexts` catalogue; link paths in `NotificationLinks.cs` (since Stage 2 session 14: the `CORE.*` types on Platform's `NotificationService` / `NotificationTexts`, the module's on `TransportationNotificationTypes` / `TransportationNotificationTexts`, links in `NotificationLinks` and `TransportationNotificationLinks`) | golden (8 types) |
| E-19 | `Application/Accounting/PostingKeys.cs` (`AccountingCodes`: modules, source and party codes, CHECK lists) and the `SourceType` constants of the document services | golden; `TRN` / `ACC` are data codes, never namespaces |
| E-20 | `Contracts/Security/PermissionKeys.cs` (`PermissionKeys`, `PresetRoles`); seed rows `Infrastructure/Persistence/Seed/ReferenceDataSeed.cs` (since Stage 2 session 11 split by owner, DT-337: `core.*` keys and the TRIAL plan in `Platform/DirectService.Platform.Infrastructure/Persistence/Seed/PlatformReferenceDataSeed.cs`, the module's keys in its `ReferenceDataSeed` through `IPermissionSeedProvider`; one `HasData` with the same values) | golden: 26 keys with seed Ids, PublicIds and groups; plan `TRIAL`; preset grants |
| E-21 | the file name `Web.Client/wwwroot/DirectService.Transportation.Web.Client.lib.module.js` (= assembly name, cannot be a constant) | test: the file exists under the browser app's assembly name; renamed with the assembly in Stage 4 |
| E-22 | `DataProtectionSetup.ApplicationName` (`Web/Security/DataProtectionSetup.cs`) | golden |
| E-23 | `Infrastructure/Security/DataProtectionPurposes.cs` (4 purposes; since Stage 2 session 11 `Shared/DirectService.Shared.Infrastructure/Security/DataProtectionPurposes.cs`, DT-340) | used by `UserStore`, `EInvoiceStore`, `EmailOutbox`; test forbids an inline `CreateProtector("…")` |
| E-24 | `DataProtectionSetup.DefaultKeysPath` = `.local/keys` | the folder itself untouched (read only) |
| E-25 | `AuthSchemes`, `AppClaims`, new `AuthCookies` (`Web/Security/AuthSetup.cs`) | cookies, antiforgery cookie and header; used by `AuthSetup`, `Program.cs`, `OpenApiSetup`; golden reads the cookie names from the real authentication set-up |
| E-26 | `ReplaySafeAuthenticatorTokenProvider.Name` (since Stage 2 session 11 in `Platform.Infrastructure/Identity`), `StaffService.InvitationTokenProvider` / `InvitationPurpose` | already constants; `Default` is the framework's provider name |
| E-27 | `Contracts/Collaboration/LiveHubNames.cs` (path, group prefix, hub methods, 4 events) | shared by `LiveHub` / `SignalRNotifier` and the browser's `LiveUpdates`; hub methods now carry `[HubMethodName]` with the same names |
| E-28 | `Web.Client/Services/PrefKeys.cs` (`dst.`, `theme.`, `nav.`); `wwwroot/js/ui.js` (`"dst." + key`), `wwwroot/js/theme.js` (`dst.theme`) | test reads both scripts |
| E-29 | `<UserSecretsId>` in `Web/…Web.csproj` and `Hosts/DirectService.Database/DirectService.Database.csproj` (moved from Infrastructure in Stage 2 session 3, same value) | golden |
| E-30 | not stored — configuration and environment names (`DST_` prefix in `DesignTimeDbContextFactory`, `DST_TEST_DB`, …) | unchanged; not data |
| E-31 | `UrlTokenOptions.Section` (`UrlTokens`) + `Key` | unchanged |
| E-32 | `EmailLogo.ResourceName` (`Infrastructure/Email/MimeMessageBuilder.cs`; since Stage 2 session 11 `Platform/DirectService.Platform.Infrastructure/Email/MimeMessageBuilder.cs` with the resource, DT-340) = the csproj LogicalName `DST.Email.Logo.png`; fonts `fonts/<file>` (Application csproj) | golden: the manifest resources of the infrastructure assembly |
| E-33 | `ExportService.Root` = `exports/` (since Stage 2 session 10: `Shared.Infrastructure/Collaboration/ExportService.cs`, value and folder format defined in SharedKernel `ExportFiles`, DT-334; before: `Application/Collaboration/ExportService.cs`, used by the folder format, `Billing` storage usage and `ExportWorker`); `LocalFileStore.DefaultRoot` = `App_Data/files` | golden: the folder of a sample export |
| E-34 | `SystemEmailOptions.DefaultDropFolder` = `.local/mail` (`Application/Platform/SystemEmail.cs`) | golden |
| E-35 | not stored — the launch profile name (`Properties/launchSettings.json`, `.claude/launch.json`) | renamed in Stage 4 |
| E-36 | routes — golden OpenAPI (449 paths, 521 operations) | |
| E-37 | `docs/api/openapi-v1.json` — golden SHA-256 (LF) | re-exported from the host on 28 Oct 2026: byte-identical |

Also found **not stored** (left as they are): the ZIP entry names of the company export (table names, written once, never read back), the payment gateway's application-lock name `dst:…` (a SQL lock of the running process) and exception type names in e-mail delivery error texts.

---

## f. Products, subscriptions and trials (Stage 5)

### f.1 Principles
- A **product** is a system the platform sells: `TRN` (Direct Service Transportation) and `ACC` (Direct Service Accounting), and later others.
- **Accounting is used by every system.** A tenant with TRN always has the accounting features it needs for posting, the chart, journals and financial reports. Recommendation K-04: TRN **includes** ACC. ACC can also be subscribed alone.
- The tenant's existing subscription (`core.Tenants.Status`, `PlanId`, `TrialEndsAt`, `CurrentPeriodEnd`, `GraceEndsAt`, `CancelledAt`, `RetentionEndsAt`, `core.SubscriptionInvoices`) and the hourly lifecycle job stay **authoritative for the plan**. Per-product rows record only what the plan does not cover: per-product trials and manual add-ons. There is never a second copy of the same date.

### f.2 Data model — database design revision 7 (new tables only, `core` schema, standard columns)

| Table | Columns (besides the standard Id, PublicId, audit, soft delete, RowVersion) | Rules |
|---|---|---|
| `core.Products` | Code VARCHAR(10) (`TRN`, `ACC`), NameAr, NameEn, Status VARCHAR(12) (`Available`, `ComingSoon`, `Hidden`, `Retired`), SortOrder, IncludesProductCodes VARCHAR(100) NULL (e.g. TRN → `ACC`), CanSubscribeAlone BIT | `UX_Products_Code`; platform-wide (no tenant) |
| `core.ProductTrialOffers` | ProductId FK, IsEnabled BIT, TrialDays SMALLINT (1–90), OncePerTenant BIT default 1, OfferTextAr/En NVARCHAR(300) NULL, ChangedAt/By | one row per product (`UX_ProductTrialOffers_ProductId`); every change audited (`TrialOfferOn`, `TrialOfferOff`, `TrialOfferChanged`) |
| `core.PlanProducts` | PlanId FK, ProductId FK | `UX_PlanProducts_PlanId_ProductId`; a plan contains one or more products |
| `core.PlanLimits` | PlanId FK, LimitKey VARCHAR(60) (`TRN.TripsPerMonth` …), Value INT | for limits of **new** products. The four existing `core.Plans` columns stay authoritative for companies, users, storage and trips |
| `core.TenantProducts` | TenantId FK, ProductId FK, Source VARCHAR(10) (`Plan`, `Trial`, `Manual`), Status VARCHAR(12) (`Trial`, `Active`, `Ended`), TrialStartedAt, TrialEndsAt, ActiveUntil (Manual only), EndedAt, EndReason NVARCHAR(300) | `UX_TenantProducts_TenantId_ProductId` (filter not deleted). Rows are never deleted, so the trial history is kept (once per tenant). `Source = Plan` rows have **no dates**: their state is the tenant's |
| `core.SubscriptionActions` | TenantId FK, Action VARCHAR(20) (`Activate`, `Renew`, `Extend`, `ChangePlan`, `StartTrial`, `EndTrial`, `ExtendTrial`, `AddProduct`, `RemoveProduct`, `Suspend`, `Reactivate`), ProductId NULL, FromPlanId / ToPlanId NULL, PeriodFrom / PeriodTo NULL, Amount DECIMAL(18,3) NULL, Currency CHAR(3), PaymentMethod VARCHAR(20) NULL (`Cash`, `BankTransfer`, `CliQ`, `Cheque`, `CardOffline`, `Complimentary`, `Gateway`), PaymentReference NVARCHAR(100) NULL, ReceivedAt DATE NULL, SubscriptionInvoiceId FK NULL, Notes NVARCHAR(500) NULL, ByStaffUserId | append-only (`IAppendOnly`); a register of every manual action, shown to staff and (without notes) to the tenant Owner |

Revision 7 needs owner approval before Stage 5 (it is new tables only; nothing is renamed or moved). `SubscriptionInvoices` is unchanged: a manual payment issues the normal `SUB-` tax invoice and marks it paid (K-17).

### f.3 Effective access per product (one function, used by the API, the menu and the jobs)

```
ProductState(tenant, product) =
  tenant.Status in (Deleted)                                      → None
  product included by another active/trial product (TRN → ACC)    → state of that product
  TenantProducts row Source=Plan and the tenant's plan contains it → tenant state: Trial/Active/PastDue → Full; Suspended/Cancelled → ReadOnly
  TenantProducts row Source=Trial, now < TrialEndsAt               → Full (Trial)
  TenantProducts row Source=Manual, now < ActiveUntil              → Full
  any row that has ended (Ended, trial or ActiveUntil passed)      → ReadOnly (data kept, K-05)
  no row                                                          → None (offer shown if the trial offer is on and not used)
```
The tenant-level write guard (Suspended / Cancelled / support session / archived company) stays in force on top of this.

### f.4 Free-trial offers managed by platform staff
- **Console → Products** lists each product with its status and trial offer (on/off, days, once per tenant, offer text). Turning an offer on or off is audited.
- **Offer on.** Every tenant that does not have the product (no `TenantProducts` row, or `OncePerTenant` = 0) sees:
  - a menu entry under "More systems";
  - a card on Tenant → Subscription with **"Start free trial"** (Owner only, `core.tenant.manage`, K-06).
  - Starting the trial creates a `TenantProducts` row (Source Trial, Status Trial, `TrialEndsAt` = now + TrialDays), runs the product's `ICompanyProvisioner` for every active company (chart extension, layouts, number sequences), records `StartTrial` in `SubscriptionActions` and the audit log, and refreshes the menu live (SignalR to the tenant's users).
- **Offer off.** The entry and card disappear for tenants that have not started a trial. **Tenants already in a trial keep it until it ends** (recommendation; K-01).
- **Staff actions on one tenant:** end a trial now (with a reason), or extend it (new end date). Both are audited and appear in `SubscriptionActions`.
- **Rules:**
  - once per tenant per product (K-02);
  - not while the tenant is Suspended, Cancelled or PastDue;
  - not for a product already included by the plan;
  - a trial of TRN also gives ACC (included) for its length, if ACC is not already active.
- **When a trial ends**, the hourly lifecycle job sets `Status = Ended` and the product becomes read-only (f.6). It notifies the Owners (new notification type `CORE.TrialEnded`) 3 days before and on the day.
- The existing tenant-level trial (`Tenants.Status = Trial`, `App:TrialDays` 14) remains **the trial of the plan** for new sign-ups. Sign-up keeps creating the tenant on the TRIAL plan; the TRIAL plan's `PlanProducts` decide which products the new tenant tries (K-14: TRN + ACC, as today).

### f.5 Plans, limits and manual subscription management (payment gateway later)
- **Console → Plans:** each plan gets a "Products" checklist (`PlanProducts`) and the limits of those products. The existing four limits stay; product limits are registered by modules through `ILimitCheck`, and limits are still checked only on creation (BR-013).
- **Console → Tenant → Subscription tab:**

| Action | Effect | Recorded |
|---|---|---|
| Activate (from Trial / Suspended / Cancelled-in-retention) | Status Active, `CurrentPeriodEnd` = chosen end, plan chosen | `SUB-` invoice issued and marked Paid with method + reference; `SubscriptionActions`; audit |
| Renew | `CurrentPeriodEnd` += one billing cycle | same |
| Extend (days, free or paid) | `CurrentPeriodEnd` or `TrialEndsAt` += days | `SubscriptionActions` (Complimentary when free); audit |
| Change plan | `PlanId` = new plan; downgrade refused if the usage exceeds the new limits (BR-012, existing rule) | pro-rated `SUB-` invoice if upgrade (existing calculation) or credit (D-11) |
| Add / remove a product (Manual) | `TenantProducts` Source Manual with `ActiveUntil` | invoice + action |
| Start / end / extend a product trial | f.4 | action + audit |
| Suspend / reactivate | existing console actions | action + audit |

- **Payment method** is required for paid actions: Cash, Bank transfer, CliQ, Cheque, Card (offline), Complimentary (K-16). The payment reference is required for everything except Cash and Complimentary.
- The **Payments register** (Console) lists every manual payment with filters and export.
- The **gateway later:** `IPaymentGateway` stays as it is (`CreateCheckoutAsync`, `VerifyWebhook`, `ChargeAsync`). A gateway payment calls the same `BillingService.ApplyPaymentAsync` with method `Gateway` and the gateway reference. The manual and gateway paths share one code path, so connecting the gateway changes only the provider class and configuration.

### f.6 When a product's trial or subscription ends (recommendation K-05)
- **Read-only, data kept.**
  - The product stays in the menu with a badge "Ended — view only".
  - A banner shows "Subscribe" / "Contact us".
  - Lists, records, reports, print and export keep working (BR-013 spirit).
  - Every write endpoint of that product returns 403 `product.readOnly` ("Your {product} subscription has ended; you can view, print and export.").
- Postings already in Accounting stay. Accounting itself stays fully usable if ACC is active on its own.
- Nothing is deleted while the tenant exists. Deletion follows the existing tenant lifecycle only (cancel → 90-day retention → anonymise).
- Rejected alternative: hiding the product. Users would lose sight of their own records and links in the audit log and notifications would lead nowhere.

### f.7 Menu and permission behaviour per product
- Each module UI registers its menu groups with their product code (`INavContributor`). The composed menu shows:
  1. the groups of products in state Full, Trial or ReadOnly (Trial shows "Trial — n days left"; ReadOnly shows the badge);
  2. shared groups (Dashboard, Reports, Settings, Help) with only the items whose product is present;
  3. "More systems": products with an active offer the tenant may start, each with "Start free trial".
- The data comes from `GET /api/v1/me/products` (new, platform) — `[{code, name, state, trialEndsAt, offer}]`. It is cached in `AppSession` and refreshed by a SignalR `productsChanged` event.
- **Permissions:**
  - Effective permission = the role grants it **and** the key's product is Full (all keys) or ReadOnly (view keys only).
  - The product of a key is its prefix (`trn.` → TRN, `acc.` → ACC, `core.` → platform), which is pinned; `trn.reports.view` is cross-product (U-4).
  - The role editor shows only the groups of the tenant's products. Grants on other products' keys stay stored, so a re-subscription restores them.
  - The API enforces this in the existing permission handler (one more check per request, from the cached product state). The UI hiding is only convenience.
- Endpoints declare their product: `.RequireProduct("TRN")` on each module's route group. An architecture test fails if a module endpoint has no product.

### f.8 Provider console and customer screens (new or changed)

| Screen | Content |
|---|---|
| Console → **Products** (new) | product list, status, trial offer on/off, days, once per tenant, offer text, counts (tenants with the product, in trial) |
| Console → **Plans** (changed) | products checklist + product limits |
| Console → **Tenants → tenant** (changed) | Subscription tab: plan, status, dates, products with their state; the actions table of f.5; the action register |
| Console → **Payments** (new) | manual payment register |
| Tenant → **Subscription** (changed) | per-product cards (state, trial days left, "Start free trial" when offered), plan products, invoices; "Change plan" / "Pay" go through the gateway when connected; until then "Contact us to subscribe" with the platform's contact details |
| Side menu (changed) | f.7 |
| Accounting → **Setup & control → Tax codes** (moved) | from Settings; route `/c/{c}/accounting/tax-codes`; permissions `acc.acct.view` (view) and `acc.acct.setup` (change) instead of `core.settings.manage`; `/c/{c}/settings/tax-codes` redirects; API `/api/v1/companies/{c}/tax-codes` kept + alias under `/accounting/tax-codes`. Done in Stage 3 |

### f.9 Existing tenants (carried over unchanged)
The Stage 5 migration runs these data steps. They **insert** rows only; nothing existing is changed or moved.
1. Insert products TRN (includes ACC) and ACC, both `Available`, with trial offers **off** (staff turn them on).
2. Every existing plan (including TRIAL) gets `PlanProducts` TRN + ACC.
3. Every tenant gets `TenantProducts` TRN and ACC with Source `Plan` (no dates).

Every tenant therefore keeps working exactly as today. A tenant in its trial stays in its trial (`Tenants.TrialEndsAt` unchanged), a paying tenant stays on its plan and period, and suspended or cancelled tenants keep their state.

Rollback: Down drops the new tables while no manual action or product trial exists. Once one exists, it throws (`THROW 50300`, the established pattern).

---

## g. UI composition

- **One browser app** (`DirectService.Web.Client`, Blazor WebAssembly).
  - It references `DirectService.Platform.UI`, `DirectService.Accounting.UI`, `DirectService.Transportation.UI` (Razor class libraries) and `DirectService.Shared.UI`.
  - The router gets `AdditionalAssemblies` = the module UI assemblies; the host's `AddAdditionalAssemblies` does the same.
- **Routes unchanged**: `/c/{Company}/…`, `/console/…`, `/tenant/…`. Each page keeps its `@page` lines. The only added route is the tax-codes redirect.
- **Each module UI library** contains:
  - its pages;
  - its `UiText.<Module>` partials (AR/EN in code, as today);
  - its `INavContributor` (groups, items, permissions, product code);
  - its `IHelpTopicProvider`;
  - its `ILinkResolver` (search hits, notifications, drill-downs, audit links);
  - its lookups (`TripLookups`, `AccountingLookups`);
  - its report catalogue entries.
- **It registers** through one `services.Add<Module>Ui()` call in `Web.Client/Program.cs`.
- **Shared design system** (Shared.UI):
  - `tokens.css` (colour tokens, light/dark themes, RTL/LTR) and `app.css`;
  - fonts and the icon sprite;
  - the components and the shell.
  - Module libraries use only tokens and Shared components (no colour literals, test-enforced as today). Static files stay at the root URLs they have today (`css/app.css`, `js/ui.js`, `img/icons.svg`), so the Shared.UI files are served by the Web.Client project, not at `_content/…`, to keep every URL and cache entry the same.
- **Menu by product**:
  - f.7. Before Stage 5, the product provider returns "every product Full" for all tenants, so Stage 4 can deliver the composed menu with no behaviour change.
  - Product hiding is UI only. The API checks are authoritative (f.7).
- **Not done (K-20):** lazy loading of module assemblies per product. All modules are small and cached; it can be added later without changing routes.

---

## h. Rename plan

| Item | From | To | Stage |
|---|---|---|---|
| Solution | `DirectService.Transportation.slnx` | `DirectService.slnx` (folders Shared, Platform, Modules, Hosts, tests) | 4 |
| Shared projects | (new) | `DirectService.SharedKernel`, `.Shared.Infrastructure`, `.Shared.UI` | 2 |
| Platform projects | (new; code moved out of the Transportation projects) | `DirectService.Platform.{Domain,Application,Infrastructure,Contracts,UI}` | 2 |
| Accounting projects | (new; code moved) | `DirectService.Accounting.{…}` | 3 |
| Transportation projects | `src/DirectService.Transportation.{Domain,Application,Infrastructure,Contracts}` (holding everything) | `src/Modules/Transportation/DirectService.Transportation.{Domain,Application,Infrastructure,Contracts,UI}` (transport only). Assembly names **stay** `DirectService.Transportation.*`; folders move | 4 |
| Host | `DirectService.Transportation.Web` | `src/Hosts/DirectService.Web` (assembly `DirectService.Web`, Dockerfile `ENTRYPOINT dotnet DirectService.Web.dll`) | 4 |
| Browser app | `DirectService.Transportation.Web.Client` | `src/Hosts/DirectService.Web.Client` (+ `lib.module.js` renamed, E-21) | 4 |
| Migrations | in Infrastructure | `src/Hosts/DirectService.Database` (K-08) | 2 |
| Namespaces | `DirectService.Transportation.{Domain.Core,…}` | `DirectService.Platform.Domain`, `DirectService.Accounting.Domain`, … (entity namespaces no longer carry `Core/Acc/Cfg/Trn`; E-1 pins make that safe) | with each move |
| Tests | `tests/DirectService.Transportation.*.Tests` | `tests/DirectService.{Architecture,Platform,Accounting,Transportation,Api,UI}.Tests`, `tests/DirectService.Testing` | 2–4 (i) |
| CI | `.github/workflows/ci.yml` (slnx, `tests/DirectService.Transportation.$p.Tests` loops) | new solution and project list; Architecture.Tests first | each stage |
| Dockerfile | slnx, Web csproj, entrypoint | new names; volumes `/keys`, `/files` unchanged | 4 |
| Launch | profile `DirectService.Transportation.Web`, `.claude/launch.json` | `DirectService.Web` | 4 |
| e2e support | hard-coded project paths in `support/env.ts`, `global-setup.ts`, `text.ts`, `specs/shortcuts.spec.ts`, `side-menu.spec.ts`, `visual.spec.ts` | new paths | 4 |
| Tools | `tools/design/gen_efcore.py` (`NS`), `tools/perf/*`, `tools/trace/*` | paths updated; `gen_efcore.py` marked retired (it must never be re-run anyway) | 4 |
| Docs | `Setup_and_Run.md`, `BUILD_STATUS.md` "Resume here", `00_START_HERE.md`, this folder | new paths | 4–5 |
| Product names in texts | "Direct Service Transportation" in page titles, emails, OpenAPI title | platform: "Direct Service"; product names where a product is meant (K-15, K-18) | 4 |

**Deliberately NOT renamed:**
- the database `DirectServiceMaster`, schemas, tables, columns, constraints, indexes and views;
- the migration ids and the history table;
- the `MasterDbContext` class name;
- the Data Protection application name and purposes;
- cookie names, auth schemes, claims and the antiforgery header;
- the SignalR path, groups and events;
- localStorage keys;
- URL token labels and the master key;
- all stored keys (section e.2);
- permission codes, role names and the plan code;
- `UserSecretsId`s and environment variable names;
- routes and API paths;
- the Transportation assembly names (`DirectService.Transportation.*` is the module's correct final name);
- the e2e database names (`DirectServiceMaster_E2E`, `_Tests`, `_ApiTests`, `_PerfTests`);
- the git repository folder `D:\Direct Service Transportation System` (renaming it is the owner's choice; nothing depends on it).

---

## i. Tests

### i.1 Today
About **1,400 test cases** (≈ 730 test methods; theories expand):

| Project | Tests | Kind |
|---|---|---|
| Api | ≈ 500 | integration on SQL Server, one tenant per test |
| Application | 473 | unit |
| UI | 344 | bUnit |
| Infrastructure | 68 | database rules |
| Domain | 4 | unit |

The e2e suite (Playwright, 12 specs) runs by hand only.

### i.2 Where the test classes go

| Destination | Classes |
|---|---|
| **DirectService.Platform.Tests** (unit + DB) | FoundationUnitTests (roles, password policy part), EmailTemplateTests, SubscriptionUnitTests, Email/GraphEmailTransportTests |
| **DirectService.Accounting.Tests** | AccountingUnitTests, ApArUnitTests, BudgetMathTests, BudgetProfitLossUnitTests, JournalTaxUnitTests, Persistence/PostingEngineTests |
| **DirectService.Transportation.Tests** | EInvoiceUnitTests, InvoiceUnitTests, MasterDataUnitTests, PricingUnitTests, TripUnitTests, ReportUnitTests, EInvoicing/JoFotaraTests (→ Accounting.Tests once the connector moves) |
| **Shared tests** (in Platform.Tests until a Shared.Tests is worth it) | FieldLabelTests, NotificationUnitTests, TenantFilterGuardTests, EntityDefaultsTests (Domain), Audit/AuditDescriberTests, PdfRendererTests, Persistence/DataRulesTests, Persistence/NumberingTests, Security/UrlTokenOptionsTests |
| **DirectService.Api.Tests** (stays one project; it tests the composed host) | all 54 classes. P: AccountChange, AccountPage, Administration, CodeReuse, ExpiredForm, Identity, Onboarding, Platform, SessionLifetime, Staff, SupportAccess, SystemEmail, TwoFactorManagement, TwoFactorPolicy, UserNameSignIn. A: Accounting, AccountingDashboard, ApAr, Budget, BudgetProfitLoss, JournalDraft, JournalTax, JournalTaxUpgrade, JournalTemplate, LedgerReport, Reconciliation. T: Cash, EInvoice, Invoice, MasterData, PaidPartnerTrip, PaidTrips, PerformanceCheck, PreviousBalance, Pricing, SettlementRepayment, SettlementUpgrade, TripListMigration, TripPerformance, Trip. X: Collaboration, DataProtectionSetup, FieldMessage, HostPage, ImportTemplate, ReleaseRegression, Report, Security, TenantIsolationSweep. Classes are grouped into `Platform/`, `Accounting/`, `Transportation/`, `CrossCutting/` folders; the isolation sweep and endpoint-coverage tests keep finding every endpoint automatically |
| **DirectService.UI.Tests** (stays one project; references every UI library) | all 47 classes, grouped the same way; `UiTestBase` / `PageTestBase` move to `DirectService.Testing`. Every event still goes through `UiTestBase.Act` |
| **DirectService.Testing** (helper library) | `DatabaseFixture`, `ApiFactory`, `SystemEmailFactory`, `FakeGraph`, `PartBSupport`, UI fake API, golden-file helpers |
| **e2e** | unchanged specs; support paths updated in Stage 4 |

Rule for each move: **namespace and file move only, no assertion changed.** The count of passed tests per stage must equal the count before the stage, plus the new tests.

### i.3 New Architecture.Tests (run first in CI)
1. **Layer and module rules** of a.2. They read `csproj` `ProjectReference`s and the compiled `GetReferencedAssemblies()`, so no new package is needed:
   - Domain → SharedKernel only;
   - Contracts are WASM-safe;
   - no module references another module's Domain, Application, Infrastructure or UI;
   - Shared references no Platform or module;
   - UI never references Application, Infrastructure or Domain;
   - exception E-1 allowed only in `*.Infrastructure` and only for `Tenant`, `Company` and `User`.
2. **Stored names (golden):** E-1 entity names, E-3 token labels (fixed key + GUID → token), E-4 email template names, E-6 entity property lists, E-8…E-20 stored keys (setting keys, document types, posting keys, notification types, permission keys + groups + seed Ids, field-catalog entity types, view keys, report-builder source keys, source/party codes).
3. **Crypto and browser literals:** Data Protection application name and purposes, cookie and scheme names, hub path and group format, localStorage keys.
4. **Ownership:**
   - every posting key, setting key, notification type and permission key has the prefix of its product;
   - every module endpoint has a permission and a product;
   - every entity is configured by exactly one module.
5. **Database:** the model has no pending changes against the snapshot (Stages 2–4: none at all).

### i.4 CI changes
- Solution name, project loops (`Domain Application UI` → `Architecture Platform Accounting Transportation UI`; DB loop `Infrastructure Api` → `Platform Accounting Transportation Api` with `DST_TEST_DB` / `DST_API_TEST_DB`).
- A step `dotnet ef migrations has-pending-model-changes`.
- The OpenAPI lint is unchanged.
- The owner pushes; nobody waits on GitHub runs in a working session.

---

## j. Stage plan

Common to every stage:
- Restore point: tag `pre-modules-final-2026-10-27` (= the code before Stage 2). Each stage also ends with its own tag (`stage2-done-…`).
- Local commits only; the owner pushes.
- No E2E unless the owner asks.
- Each stage ends with: build, all .NET tests green (foreground, with time limits), Architecture.Tests green, `has-pending-model-changes` clean, OpenAPI diff (empty apart from the title in Stage 4), one focused visual check (`VISUAL_ONLY`) where screens moved, a `BUILD_STATUS.md` update and a DT log entry.

### Stage 2 — Shared + Platform split (behaviour unchanged) — est. 50–65 h
- **Scope:**
  - **Step 0 (pins):**
    - `[StoredName]` and `[TokenLabel]` on all 78 entities;
    - interceptor, EntityResolver, AuditDescriber, UrlTokenService, CompanyExportService and email template names switched to the pins;
    - golden files generated from the restore tag;
    - Architecture.Tests project with the golden tests.
  - New projects SharedKernel, Shared.Infrastructure, Shared.UI, Platform.{Domain, Application, Infrastructure, Contracts, UI}, Database, Testing.
  - Moves:
    - core.* entities, configurations and services (a.4–a.6);
    - the Shared building blocks;
    - the composed `MasterDbContext` with `IModelContributor`s;
    - migrations to the Database project (ids unchanged, `MigrationsAssembly` set, same `UserSecretsId`);
    - the platform UI pages and components to the Platform.UI / Shared.UI libraries, with the menu still static.
  - Contribution interfaces needed by Platform: `ICompanyProvisioner`, `IDemoDataContributor`, `ISetupStepProvider`, `IUsageMeter`, `ITenantDataEraser`, `IPermissionCatalog`, `INotificationTypeProvider`. Their transport and accounting implementations live in the old Transportation projects for now.
- **Must stay unchanged:** routes, API, database, every stored name, sign-in (cookies, 2FA, sessions), the menu, screens, texts, the OpenAPI document.
- **Proven by:** all existing tests; golden tests; `has-pending-model-changes`; idempotent script identical; sign-in in the browser with the existing development key ring; one visual check of the console and the tenant screens.
- **Risks:**
  - EF model drift from moved configurations (caught by the pending-changes check);
  - the assembly scans in a.12/U-11 (replaced by EF-model scans);
  - `HasData` seed moved but value-identical.
- **Rollback:** `git reset --hard pre-modules-final-2026-10-27` (no database change in this stage).

### Stage 3 — Accounting module + posting contract — est. 65–85 h
- **Scope:**
  - `DirectService.Accounting.*` projects. acc.* entities and configurations, `cfg.EInvoiceSettings`, `core.MaintenanceLogs` and every accounting service, endpoint, worker (pending postings, e-invoicing engine) and page move here.
  - `IAccountingPosting`, `IPostingSourceHandler`, `IPostingKeyProvider`, `IPartyProvider` / `IPartyDirectory`, `IPartyBalanceSource`, `IReconciliationSource`, `IPeriodCloseCheck`, `ICashMovementSource`, `IEInvoiceDocumentSource`, `IChartTemplateExtension`. Transport code switches from `PostingEngine` / `Set<JournalEntry>` to the contracts.
  - Cross-module navigations replaced by ids.
  - Shared customers (b): ensure-party on save, plus the one-time, idempotent step that links every existing transport customer to a party (K-07b, decided 28 Oct 2026).
  - Tax codes move to Accounting → Setup & control (route, permissions, redirect).
  - Chart template: 4103 / 4105 "Other income" for new companies; general template + transport extension (K-13).
- **Must stay unchanged:** every posting (same lines, accounts, parties, numbers, descriptions), pending-posting behaviour, reversal links, reports RPT-13…24 and P&L / TB / BS figures, the DT-226…280 rules, the API (except the tax-codes alias).
- **Proven by:** all Accounting and Transportation API tests (posting, reconciliation RPT-23, previous balances, paid trips, budgets, journal tax); a new contract test suite (idempotent post, reverse, no ambient transaction → refused, pending + rebuild by handler); a golden "posting snapshot" test that posts each document type in a fixed company and compares the entry lines with the snapshot taken before the stage; architecture tests (Transportation does not reference Accounting.Domain); a visual check of the accounting screens and tax codes.
- **Risks:**
  - hidden direct reads of `acc` tables from transport code (the compiler finds them once the reference is removed);
  - statement merging of linked customer parties;
  - performance of contribution calls in reports (kept set-based: providers return `IQueryable` projections inside the same context, not per-row calls).
- **Rollback:** reset to the Stage 2 tag (no database change; the tax-code template change touches only new companies).

### Stage 4 — Transportation module + UI composition + menu by product — est. 55–75 h
- **Scope:**
  - Remaining code to `src/Modules/Transportation/DirectService.Transportation.*` (+ UI library).
  - Hosts renamed to `DirectService.Web` / `DirectService.Web.Client` (E-21 lib.module.js, E-24 key folder copy + start-up check, E-29 user secrets, E-35 launch profile).
  - Solution, Dockerfile, CI and e2e paths, tools, docs.
  - Composed menu, help, links, search and dashboard through the contributors, with the product provider returning "all products Full".
  - Product-name texts (K-15/K-18).
- **Must stay unchanged:** all routes and screens, the menu as the user sees it, live refresh, record locks, saved column views, pins, saved reports with filters, emailed links, notifications' links, sign-in without re-authentication (same key ring).
- **Proven by:** all tests; golden tests; visual check of the menu and one screen per module (AR/EN, light/dark); manual check: sign in with an existing session cookie after the rename (must stay signed in), open an old bookmarked trip link, open a notification link, run a saved report with a driver filter.
- **Risks:**
  - the key ring path (E-24);
  - the boot overlay (E-21);
  - static asset URLs (Shared.UI assets served at the same root paths);
  - CI paths.
- **Rollback:** reset to the Stage 3 tag. The renamed key folder is a copy; the old one is untouched.

### Stage 5 — Products, subscriptions, per-product trials, manual activation, starter module, guides — est. 70–95 h
- **Scope:**
  - Database revision 7 (f.2) + migration `Rev7_ProductsAndSubscriptions` with the f.9 data steps; idempotent script regenerated.
  - Product state service (f.3), `RequireProduct` on route groups, permission handler change, `GET /me/products`, SignalR `productsChanged`.
  - Trial offers and the lifecycle job for product trials (`CORE.TrialEnded` notification).
  - Console Products / Plans / Tenant subscription tab / Payments; tenant Subscription page; menu "More systems".
  - Manual actions through `BillingService` with payment method and reference; read-only behaviour on end.
  - A **starter module** template (`src/Modules/_Starter`, excluded from the solution build; it holds one entity, one endpoint, one page, one posting, one permission, help and tests) and the finalised guides `02_How_To_Add_A_New_System.md` and `00_START_HERE.md`.
- **Must stay unchanged:** existing tenants' access, plan, status, dates, invoices and limits; sign-up flow; gateway interface.
- **Proven by:**
  - new API tests:
    - offer on → visible, start → Full, end → ReadOnly + `product.readOnly` on writes, offer off → hidden for non-starters but kept for tenants in a trial;
    - once per tenant;
    - staff end/extend;
    - each manual action creates the invoice, register row and audit;
    - downgrade refusal;
    - an existing tenant migrated unchanged (Trial, Active, PastDue, Suspended, Cancelled);
    - an ACC-only tenant has no transport menu, keys, mappings or dashboard parts;
  - UI tests for the console and menu; migration up/down test; visual check of the new screens.
- **Risks:**
  - two sources of truth (avoided by f.1: plan products have no dates);
  - permission cache invalidation on product change;
  - Accounting-only tenants in screens that assumed transport data (the Stage 3 contributors make them optional).
- **Rollback:** migration Down while no product action exists (otherwise refused), then reset to the Stage 4 tag.

**Total: about 240–320 hours** (Stage 2: 50–65, 3: 65–85, 4: 55–75, 5: 70–95). The owner reviews and accepts after each stage before the next starts.

---

## k. Owner decisions (decided 26/27 Oct 2026, recorded 28 Oct 2026)

All K-01 … K-23 are approved as recommended, **except K-07b** (changed: link every existing customer now, in Stage 3).

| # | Question | Recommendation | Decision |
|---|---|---|---|
| K-01 | When staff turn a product's trial offer **off**, do tenants already in that trial keep it? | **Yes**, until it ends. Staff can end or extend one tenant's trial manually | Approved as recommended |
| K-02 | Can a tenant try the same product more than once? | **No**, once per tenant per product (staff can still extend or grant manually) | Approved as recommended |
| K-03 | Default trial length for a new offer | **14 days** (same as `App:TrialDays`), adjustable per product 1–90 | Approved as recommended |
| K-04 | Does Transportation include Accounting? | **Yes**: TRN always includes ACC. ACC can be subscribed alone | Approved as recommended |
| K-05 | What happens when a product's trial or subscription ends? | **Read-only with all data kept** (view, print, export); deletion only through the existing tenant lifecycle | Approved as recommended |
| K-06 | Who may start a free trial on the tenant side? | **Owner only** (`core.tenant.manage`) | Approved as recommended |
| K-07 | Shared customers: `acc.ApArParties` as the shared register, `trn.TRN_Customers` as the linked transport profile (section b) | **Yes** | Approved as recommended |
| K-07b | Link every existing transport customer to a party now (one-time insert of missing parties), or lazily on next save/invoice? | **Lazily** (no data step); a one-time backfill is a small extra if wanted | **Changed: link every existing transport customer to a shared party now**, with a one-time, idempotent data step (inserts the missing parties and fills the empty `ApArPartyId`; running it again changes nothing). Done in **Stage 3**, when shared customers are built — not before |
| K-08 | Migrations in a separate `src/Hosts/DirectService.Database` project (an addition to the approved layout) or inside `DirectService.Web`? | **Separate project** (tests and tools need migrations without the web host) | Approved as recommended |
| K-09 | Allow module Infrastructure projects to reference the Platform anchor entities `Tenant`, `Company`, `User` for FK mapping only (exception E-1)? | **Yes** (the alternative is mapping by name everywhere, which is less safe) | Approved as recommended |
| K-10 | E-invoicing engine (JoFotara connector, settings, ICV counter, worker) in Accounting? | **Yes**; transport invoices plug in through `IEInvoiceDocumentSource` | Approved as recommended |
| K-11 | `core.MaintenanceLogs` owned by Accounting (table stays in `core`)? | **Yes** | Approved as recommended |
| K-12 | `trn.reports.view` becomes the cross-product "Reports" permission (code unchanged) so Accounting-only tenants can use the report builder and saved reports? | **Yes** | Approved as recommended |
| K-13 | Chart template: a general template for every new company, plus transport accounts and TRN mappings added when TRN is activated? (4103 / 4105 → Other income, 5206 Operating, new companies only — already decided) | **Yes** | Approved as recommended |
| K-14 | Sign-up: new tenants start the TRIAL plan with TRN + ACC (as today), or choose a product at sign-up? | **TRN + ACC as today** until a second product is on sale; then a product choice on the sign-up page | Approved as recommended |
| K-15 | Product names: "Direct Service Transportation" / «دايركت سيرفس للنقل», "Direct Service Accounting" / «دايركت سيرفس للمحاسبة»; platform "Direct Service" / «دايركت سيرفس» | Confirm the Arabic names | **Confirmed**: platform «دايركت سيرفس», «دايركت سيرفس للنقل», «دايركت سيرفس للمحاسبة» |
| K-16 | Manual payment methods | Cash, Bank transfer, CliQ, Cheque, Card (offline), Complimentary | Approved as recommended |
| K-17 | Does a manual activation or renewal issue the normal `SUB-` tax invoice (marked paid)? | **Yes** (one tax record, whatever the payment path) | Approved as recommended |
| K-18 | OpenAPI title, page titles and emails say "Direct Service" (platform) and the product name where a product is meant | **Yes** | Approved as recommended |
| K-19 | Plan limits of new products in `core.PlanLimits`; the four existing columns stay authoritative | **Yes** | Approved as recommended |
| K-20 | Lazy-loading module UI assemblies per product | **Not now** | Approved as recommended |
| K-21 | Development key ring after the host rename: copy `.local/keys` to the new host folder (old folder kept until you confirm), plus the start-up check (E-24) | **Yes** | Approved as recommended |
| K-22 | Database design revision 7 (section f.2: six new `core` tables, no rename, no move) — approve before Stage 5 | Approve at the start of Stage 5 | Approved as recommended (revision 7 is presented for approval at the start of Stage 5) |
| K-23 | Stage order and estimates (section j); start Stage 2 after this review | **As proposed** | **Approved**: stage order as proposed. Each stage is split into several sessions of 2–4 hours; each session ends with a local commit, all tests green and a short report. Each stage ends with a local tag `stageN-done-YYYY-MM-DD`, which the owner pushes |
