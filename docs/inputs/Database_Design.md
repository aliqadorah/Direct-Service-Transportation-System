# Direct Service Transportation — Database Design Reference

Master Database · SQL Server 2025 · Revision 4 (8 October 2026) · Status: agreed — source of truth for the EF Core model and the SQL creation script.

**66 tables** — 22 Transportation tables (prefix `TRN_`, schema `trn`) and 44 shared tables (schemas `core`, `acc`, `cfg`). **118 business foreign keys** plus the standard audit/tenant keys on every table.

## 1. How to read this document
- **Owner** — *Transportation* tables exist only for this system and must carry the `TRN_` prefix. *Shared* tables are used by every project in the Master Database and keep their names without a prefix.
- **Null** — `NOT NULL` = required; `NULL` = optional.
- **Default** — database default (`DEFAULT` constraint). Blank = no default; the application must supply the value.
- **Key** — `PK` primary key, `UQ` unique, `FK → table` foreign key.
- **Rules** — validations and business rules. `CHECK` = enforced by a database check constraint; everything else is enforced by the server-side application. IDs in brackets (BR-xx, FR-xx) refer to the PRD.
- Every table also has the **standard columns** in section 4 (marked *std* in the column tables).

## 2. All tables — ownership and prefix

| # | Schema | Table | Owner | Uses TRN_ prefix | Scope | Purpose |
|---|---|---|---|---|---|---|
| 1 | core | Plans | Shared | No | Platform-wide | Subscription plans the provider sells (price, billing cycle and usage limits). Managed from the platform console without code changes. |
| 2 | core | Tenants | Shared | No | Platform-wide | A subscribing organisation (the customer of the SaaS). Owns its companies, users and roles; all business data is isolated per tenant. |
| 3 | core | SubscriptionInvoices | Shared | No | Tenant-scoped | Tax invoices the provider issues to tenants for subscription charges. |
| 4 | core | Companies | Shared | No | Tenant-scoped | A legal company (legal entity) inside a tenant. Every business row in every module belongs to exactly one company. |
| 5 | core | Users | Shared | No | Tenant-scoped (TenantId NULL for provider staff) | People who sign in. Tenant users have TenantId; provider staff have TenantId NULL and IsPlatformStaff = 1. Built on ASP.NET Core Identity. |
| 6 | core | UserRecoveryCodes | Shared | No | Tenant-scoped (TenantId NULL for provider staff) | One-time 2FA recovery codes issued at 2FA set-up; stored only as hashes. |
| 7 | core | UserSessions | Shared | No | Tenant-scoped (TenantId NULL for provider staff) | Active sign-in sessions; used to show devices and for "sign out everywhere". |
| 8 | core | Permissions | Shared | No | Platform-wide | Reference list of permission keys for every module. Seeded by each module; not editable by tenants. |
| 9 | core | Roles | Shared | No | Tenant-scoped | Named sets of permissions per tenant: presets (Owner, Administrator, Accountant, Dispatcher, Viewer) and custom roles. |
| 10 | core | RolePermissions | Shared | No | Tenant-scoped | Permissions granted by each role (many-to-many Roles ↔ Permissions). |
| 11 | core | UserCompanyRoles | Shared | No | Tenant-scoped | The single role a user holds in each company they can access. |
| 12 | core | Invitations | Shared | No | Tenant-scoped | Email invitations to join a company with a role. The link expires after 7 days. |
| 13 | core | SupportAccessGrants | Shared | No | Tenant-scoped | Time-limited access for provider support staff, approved by the tenant Owner; every action during it is audited. |
| 14 | core | TermsAcceptances | Shared | No | Tenant-scoped | Record of each user accepting a version of the Terms of Service or Privacy Policy. |
| 15 | core | AuditLogs | Shared | No | Append-only log | Append-only trail of every create, update, void, delete, restore, sign-in and setting change in every module. Also the source of the record-history screen. |
| 16 | core | Notifications | Shared | No | Tenant-scoped | In-app notifications for a user (e.g. document expiry alerts, failed postings, e-invoice rejections). |
| 17 | core | Announcements | Shared | No | Platform-wide | Planned-maintenance and service notices published by the provider to all tenants. |
| 18 | core | MaintenanceLogs | Shared | No | Append-only log | Append-only log of maintenance-zone actions (bulk soft deletes and restores) performed by administrators in any module. |
| 19 | trn | TRN_OwnershipTypes | Transportation | Yes | Company-scoped | Truck ownership types (e.g. Company, Rented, Owner-driver), each classified as own fleet or external fleet. The class decides which monthly driver rate applies. |
| 20 | trn | TRN_Partners | Transportation | Yes | Company-scoped | Partner (sub-contracted) haulage companies whose trucks carry loads for the company; the company pays them per ton. |
| 21 | trn | TRN_Drivers | Transportation | Yes | Company-scoped | Drivers and the single truck registered with each driver (one truck per driver; truck details are edited from the driver profile). |
| 22 | trn | TRN_Customers | Transportation | Yes | Company-scoped | Transport customers who are invoiced for trips. |
| 23 | trn | TRN_Materials | Transportation | Yes | Company-scoped | Materials carried (e.g. phosphate, potash, sand). |
| 24 | trn | TRN_LoadingSites | Transportation | Yes | Company-scoped | Places where loads are picked up (mines, quarries). Monthly driver rates are set per loading site. |
| 25 | trn | TRN_UnloadingSites | Transportation | Yes | Company-scoped | Places where loads are delivered (ports, plants, warehouses). |
| 26 | trn | TRN_Tariffs | Transportation | Yes | Company-scoped | Price rules per ton: the customer price (PartyType Customer) or the partner rate (PartyType Partner). A NULL dimension means "All". The most specific matching line wins. |
| 27 | trn | TRN_MonthlySiteRates | Transportation | Yes | Company-scoped | Driver rate per ton for each loading site and month, with separate own-fleet and external-fleet rates. |
| 28 | trn | TRN_RateHistory | Transportation | Yes | Company-scoped | Change history of tariffs and monthly site rates (who changed what, old and new values). |
| 29 | trn | TRN_Trips | Transportation | Yes | Company-scoped | A shipment: one truck load from a loading site to an unloading site for a customer. Prices, the driver rate and all amounts are calculated and frozen at save. |
| 30 | trn | TRN_Invoices | Transportation | Yes | Company-scoped | Customer invoices: trip-based (one line per trip) or manual (free lines). Drafts have no number; approval assigns the number, posts the entry and queues e-invoicing. |
| 31 | trn | TRN_InvoiceLines | Transportation | Yes | Company-scoped | Invoice lines: one line per trip on trip invoices, or free description/quantity/price lines on manual invoices. |
| 32 | trn | TRN_CreditNotes | Transportation | Yes | Company-scoped | Full or partial returns of an approved invoice. Also the only way to cancel an e-invoiced invoice. |
| 33 | trn | TRN_CreditNoteLines | Transportation | Yes | Company-scoped | The trips or quantities returned by a credit note. |
| 34 | trn | TRN_EInvoiceSubmissions | Transportation | Yes | Company-scoped | Submissions of transport invoices and credit notes to the national e-invoicing system (JoFotara, UBL 2.1) and the responses. |
| 35 | trn | TRN_Receipts | Transportation | Yes | Company-scoped | Money received from customers — against an invoice or on account. |
| 36 | trn | TRN_Advances | Transportation | Yes | Company-scoped | Cash advances paid to drivers, deducted at settlement. |
| 37 | trn | TRN_Settlements | Transportation | Yes | Company-scoped | Driver settlements: gross wages of open trips − open advances − deductions = net paid. |
| 38 | trn | TRN_SettlementDeductions | Transportation | Yes | Company-scoped | Deduction lines on a settlement (shortage, fines, other). |
| 39 | trn | TRN_PartnerPayments | Transportation | Yes | Company-scoped | Payments to partner hauliers against their balance due. |
| 40 | trn | TRN_InvoiceLayouts | Transportation | Yes | Company-scoped | Print layouts per company for the claim statement and the tax invoice. |
| 41 | acc | Accounts | Shared | No | Company-scoped | Chart of accounts per company, as a tree. Only leaf (postable) accounts receive entry lines. |
| 42 | acc | PostingKeyMappings | Shared | No | Company-scoped | Maps each posting key used by automatic entries (e.g. TRN.DriverPayable) to an account. |
| 43 | acc | CostCentres | Shared | No | Company-scoped | Cost centres used to analyse entry lines. |
| 44 | acc | CashBoxes | Shared | No | Company-scoped | Cash boxes; each is linked to its ledger account. |
| 45 | acc | BankAccounts | Shared | No | Company-scoped | Company bank accounts; each is linked to its ledger account. |
| 46 | acc | Periods | Shared | No | Company-scoped | Monthly accounting periods per company, open or closed. |
| 47 | acc | JournalEntries | Shared | No | Company-scoped | Journal entries — automatic (posted by documents in any module) and manual. Corrections are reversal entries; posted entries are never edited. |
| 48 | acc | EntryLines | Shared | No | Company-scoped | Debit and credit lines of a journal entry. |
| 49 | acc | PendingPostings | Shared | No | Company-scoped | Postings that failed for a technical reason, waiting for automatic or manual retry. |
| 50 | acc | JournalExportBatches | Shared | No | Company-scoped | Logged exports of journal entries (JSON or CSV) for external accounting tools. |
| 51 | acc | ApArParties | Shared | No | Company-scoped | Suppliers and customers kept by Accounting itself (non-transport payables and receivables). |
| 52 | acc | ApArDocuments | Shared | No | Company-scoped | Supplier bills (payables) and receivable documents for non-transport income. |
| 53 | acc | ApArDocumentLines | Shared | No | Company-scoped | Lines of AP/AR documents. |
| 54 | acc | ApArPayments | Shared | No | Company-scoped | Payments to suppliers and collections from customers against AP/AR documents. |
| 55 | acc | ApArReturns | Shared | No | Company-scoped | Returns against AP/AR documents (payable returns and receivable returns). |
| 56 | acc | ApArEInvoiceSubmissions | Shared | No | Company-scoped | Submissions of Accounting receivable documents and their returns to the national e-invoicing system. |
| 57 | cfg | ImportBatches | Shared | No | Company-scoped | Log of every import (trips, master data, chart of accounts, entries) with row counts. |
| 58 | cfg | CompanySettings | Shared | No | Company-scoped | Key/value settings per company for every module; keys carry the module prefix. |
| 59 | cfg | NumberSequences | Shared | No | Company-scoped | Gap-free number sequences per document type and company, incremented only by the server. |
| 60 | cfg | EInvoiceSettings | Shared | No | Company-scoped | National e-invoicing connection for each company, shared by every module that issues tax invoices. |
| 61 | cfg | CustomFieldDefinitions | Shared | No | Company-scoped | Admin-defined extra fields for module entities (launch: trips, drivers, customers). Values live in each record's CustomValues JSON. |
| 62 | cfg | StandardFieldConfigs | Shared | No | Company-scoped | Per-company overrides of built-in fields: rename, hide, require, reorder. |
| 63 | cfg | TableColumnViews | Shared | No | Company-scoped | Saved column layouts for list screens: company default (UserId NULL) or per user. |
| 64 | cfg | SavedReports | Shared | No | Company-scoped | Report-builder definitions, private or shared within the company. |
| 65 | cfg | DashboardPins | Shared | No | Company-scoped | Saved reports pinned to a user's dashboard, with warning and critical thresholds. |
| 66 | cfg | RecordLocks | Shared | No | Company-scoped | Edit locks: opening a record for editing locks it for other users. |

## 3. Naming, ownership and global conventions

### 3.1 Naming
| Object | Convention | Example |
|---|---|---|
| Transportation table | `TRN_` + plural PascalCase, schema `trn` | `trn.TRN_Trips` |
| Shared table | No prefix; schema `core`, `acc` or `cfg` | `core.Users`, `acc.JournalEntries` |
| Future project table | Own 3-letter prefix and schema; never `TRN_` | `xyz.XYZ_Orders` |
| Primary key | `PK_<Table>` | `PK_TRN_Trips` |
| Foreign key | `FK_<Table>_<RefTable>_<Column>` | `FK_TRN_Trips_TRN_Drivers_DriverId` |
| Unique index | `UX_<Table>_<Columns>` (filtered) | `UX_TRN_Invoices_CompanyId_Number` |
| Index / check / default | `IX_` / `CK_` / `DF_` + table + column(s) | `CK_TRN_Settlements_NetPayable` |
| EF Core entity | No prefix in C#; mapping sets the table | `Trip` → `ToTable("TRN_Trips", "trn")` |

### 3.2 Ownership rules
- Transportation tables may reference shared tables. **A shared table never has a foreign key to a `TRN_` table.** Shared Accounting points back to transport documents with `SourceModule` + `SourceType` + `SourceId`, and to parties with `PartyModule` + `PartyType` + `PartyId`; the application validates these.
- Values in shared tables that belong to a module carry its prefix: setting keys `TRN.WeightBasis`, document types `TRN.Invoice`, posting keys `TRN.DriverPayable`, permission keys `trn.trips.view`, entity types `TRN.Trip`, view keys `TRN.TripsList`.
- One e-invoice counter (ICV) per company in shared `cfg.EInvoiceSettings`; submission logs belong to the module owning the document (`trn.TRN_EInvoiceSubmissions`, `acc.ApArEInvoiceSubmissions`).
- Classification reflects what is known today (no other project defined yet). A `TRN_` table is promoted to shared only by a planned migration when a second project needs it.

### 3.3 Global conventions
- Keys: internal `BIGINT IDENTITY(1,1)`; public `UNIQUEIDENTIFIER` (`NEWID()`), hashed into URLs. Internal keys never leave the server.
- Soft delete only (BR-041): no `DELETE` statements against business tables. All foreign keys are `ON DELETE NO ACTION` / `ON UPDATE NO ACTION`; a referenced row cannot be deleted (BR-040) — it is archived or voided instead.
- Unique indexes are **filtered** to active rows (`WHERE IsDeleted = 0`) so a soft-deleted row never blocks a new one. In SQL Server a unique index treats NULLs as equal, which is what the "All" (NULL) dimensions of tariffs need.
- Tenant and company isolation: every query is filtered by `TenantId` and `CompanyId` (EF Core global filters + server checks).
- Types: money `DECIMAL(18,3)` (fils), prices/rates `DECIMAL(18,4)`, weights `DECIMAL(18,3)`, percentages `DECIMAL(5,2)`/`DECIMAL(9,4)`, dates `DATE`, timestamps `DATETIME2` in UTC, months `CHAR(7)` as `YYYY-MM`.
- Text: `NVARCHAR` for anything a person types (Arabic/English); `VARCHAR` for codes, numbers and enumerations. Enumerations are stored as English codes and enforced with `CHECK IN (...)`; the interface translates them.
- JSON columns are `NVARCHAR(MAX)` with `CHECK (ISJSON(col) = 1)`.
- Rounding half-up to the currency decimals at every calculated amount (BR-021). Calculated amounts are computed by the server and stored (frozen) at save.
- Record history comes from append-only `core.AuditLogs`; custom-field values are stored in a `CustomValues` JSON column.

## 4. Standard columns

Added automatically according to the table scope, and listed again in each table below (marked *std*).

| Column | Type | Null | Default | Key | Description | Company | Tenant | Platform | Append-only |
|---|---|---|---|---|---|---|---|---|---|
| Id | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. | ✓ | ✓ | ✓ | own Id only |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). Unique; never changes. | ✓ | ✓ | ✓ | — |
| TenantId | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. Set by the server from the signed-in context; never from user input. | ✓ | ✓ | — | — |
| CompanyId | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. Must belong to TenantId; set by the server. | ✓ | — | — | — |
| CreatedAt | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. | ✓ | ✓ | ✓ | — |
| CreatedBy | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. | ✓ | ✓ | ✓ | — |
| UpdatedAt | DATETIME2(3) | NULL |  |  | UTC time of the last update. | ✓ | ✓ | ✓ | — |
| UpdatedBy | BIGINT | NULL |  | FK → core.Users | User who last updated the row. | ✓ | ✓ | ✓ | — |
| IsDeleted | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. No hard deletes (BR-041). | ✓ | ✓ | ✓ | — |
| DeletedAt | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. Required when IsDeleted = 1 (CHECK). | ✓ | ✓ | ✓ | — |
| DeletedBy | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. Required when IsDeleted = 1 (CHECK). | ✓ | ✓ | ✓ | — |
| DeleteReason | NVARCHAR(500) | NULL |  |  | Why the row was deleted. Required when IsDeleted = 1 (CHECK). | ✓ | ✓ | ✓ | — |
| RowVersion | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. | ✓ | ✓ | ✓ | — |

Scopes: **Company-scoped** rows belong to one company; **Tenant-scoped** to a tenant; **Platform-wide** rows are reference data for all tenants; **Append-only** logs list their own columns and are never updated or deleted.

## 5. Table reference

### Schema `core` — Shared — platform, tenants, users, roles, audit

#### core.Plans

**Owner:** Shared across all projects — no prefix · **Scope:** Platform-wide

**Purpose:** Subscription plans the provider sells (price, billing cycle and usage limits). Managed from the platform console without code changes.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| Code | VARCHAR(30) | NOT NULL |  | UQ | Short unique plan code, e.g. BASIC, PRO. | Unique among active plans. |
| NameAr | NVARCHAR(100) | NOT NULL |  |  | Plan name in Arabic. |  |
| NameEn | NVARCHAR(100) | NOT NULL |  |  | Plan name in English. |  |
| Price | DECIMAL(18,3) | NOT NULL |  |  | Price per billing cycle, excluding tax. | CHECK ≥ 0 |
| BillingCycle | VARCHAR(10) | NOT NULL | 'Monthly' |  | Billing cycle of the price. | CHECK IN ('Monthly','Annual'). |
| MaxCompanies | INT | NOT NULL |  |  | Maximum companies a tenant may create. | CHECK > 0. |
| MaxActiveUsers | INT | NOT NULL |  |  | Maximum active (non-archived) users. | CHECK > 0. |
| MaxTripsPerMonth | INT | NOT NULL |  |  | Maximum trips saved per calendar month. | CHECK > 0. |
| MaxStorageMb | INT | NOT NULL |  |  | Storage allowance for attachments and exports, in MB. | CHECK > 0. |
| IsActive | BIT | NOT NULL | 1 |  | Whether the plan can be chosen by new or upgrading tenants. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Plans (Id)`

Standard foreign keys: CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Plans_PublicId` (PublicId)
- `UX_Plans_Code` (Code) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One core.Plans → many core.Tenants via PlanId

**Business rules:**
- Changing a plan never changes invoices already issued.
- A plan used by any tenant cannot be deleted (BR-040); set IsActive = 0 instead.


#### core.Tenants

**Owner:** Shared across all projects — no prefix · **Scope:** Platform-wide

**Purpose:** A subscribing organisation (the customer of the SaaS). Owns its companies, users and roles; all business data is isolated per tenant.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| Name | NVARCHAR(200) | NOT NULL |  |  | Organisation name entered at sign-up. | Required. |
| Status | VARCHAR(15) | NOT NULL | 'Trial' |  | Subscription lifecycle state. | CHECK IN ('Trial','Active','PastDue','Suspended','Cancelled','Deleted'). |
| PlanId | BIGINT | NOT NULL |  | FK → core.Plans | Current subscription plan. |  |
| BillingCycle | VARCHAR(10) | NOT NULL | 'Monthly' |  | Billing cycle chosen by the tenant. | CHECK IN ('Monthly','Annual'). |
| TrialEndsAt | DATETIME2 | NULL |  |  | End of the free trial (UTC). | Set at sign-up. |
| CurrentPeriodEnd | DATETIME2 | NULL |  |  | End of the paid period (renewal date). |  |
| GraceEndsAt | DATETIME2 | NULL |  |  | End of the grace period after a failed payment; after it the tenant is suspended. |  |
| CancelledAt | DATETIME2 | NULL |  |  | When the subscription was cancelled. |  |
| RetentionEndsAt | DATETIME2 | NULL |  |  | Date data is kept until after cancellation, before the tenant is marked Deleted (soft). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Tenants (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Tenants_Plans_PlanId`: PlanId → core.Plans(Id) — required

Standard foreign keys: CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Tenants_PublicId` (PublicId)

**Indexes:**
- `IX_Tenants_PlanId` (PlanId) — supports the foreign key

**Relationships:**
- Many core.Tenants → one core.Plans via PlanId (required)
- One core.Tenants → many core.AuditLogs via TenantId
- One core.Tenants → many core.MaintenanceLogs via TenantId

**Business rules:**
- Suspended tenants can sign in read-only (export only); no posting.
- Tenant deletion is a soft delete (BR-041).


#### core.SubscriptionInvoices

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Tax invoices the provider issues to tenants for subscription charges.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| Number | VARCHAR(30) | NOT NULL |  | UQ | Provider invoice number. | Unique platform-wide. |
| PeriodStart | DATE | NOT NULL |  |  | First day billed. |  |
| PeriodEnd | DATE | NOT NULL |  |  | Last day billed. | PeriodEnd ≥ PeriodStart (CHECK). |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Charge before tax. | CHECK ≥ 0 |
| Tax | DECIMAL(18,3) | NOT NULL | 0 |  | Sales tax amount. | CHECK ≥ 0; rounded half-up to the currency decimals (BR-021). |
| Total | DECIMAL(18,3) | NOT NULL |  |  | Amount + Tax. | Total = Amount + Tax (CHECK). |
| Status | VARCHAR(15) | NOT NULL | 'Due' |  | Payment state. | CHECK IN ('Due','Paid','Failed','Credited'). |
| PaidAt | DATETIME2 | NULL |  |  | When payment succeeded. | Required when Status = Paid. |
| GatewayReference | NVARCHAR(100) | NULL |  |  | Payment-gateway transaction reference (provider deferred; stored as text). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_SubscriptionInvoices (Id)`

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_SubscriptionInvoices_PublicId` (PublicId)
- `UX_SubscriptionInvoices_Number` (Number) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### core.Companies

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** A legal company (legal entity) inside a tenant. Every business row in every module belongs to exactly one company.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| Name | NVARCHAR(200) | NOT NULL |  |  | Legal name (Arabic or main language). | Unique within the tenant. |
| NameEn | NVARCHAR(200) | NULL |  |  | Legal name in English. |  |
| TaxNumber | VARCHAR(30) | NULL |  |  | Tax registration number. | Required on parties receiving a tax invoice when e-invoicing is enabled (application rule). |
| CommercialRegNo | VARCHAR(30) | NULL |  |  | Commercial registration number. |  |
| Address | NVARCHAR(300) | NULL |  |  | Postal / street address. |  |
| Phone | VARCHAR(20) | NULL |  |  | Phone number. | Market mobile format; launch market 07[789]XXXXXXX or 06XXXXXXX (configurable per market). |
| Email | NVARCHAR(150) | NULL |  |  | Email address. | Valid email format. |
| LogoFile | VARBINARY(MAX) | NULL |  |  | Company logo image shown on screens and printouts. | PNG/JPG, size limited by the application. |
| CountryCode | CHAR(2) | NOT NULL | 'JO' |  | ISO 3166 country of the company; drives market formats. | 2 uppercase letters. |
| CurrencyCode | CHAR(3) | NOT NULL | 'JOD' |  | ISO 4217 currency. | 3 uppercase letters. |
| CurrencyDecimals | TINYINT | NOT NULL | 3 |  | Decimals shown and rounded to. | CHECK BETWEEN 0 AND 3. |
| TaxRate | DECIMAL(5,2) | NOT NULL | 16.00 |  | Sales tax rate %. | CHECK BETWEEN 0 AND 100. |
| TaxInclusive | BIT | NOT NULL | 0 |  | 1 = prices include tax; 0 = tax added on top (default, BR-022). |  |
| TimeZone | VARCHAR(50) | NOT NULL | 'Asia/Amman' |  | IANA time zone for dates and period cut-off. |  |
| MigrationCompletedAt | DATETIME2 | NULL |  |  | When initial setup/data migration was marked complete. Until then an opening-balance difference is allowed with a warning (FR-110). | Once set, opening balances must balance. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Companies (Id)`

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Companies_PublicId` (PublicId)
- `UX_Companies_TenantId_Name` (TenantId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One core.Companies → many core.Users via DefaultCompanyId
- One core.Companies → many core.UserCompanyRoles via CompanyId
- One core.Companies → many core.Invitations via CompanyId
- One core.Companies → many core.AuditLogs via CompanyId
- One core.Companies → many core.Notifications via CompanyId
- One core.Companies → many core.MaintenanceLogs via CompanyId

**Business rules:**
- Company currency, decimals and tax settings are copied onto documents at issue time so later changes never alter issued documents.


#### core.Users

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped (TenantId NULL for provider staff)

**Purpose:** People who sign in. Tenant users have TenantId; provider staff have TenantId NULL and IsPlatformStaff = 1. Built on ASP.NET Core Identity.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NULL |  | FK → core.Tenants | Owning tenant; NULL for provider staff. | Set by the server from the signed-in context; never from user input. |
| FullName | NVARCHAR(150) | NOT NULL |  |  | User's full name. | Required. |
| Email | NVARCHAR(150) | NOT NULL |  | UQ | Sign-in email. | Unique among active users platform-wide: one email = one account in one tenant (confirmed decision). |
| NormalizedEmail | NVARCHAR(150) | NOT NULL |  | UQ | Upper-cased email used for look-ups (Identity). | Unique among active users. |
| EmailConfirmedAt | DATETIME2 | NULL |  |  | When the user verified their email address (sign-up link or accepted invitation). | Sign-in is refused until set (PRD 9.1); Identity's EmailConfirmed = EmailConfirmedAt IS NOT NULL. |
| PasswordHash | NVARCHAR(500) | NOT NULL |  |  | Salted password hash (Identity). Never shown or emailed. | Password policy: ≥ 8 chars, letters and digits; configurable per tenant (FR-175). |
| SecurityStamp | NVARCHAR(100) | NOT NULL |  |  | Identity security stamp; changed on password/2FA change to invalidate sessions. |  |
| IsPlatformStaff | BIT | NOT NULL | 0 |  | 1 for provider staff (no tenant). | IsPlatformStaff = 1 ⇔ TenantId IS NULL (CHECK). |
| TwoFactorSecret | VARBINARY(256) | NULL |  |  | Encrypted authenticator-app key. | Required once 2FA is set up. |
| TwoFactorEnabledAt | DATETIME2 | NULL |  |  | When mandatory 2FA was set up (first sign-in). | 2FA cannot be disabled; reset requires users.manage and is audited (FR-177). |
| LanguageCode | CHAR(2) | NOT NULL | 'ar' |  | Interface language. | CHECK IN ('ar','en'). |
| DefaultCompanyId | BIGINT | NULL |  | FK → core.Companies | Company opened after sign-in. | Must be a company the user has a role in. |
| DateFormat | VARCHAR(20) | NOT NULL | 'dd/MM/yyyy' |  | Preferred date format. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Account state. | CHECK IN ('Active','Disabled','Archived'). |
| FailedSignInCount | INT | NOT NULL | 0 |  | Consecutive failed sign-ins. | 5 failures → 15-minute lock (FR-175). |
| LockedUntil | DATETIME2 | NULL |  |  | Sign-in blocked until this UTC time. |  |
| LastSignInAt | DATETIME2 | NULL |  |  | Last successful sign-in. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Users (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Users_Companies_DefaultCompanyId`: DefaultCompanyId → core.Companies(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Users_PublicId` (PublicId)
- `UX_Users_Email` (Email) WHERE IsDeleted = 0
- `UX_Users_NormalizedEmail` (NormalizedEmail) WHERE IsDeleted = 0

**Indexes:**
- `IX_Users_DefaultCompanyId` (DefaultCompanyId) — supports the foreign key

**Relationships:**
- Many core.Users → one core.Companies via DefaultCompanyId (optional)
- One core.Users → many core.UserRecoveryCodes via UserId
- One core.Users → many core.UserSessions via UserId
- One core.Users → many core.UserCompanyRoles via UserId
- One core.Users → many core.Invitations via InvitedBy
- One core.Users → many core.SupportAccessGrants via RequestedBy
- One core.Users → many core.SupportAccessGrants via ApprovedBy
- One core.Users → many core.TermsAcceptances via UserId
- One core.Users → many core.AuditLogs via UserId
- One core.Users → many core.Notifications via UserId
- One core.Users → many trn.TRN_RateHistory via ChangedBy
- One core.Users → many trn.TRN_Trips via VoidedBy
- One core.Users → many trn.TRN_Invoices via ApprovedBy
- One core.Users → many trn.TRN_Invoices via VoidedBy
- One core.Users → many trn.TRN_CreditNotes via VoidedBy
- One core.Users → many cfg.ImportBatches via ImportedBy
- One core.Users → many trn.TRN_Receipts via VoidedBy
- One core.Users → many trn.TRN_Advances via VoidedBy
- One core.Users → many trn.TRN_Settlements via VoidedBy
- One core.Users → many trn.TRN_PartnerPayments via VoidedBy
- One core.Users → many acc.Periods via ClosedBy
- One core.Users → many acc.JournalEntries via VoidedBy
- One core.Users → many acc.JournalExportBatches via ExportedBy
- One core.Users → many acc.ApArDocuments via VoidedBy
- One core.Users → many acc.ApArPayments via VoidedBy
- One core.Users → many acc.ApArReturns via VoidedBy
- One core.Users → many core.MaintenanceLogs via UserId
- One core.Users → many cfg.TableColumnViews via UserId
- One core.Users → many cfg.SavedReports via OwnerUserId
- One core.Users → many cfg.DashboardPins via UserId
- One core.Users → many cfg.RecordLocks via UserId


#### core.UserRecoveryCodes

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped (TenantId NULL for provider staff)

**Purpose:** One-time 2FA recovery codes issued at 2FA set-up; stored only as hashes.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NULL |  | FK → core.Tenants | Owning tenant; NULL for provider staff. | Set by the server from the signed-in context; never from user input. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Owner of the code. |  |
| CodeHash | NVARCHAR(200) | NOT NULL |  |  | Hash of the recovery code. |  |
| UsedAt | DATETIME2 | NULL |  |  | When the code was used; a used code can never be used again. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_UserRecoveryCodes (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_UserRecoveryCodes_Users_UserId`: UserId → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_UserRecoveryCodes_PublicId` (PublicId)

**Indexes:**
- `IX_UserRecoveryCodes_UserId` (UserId) — supports the foreign key

**Relationships:**
- Many core.UserRecoveryCodes → one core.Users via UserId (required)
- Not referenced by other tables

**Business rules:**
- Regenerating codes soft-deletes all previous codes of the user.


#### core.UserSessions

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped (TenantId NULL for provider staff)

**Purpose:** Active sign-in sessions; used to show devices and for "sign out everywhere".

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NULL |  | FK → core.Tenants | Owning tenant; NULL for provider staff. | Set by the server from the signed-in context; never from user input. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Signed-in user. |  |
| TokenHash | NVARCHAR(200) | NOT NULL |  | UQ | Hash of the session/refresh token. | Unique. |
| LastSeenAt | DATETIME2 | NOT NULL |  |  | Last request time. |  |
| ExpiresAt | DATETIME2 | NOT NULL |  |  | Absolute expiry. |  |
| IpAddress | VARCHAR(45) | NULL |  |  | Client IP address (IPv4 or IPv6). |  |
| UserAgent | NVARCHAR(300) | NULL |  |  | Browser user-agent string. |  |
| RevokedAt | DATETIME2 | NULL |  |  | When revoked (sign-out, password or 2FA reset). | A revoked or expired session is rejected. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_UserSessions (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_UserSessions_Users_UserId`: UserId → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_UserSessions_PublicId` (PublicId)
- `UX_UserSessions_TokenHash` (TokenHash) WHERE IsDeleted = 0

**Indexes:**
- `IX_UserSessions_UserId_ExpiresAt` (UserId, ExpiresAt) WHERE RevokedAt IS NULL

**Relationships:**
- Many core.UserSessions → one core.Users via UserId (required)
- Not referenced by other tables


#### core.Permissions

**Owner:** Shared across all projects — no prefix · **Scope:** Platform-wide

**Purpose:** Reference list of permission keys for every module. Seeded by each module; not editable by tenants.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| PermissionKey | VARCHAR(80) | NOT NULL |  | UQ | Namespaced key checked on the server, e.g. trn.trips.view. | Unique; lower-case, dot-separated, starts with the module prefix. |
| GroupName | VARCHAR(50) | NOT NULL |  |  | Group shown in the role editor (e.g. Trips, Invoices). |  |
| DescriptionAr | NVARCHAR(200) | NOT NULL |  |  | What the permission allows (Arabic). |  |
| DescriptionEn | NVARCHAR(200) | NOT NULL |  |  | What the permission allows (English). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Permissions (Id)`

Standard foreign keys: CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Permissions_PublicId` (PublicId)
- `UX_Permissions_PermissionKey` (PermissionKey) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One core.Permissions → many core.RolePermissions via PermissionId


#### core.Roles

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Named sets of permissions per tenant: presets (Owner, Administrator, Accountant, Dispatcher, Viewer) and custom roles.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Role name. | Unique within the tenant. |
| IsPreset | BIT | NOT NULL | 0 |  | 1 = built-in preset role (name and permissions fixed; cannot be deleted). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Roles (Id)`

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Roles_PublicId` (PublicId)
- `UX_Roles_TenantId_Name` (TenantId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One core.Roles → many core.RolePermissions via RoleId
- One core.Roles → many core.UserCompanyRoles via RoleId
- One core.Roles → many core.Invitations via RoleId

**Business rules:**
- At least one active user must hold the Owner role in each tenant.


#### core.RolePermissions

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Permissions granted by each role (many-to-many Roles ↔ Permissions).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| RoleId | BIGINT | NOT NULL |  | FK → core.Roles | Role. |  |
| PermissionId | BIGINT | NOT NULL |  | FK → core.Permissions | Permission granted. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_RolePermissions (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_RolePermissions_Roles_RoleId`: RoleId → core.Roles(Id) — required
- `FK_RolePermissions_Permissions_PermissionId`: PermissionId → core.Permissions(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_RolePermissions_PublicId` (PublicId)
- `UX_RolePermissions_RoleId_PermissionId` (RoleId, PermissionId) WHERE IsDeleted = 0

**Indexes:**
- `IX_RolePermissions_PermissionId` (PermissionId) — supports the foreign key

**Relationships:**
- Many core.RolePermissions → one core.Roles via RoleId (required)
- Many core.RolePermissions → one core.Permissions via PermissionId (required)
- Not referenced by other tables


#### core.UserCompanyRoles

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** The single role a user holds in each company they can access.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | User. |  |
| CompanyId | BIGINT | NOT NULL |  | FK → core.Companies | Company the role applies to. | Must belong to the same tenant as the user. |
| RoleId | BIGINT | NOT NULL |  | FK → core.Roles | Role held. | Role must belong to the same tenant. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_UserCompanyRoles (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_UserCompanyRoles_Users_UserId`: UserId → core.Users(Id) — required
- `FK_UserCompanyRoles_Companies_CompanyId`: CompanyId → core.Companies(Id) — required
- `FK_UserCompanyRoles_Roles_RoleId`: RoleId → core.Roles(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_UserCompanyRoles_PublicId` (PublicId)
- `UX_UserCompanyRoles_UserId_CompanyId` (UserId, CompanyId) WHERE IsDeleted = 0

**Indexes:**
- `IX_UserCompanyRoles_CompanyId` (CompanyId) — supports the foreign key
- `IX_UserCompanyRoles_RoleId` (RoleId) — supports the foreign key

**Relationships:**
- Many core.UserCompanyRoles → one core.Users via UserId (required)
- Many core.UserCompanyRoles → one core.Companies via CompanyId (required)
- Many core.UserCompanyRoles → one core.Roles via RoleId (required)
- Not referenced by other tables


#### core.Invitations

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Email invitations to join a company with a role. The link expires after 7 days.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId | BIGINT | NOT NULL |  | FK → core.Companies | Company the invitee will join. |  |
| Email | NVARCHAR(150) | NOT NULL |  |  | Invitee email. | Valid email. |
| RoleId | BIGINT | NOT NULL |  | FK → core.Roles | Role granted on acceptance. |  |
| TokenHash | NVARCHAR(200) | NOT NULL |  | UQ | Hash of the invitation token in the link. | Unique. |
| ExpiresAt | DATETIME2 | NOT NULL |  |  | Expiry = sent time + 7 days. |  |
| AcceptedAt | DATETIME2 | NULL |  |  | When accepted. | An accepted or expired invitation cannot be used again. |
| InvitedBy | BIGINT | NOT NULL |  | FK → core.Users | User who sent the invitation. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Invitations (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Invitations_Companies_CompanyId`: CompanyId → core.Companies(Id) — required
- `FK_Invitations_Roles_RoleId`: RoleId → core.Roles(Id) — required
- `FK_Invitations_Users_InvitedBy`: InvitedBy → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Invitations_PublicId` (PublicId)
- `UX_Invitations_TokenHash` (TokenHash) WHERE IsDeleted = 0

**Indexes:**
- `IX_Invitations_CompanyId` (CompanyId) — supports the foreign key
- `IX_Invitations_RoleId` (RoleId) — supports the foreign key
- `IX_Invitations_InvitedBy` (InvitedBy) — supports the foreign key

**Relationships:**
- Many core.Invitations → one core.Companies via CompanyId (required)
- Many core.Invitations → one core.Roles via RoleId (required)
- Many core.Invitations → one core.Users via InvitedBy (required)
- Not referenced by other tables


#### core.SupportAccessGrants

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Time-limited access for provider support staff, approved by the tenant Owner; every action during it is audited.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| RequestedBy | BIGINT | NOT NULL |  | FK → core.Users | Provider staff user requesting access. | Must be platform staff. |
| ApprovedBy | BIGINT | NULL |  | FK → core.Users | Tenant Owner who approved or rejected. | Required when Status is Approved or Rejected. |
| Reason | NVARCHAR(500) | NOT NULL |  |  | Why access is needed. | Required. |
| StartsAt | DATETIME2 | NULL |  |  | Access window start. |  |
| EndsAt | DATETIME2 | NULL |  |  | Access window end. | EndsAt > StartsAt (CHECK). |
| Status | VARCHAR(10) | NOT NULL | 'Requested' |  | Request state. | CHECK IN ('Requested','Approved','Rejected','Expired'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_SupportAccessGrants (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_SupportAccessGrants_Users_RequestedBy`: RequestedBy → core.Users(Id) — required
- `FK_SupportAccessGrants_Users_ApprovedBy`: ApprovedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_SupportAccessGrants_PublicId` (PublicId)

**Indexes:**
- `IX_SupportAccessGrants_RequestedBy` (RequestedBy) — supports the foreign key
- `IX_SupportAccessGrants_ApprovedBy` (ApprovedBy) — supports the foreign key

**Relationships:**
- Many core.SupportAccessGrants → one core.Users via RequestedBy (required)
- Many core.SupportAccessGrants → one core.Users via ApprovedBy (optional)
- Not referenced by other tables


#### core.TermsAcceptances

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** Record of each user accepting a version of the Terms of Service or Privacy Policy.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | User who accepted. |  |
| DocumentType | VARCHAR(20) | NOT NULL |  |  | Document accepted. | CHECK IN ('Terms','Privacy'). |
| DocumentVersion | VARCHAR(20) | NOT NULL |  |  | Version accepted. |  |
| AcceptedAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | When accepted. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TermsAcceptances (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TermsAcceptances_Users_UserId`: UserId → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TermsAcceptances_PublicId` (PublicId)

**Indexes:**
- `IX_TermsAcceptances_UserId` (UserId) — supports the foreign key

**Relationships:**
- Many core.TermsAcceptances → one core.Users via UserId (required)
- Not referenced by other tables


#### core.AuditLogs

**Owner:** Shared across all projects — no prefix · **Scope:** Append-only log

**Purpose:** Append-only trail of every create, update, void, delete, restore, sign-in and setting change in every module. Also the source of the record-history screen.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal key. | BIGINT IDENTITY. |
| TenantId | BIGINT | NULL |  | FK → core.Tenants | Tenant (NULL for platform-level actions). |  |
| CompanyId | BIGINT | NULL |  | FK → core.Companies | Company (NULL for tenant/platform-level actions). |  |
| UserId | BIGINT | NULL |  | FK → core.Users | Acting user (NULL for system jobs). |  |
| OccurredAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | UTC time of the action. |  |
| Action | VARCHAR(50) | NOT NULL |  |  | Action performed. | e.g. Create, Update, Void, Delete, Restore, SignIn, SignInFailed, SettingChange, SupportAccess. |
| EntityType | VARCHAR(60) | NULL |  |  | Namespaced entity type, e.g. TRN.Trip. |  |
| EntityId | BIGINT | NULL |  |  | Internal id of the affected row. |  |
| OldValues | NVARCHAR(MAX) | NULL |  |  | Values before the change (JSON). | CHECK ISJSON = 1 when not NULL. |
| NewValues | NVARCHAR(MAX) | NULL |  |  | Values after the change (JSON). | CHECK ISJSON = 1. |
| IpAddress | VARCHAR(45) | NULL |  |  | Client IP address (IPv4 or IPv6). |  |

**Primary key:** `PK_AuditLogs (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_AuditLogs_Tenants_TenantId`: TenantId → core.Tenants(Id) — optional
- `FK_AuditLogs_Companies_CompanyId`: CompanyId → core.Companies(Id) — optional
- `FK_AuditLogs_Users_UserId`: UserId → core.Users(Id) — optional

Standard foreign keys: —

**Indexes:**
- `IX_AuditLogs_Entity` (CompanyId, EntityType, EntityId, OccurredAt)
- `IX_AuditLogs_User` (UserId, OccurredAt)
- `IX_AuditLogs_TenantId` (TenantId) — supports the foreign key

**Relationships:**
- Many core.AuditLogs → one core.Tenants via TenantId (optional)
- Many core.AuditLogs → one core.Companies via CompanyId (optional)
- Many core.AuditLogs → one core.Users via UserId (optional)
- Not referenced by other tables

**Business rules:**
- No UPDATE or DELETE is ever allowed (enforced by database permissions and the application).
- No soft-delete or RowVersion columns — rows never change.


#### core.Notifications

**Owner:** Shared across all projects — no prefix · **Scope:** Tenant-scoped

**Purpose:** In-app notifications for a user (e.g. document expiry alerts, failed postings, e-invoice rejections).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Recipient. |  |
| CompanyId | BIGINT | NULL |  | FK → core.Companies | Company the notification relates to. |  |
| NotificationType | VARCHAR(40) | NOT NULL |  |  | Namespaced type, e.g. TRN.DriverDocumentExpiry. |  |
| Message | NVARCHAR(500) | NOT NULL |  |  | Text shown. |  |
| Link | NVARCHAR(300) | NULL |  |  | In-app link to the record; contains only hashed PublicIds. | Never contains an internal Id. |
| ReadAt | DATETIME2 | NULL |  |  | When read; NULL = unread. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Notifications (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Notifications_Users_UserId`: UserId → core.Users(Id) — required
- `FK_Notifications_Companies_CompanyId`: CompanyId → core.Companies(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Notifications_PublicId` (PublicId)

**Indexes:**
- `IX_Notifications_Unread` (UserId, ReadAt)
- `IX_Notifications_CompanyId` (CompanyId) — supports the foreign key

**Relationships:**
- Many core.Notifications → one core.Users via UserId (required)
- Many core.Notifications → one core.Companies via CompanyId (optional)
- Not referenced by other tables


#### core.Announcements

**Owner:** Shared across all projects — no prefix · **Scope:** Platform-wide

**Purpose:** Planned-maintenance and service notices published by the provider to all tenants.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| Title | NVARCHAR(200) | NOT NULL |  |  | Notice title. |  |
| Message | NVARCHAR(1000) | NOT NULL |  |  | Notice text. |  |
| StartsAt | DATETIME2 | NOT NULL |  |  | Show from. |  |
| EndsAt | DATETIME2 | NOT NULL |  |  | Show until. | EndsAt > StartsAt (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Announcements (Id)`

Standard foreign keys: CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Announcements_PublicId` (PublicId)

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### core.MaintenanceLogs

**Owner:** Shared across all projects — no prefix · **Scope:** Append-only log

**Purpose:** Append-only log of maintenance-zone actions (bulk soft deletes and restores) performed by administrators in any module.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal key. | BIGINT IDENTITY. |
| TenantId | BIGINT | NOT NULL |  | FK → core.Tenants | Tenant where the action ran. |  |
| CompanyId | BIGINT | NOT NULL |  | FK → core.Companies | Company where the action ran. |  |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Administrator who performed the action. |  |
| Action | VARCHAR(40) | NOT NULL |  |  | Action, e.g. BulkSoftDelete, Restore. |  |
| RecordCounts | NVARCHAR(MAX) | NOT NULL |  |  | Rows affected per entity type (JSON). | CHECK ISJSON = 1. |
| OccurredAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | UTC time. |  |

**Primary key:** `PK_MaintenanceLogs (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_MaintenanceLogs_Tenants_TenantId`: TenantId → core.Tenants(Id) — required
- `FK_MaintenanceLogs_Companies_CompanyId`: CompanyId → core.Companies(Id) — required
- `FK_MaintenanceLogs_Users_UserId`: UserId → core.Users(Id) — required

Standard foreign keys: —

**Indexes:**
- `IX_MaintenanceLogs_TenantId` (TenantId) — supports the foreign key
- `IX_MaintenanceLogs_CompanyId` (CompanyId) — supports the foreign key
- `IX_MaintenanceLogs_UserId` (UserId) — supports the foreign key

**Relationships:**
- Many core.MaintenanceLogs → one core.Tenants via TenantId (required)
- Many core.MaintenanceLogs → one core.Companies via CompanyId (required)
- Many core.MaintenanceLogs → one core.Users via UserId (required)
- Not referenced by other tables

**Business rules:**
- Append-only; never updated or deleted.


### Schema `trn` — Transportation only — every table carries the TRN_ prefix

#### trn.TRN_OwnershipTypes

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Truck ownership types (e.g. Company, Rented, Owner-driver), each classified as own fleet or external fleet. The class decides which monthly driver rate applies.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Type name. | Unique within the company. |
| FleetClass | VARCHAR(10) | NOT NULL |  |  | Own or external fleet. | CHECK IN ('Own','External'). |
| IsBuiltIn | BIT | NOT NULL | 0 |  | 1 for the built-in "Company" type (FleetClass Own); cannot be renamed or deleted. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_OwnershipTypes (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_OwnershipTypes_PublicId` (PublicId)
- `UX_TRN_OwnershipTypes_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One trn.TRN_OwnershipTypes → many trn.TRN_Drivers via OwnershipTypeId

**Business rules:**
- BR-029: Company type → own-fleet rate; any other type → external-fleet rate.


#### trn.TRN_Partners

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Partner (sub-contracted) haulage companies whose trucks carry loads for the company; the company pays them per ton.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(200) | NOT NULL |  |  | Partner name. | Unique among active partners in the company. |
| TaxNumber | VARCHAR(30) | NULL |  |  | Tax registration number. | Required on parties receiving a tax invoice when e-invoicing is enabled (application rule). |
| Phone | VARCHAR(20) | NULL |  |  | Phone number. | Market mobile format; launch market 07[789]XXXXXXX or 06XXXXXXX (configurable per market). |
| Address | NVARCHAR(300) | NULL |  |  | Postal / street address. |  |
| ContactPerson | NVARCHAR(150) | NULL |  |  | Main contact person. |  |
| BankName | NVARCHAR(100) | NULL |  |  | Name of the bank. |  |
| Iban | VARCHAR(34) | NULL |  |  | IBAN for bank payments. | Country format; launch market JO + 28 characters (30 total). Required when PaymentMethod = Bank. |
| CliqAlias | NVARCHAR(50) | NULL |  |  | CliQ alias for instant payments. | Required when PaymentMethod = CliQ. |
| Notes | NVARCHAR(1000) | NULL |  |  | Free-text notes. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Partners (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Partners_PublicId` (PublicId)
- `UX_TRN_Partners_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0 AND Status = 'Active'

**Relationships:**
- References: none (besides standard keys)
- One trn.TRN_Partners → many trn.TRN_Drivers via PartnerId
- One trn.TRN_Partners → many trn.TRN_Tariffs via PartnerId
- One trn.TRN_Partners → many trn.TRN_Trips via PartnerId
- One trn.TRN_Partners → many trn.TRN_PartnerPayments via PartnerId

**Business rules:**
- Cannot be deleted while referenced by trips, drivers, partner payments or tariffs (BR-040).


#### trn.TRN_Drivers

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Drivers and the single truck registered with each driver (one truck per driver; truck details are edited from the driver profile).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| FullName | NVARCHAR(150) | NOT NULL |  |  | Driver full name. | Required. |
| NationalId | CHAR(10) | NOT NULL |  |  | National ID number. | Exactly 10 digits; unique within the company. |
| Phone1 | VARCHAR(20) | NOT NULL |  |  | Main mobile number. | Required; market mobile format. |
| Phone2 | VARCHAR(20) | NULL |  |  | Second mobile number. | Market mobile format when given. |
| Address | NVARCHAR(300) | NULL |  |  | Postal / street address. |  |
| LicenceNo | VARCHAR(30) | NOT NULL |  |  | Driving licence number. | Required. |
| LicenceType | NVARCHAR(30) | NOT NULL |  |  | Licence category. | Required. |
| LicenceExpiry | DATE | NOT NULL |  |  | Licence expiry date. | Expired → warning on the dashboard and trip form; trips are not blocked. |
| PaymentMethod | VARCHAR(10) | NOT NULL | 'Cash' |  | How wages and advances are paid to the driver. | CHECK IN ('Cash','Bank','CliQ'). |
| BankName | NVARCHAR(100) | NULL |  |  | Name of the bank. |  |
| Iban | VARCHAR(34) | NULL |  |  | IBAN for bank payments. | Country format; launch market JO + 28 characters (30 total). Required when PaymentMethod = Bank. |
| CliqAlias | NVARCHAR(50) | NULL |  |  | CliQ alias for instant payments. | Required when PaymentMethod = CliQ. |
| PlateNumber | VARCHAR(20) | NOT NULL |  |  | Truck plate number. | Unique within the company. |
| TruckType | NVARCHAR(50) | NULL |  |  | Truck type / model. |  |
| TrailerNo | VARCHAR(20) | NULL |  |  | Trailer number. |  |
| CapacityTons | DECIMAL(18,3) | NOT NULL |  |  | Truck load capacity in tons. | CHECK > 0; a loaded weight above it gives a warning. |
| OwnershipTypeId | BIGINT | NOT NULL |  | FK → trn.TRN_OwnershipTypes | Ownership type of the truck (decides own vs external fleet). |  |
| PartnerId | BIGINT | NULL |  | FK → trn.TRN_Partners | Partner company that owns the truck, for partner drivers. | NULL for company and external drivers. |
| RegistrationExpiry | DATE | NOT NULL |  |  | Truck registration expiry. | Warning only. |
| InsuranceExpiry | DATE | NOT NULL |  |  | Truck insurance expiry. | Warning only. |
| InspectionExpiry | DATE | NOT NULL |  |  | Truck inspection expiry. | Warning only. |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| Notes | NVARCHAR(1000) | NULL |  |  | Free-text notes. |  |
| CustomValues | NVARCHAR(MAX) | NULL |  |  | Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions). | CHECK ISJSON(CustomValues) = 1; values validated against the definitions by the application. Custom-field values (JSON) |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Drivers (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Drivers_TRN_OwnershipTypes_OwnershipTypeId`: OwnershipTypeId → trn.TRN_OwnershipTypes(Id) — required
- `FK_TRN_Drivers_TRN_Partners_PartnerId`: PartnerId → trn.TRN_Partners(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Drivers_PublicId` (PublicId)
- `UX_TRN_Drivers_CompanyId_NationalId` (CompanyId, NationalId) WHERE IsDeleted = 0
- `UX_TRN_Drivers_CompanyId_PlateNumber` (CompanyId, PlateNumber) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Drivers_Expiry` (CompanyId, LicenceExpiry, RegistrationExpiry, InsuranceExpiry, InspectionExpiry) WHERE Status = 'Active'
- `IX_TRN_Drivers_OwnershipTypeId` (OwnershipTypeId) — supports the foreign key
- `IX_TRN_Drivers_PartnerId` (PartnerId) — supports the foreign key

**Relationships:**
- Many trn.TRN_Drivers → one trn.TRN_OwnershipTypes via OwnershipTypeId (required)
- Many trn.TRN_Drivers → one trn.TRN_Partners via PartnerId (optional)
- One trn.TRN_Drivers → many trn.TRN_Trips via DriverId
- One trn.TRN_Drivers → many trn.TRN_Advances via DriverId
- One trn.TRN_Drivers → many trn.TRN_Settlements via DriverId

**Business rules:**
- Full driver details are visible to every user with the drivers view permission (no masking).
- Cannot be deleted while referenced by trips, advances or settlements (BR-040).


#### trn.TRN_Customers

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Transport customers who are invoiced for trips.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(200) | NOT NULL |  |  | Customer name. | Unique among active customers in the company. |
| TaxNumber | VARCHAR(30) | NULL |  |  | Tax registration number. | Required on parties receiving a tax invoice when e-invoicing is enabled (application rule). |
| RegistrationNo | VARCHAR(30) | NULL |  |  | Commercial registration or national ID of the customer. |  |
| Phone | VARCHAR(20) | NULL |  |  | Phone number. | Market mobile format; launch market 07[789]XXXXXXX or 06XXXXXXX (configurable per market). |
| Email | NVARCHAR(150) | NULL |  |  | Email address. | Valid email format. |
| Address | NVARCHAR(300) | NULL |  |  | Postal / street address. |  |
| City | NVARCHAR(100) | NULL |  |  | City. |  |
| ContactPerson | NVARCHAR(150) | NULL |  |  | Main contact person. |  |
| PaymentTermsDays | INT | NOT NULL | 0 |  | Credit days; invoice due date = invoice date + this value. | CHECK ≥ 0. |
| ApArPartyId | BIGINT | NULL |  | FK → acc.ApArParties | Optional link to the same company kept as a party in shared Accounting. | One direction only (TRN → shared). |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| Notes | NVARCHAR(1000) | NULL |  |  | Free-text notes. |  |
| CustomValues | NVARCHAR(MAX) | NULL |  |  | Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions). | CHECK ISJSON(CustomValues) = 1; values validated against the definitions by the application. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Customers (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Customers_ApArParties_ApArPartyId`: ApArPartyId → acc.ApArParties(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Customers_PublicId` (PublicId)
- `UX_TRN_Customers_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0 AND Status = 'Active'

**Indexes:**
- `IX_TRN_Customers_ApArPartyId` (ApArPartyId) — supports the foreign key

**Relationships:**
- Many trn.TRN_Customers → one acc.ApArParties via ApArPartyId (optional)
- One trn.TRN_Customers → many trn.TRN_Tariffs via CustomerId
- One trn.TRN_Customers → many trn.TRN_Trips via CustomerId
- One trn.TRN_Customers → many trn.TRN_Invoices via CustomerId
- One trn.TRN_Customers → many trn.TRN_CreditNotes via CustomerId
- One trn.TRN_Customers → many trn.TRN_Receipts via CustomerId

**Business rules:**
- Customers cannot be created from the trip form (FR-167).
- Cannot be deleted while referenced by trips, invoices, receipts, credit notes or tariffs (BR-040).


#### trn.TRN_Materials

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Materials carried (e.g. phosphate, potash, sand).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Material name. | Unique within the company. |
| Unit | NVARCHAR(20) | NOT NULL | N'ton' |  | Unit of measure. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Materials (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Materials_PublicId` (PublicId)
- `UX_TRN_Materials_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One trn.TRN_Materials → many trn.TRN_Tariffs via MaterialId
- One trn.TRN_Materials → many trn.TRN_Trips via MaterialId


#### trn.TRN_LoadingSites

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Places where loads are picked up (mines, quarries). Monthly driver rates are set per loading site.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Site name. | Unique within the company. |
| Area | NVARCHAR(100) | NULL |  |  | Area / region of the site. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_LoadingSites (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_LoadingSites_PublicId` (PublicId)
- `UX_TRN_LoadingSites_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One trn.TRN_LoadingSites → many trn.TRN_Tariffs via LoadingSiteId
- One trn.TRN_LoadingSites → many trn.TRN_MonthlySiteRates via LoadingSiteId
- One trn.TRN_LoadingSites → many trn.TRN_Trips via LoadingSiteId


#### trn.TRN_UnloadingSites

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Places where loads are delivered (ports, plants, warehouses).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Site name. | Unique within the company. |
| Area | NVARCHAR(100) | NULL |  |  | Area / region of the site. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_UnloadingSites (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_UnloadingSites_PublicId` (PublicId)
- `UX_TRN_UnloadingSites_CompanyId_Name` (CompanyId, Name) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One trn.TRN_UnloadingSites → many trn.TRN_Tariffs via UnloadingSiteId
- One trn.TRN_UnloadingSites → many trn.TRN_Trips via UnloadingSiteId


#### trn.TRN_Tariffs

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Price rules per ton: the customer price (PartyType Customer) or the partner rate (PartyType Partner). A NULL dimension means "All". The most specific matching line wins.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| PartyType | VARCHAR(10) | NOT NULL |  |  | Whose price this line sets. | CHECK IN ('Customer','Partner'). |
| CustomerId | BIGINT | NULL |  | FK → trn.TRN_Customers | Customer the price applies to; NULL = all customers. | Must be NULL when PartyType = Partner (CHECK). |
| PartnerId | BIGINT | NULL |  | FK → trn.TRN_Partners | Partner the rate applies to; NULL = all partners. | Must be NULL when PartyType = Customer (CHECK). |
| MaterialId | BIGINT | NULL |  | FK → trn.TRN_Materials | Material; NULL = all materials. |  |
| LoadingSiteId | BIGINT | NULL |  | FK → trn.TRN_LoadingSites | Loading site; NULL = all. |  |
| UnloadingSiteId | BIGINT | NULL |  | FK → trn.TRN_UnloadingSites | Unloading site; NULL = all. |  |
| Price | DECIMAL(18,4) | NOT NULL |  |  | Price per ton. | CHECK > 0. |
| ValidFrom | DATE | NULL |  |  | First valid date; NULL = from the beginning. |  |
| ValidTo | DATE | NULL |  |  | Last valid date; NULL = open-ended. | ValidTo ≥ ValidFrom (CHECK). |
| Specificity | TINYINT | NOT NULL |  |  | Score used to pick the most specific line: party 8 + material 4 + loading site 2 + unloading site 1. | Computed by the server on save (persisted computed column); CHECK BETWEEN 0 AND 15. |
| Notes | NVARCHAR(500) | NULL |  |  | Free-text notes. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Tariffs (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Tariffs_TRN_Customers_CustomerId`: CustomerId → trn.TRN_Customers(Id) — optional
- `FK_TRN_Tariffs_TRN_Partners_PartnerId`: PartnerId → trn.TRN_Partners(Id) — optional
- `FK_TRN_Tariffs_TRN_Materials_MaterialId`: MaterialId → trn.TRN_Materials(Id) — optional
- `FK_TRN_Tariffs_TRN_LoadingSites_LoadingSiteId`: LoadingSiteId → trn.TRN_LoadingSites(Id) — optional
- `FK_TRN_Tariffs_TRN_UnloadingSites_UnloadingSiteId`: UnloadingSiteId → trn.TRN_UnloadingSites(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Tariffs_PublicId` (PublicId)
- `UX_TRN_Tariffs_CompanyId_PartyType_CustomerId_PartnerId_MaterialId_LoadingSiteId_UnloadingSiteId_ValidFrom` (CompanyId, PartyType, CustomerId, PartnerId, MaterialId, LoadingSiteId, UnloadingSiteId, ValidFrom) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Tariffs_Lookup` (CompanyId, PartyType, Status, Specificity DESC, ValidFrom DESC)
- `IX_TRN_Tariffs_CustomerId` (CustomerId) — supports the foreign key
- `IX_TRN_Tariffs_PartnerId` (PartnerId) — supports the foreign key
- `IX_TRN_Tariffs_MaterialId` (MaterialId) — supports the foreign key
- `IX_TRN_Tariffs_LoadingSiteId` (LoadingSiteId) — supports the foreign key
- `IX_TRN_Tariffs_UnloadingSiteId` (UnloadingSiteId) — supports the foreign key

**Relationships:**
- Many trn.TRN_Tariffs → one trn.TRN_Customers via CustomerId (optional)
- Many trn.TRN_Tariffs → one trn.TRN_Partners via PartnerId (optional)
- Many trn.TRN_Tariffs → one trn.TRN_Materials via MaterialId (optional)
- Many trn.TRN_Tariffs → one trn.TRN_LoadingSites via LoadingSiteId (optional)
- Many trn.TRN_Tariffs → one trn.TRN_UnloadingSites via UnloadingSiteId (optional)
- Not referenced by other tables

**Business rules:**
- BR-028: highest Specificity wins; ties → latest ValidFrom.
- Every change is written to TRN_RateHistory.
- When "Lock tariffs" is on, trips must use the tariff price; otherwise a different price is allowed and flagged as a tariff breach.


#### trn.TRN_MonthlySiteRates

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Driver rate per ton for each loading site and month, with separate own-fleet and external-fleet rates.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| RateMonth | CHAR(7) | NOT NULL |  |  | Month the rate applies to. | Format YYYY-MM (CHECK LIKE '[12][0-9][0-9][0-9]-[01][0-9]'). |
| LoadingSiteId | BIGINT | NOT NULL |  | FK → trn.TRN_LoadingSites | Loading site. |  |
| OwnFleetRate | DECIMAL(18,4) | NULL |  |  | Rate per ton for own-fleet trucks. | CHECK > 0 when not NULL. |
| ExternalFleetRate | DECIMAL(18,4) | NULL |  |  | Rate per ton for external-fleet trucks. | CHECK > 0 when not NULL; at least one of the two rates is required (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_MonthlySiteRates (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_MonthlySiteRates_TRN_LoadingSites_LoadingSiteId`: LoadingSiteId → trn.TRN_LoadingSites(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_MonthlySiteRates_PublicId` (PublicId)
- `UX_TRN_MonthlySiteRates_CompanyId_RateMonth_LoadingSiteId` (CompanyId, RateMonth, LoadingSiteId) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_MonthlySiteRates_LoadingSiteId` (LoadingSiteId) — supports the foreign key

**Relationships:**
- Many trn.TRN_MonthlySiteRates → one trn.TRN_LoadingSites via LoadingSiteId (required)
- Not referenced by other tables

**Business rules:**
- BR-029: rate looked up by company + loading site + month of the loading date + fleet class.
- BR-030: changing a rate never changes saved trips.
- Every change is written to TRN_RateHistory.


#### trn.TRN_RateHistory

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Change history of tariffs and monthly site rates (who changed what, old and new values).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| SourceType | VARCHAR(15) | NOT NULL |  |  | Which table changed. | CHECK IN ('Tariff','MonthlySiteRate'). |
| SourceId | BIGINT | NOT NULL |  |  | Id of the changed TRN_Tariffs or TRN_MonthlySiteRates row (no FK: polymorphic). |  |
| OldValues | NVARCHAR(MAX) | NULL |  |  | Values before the change (JSON). | CHECK ISJSON = 1 when not NULL. |
| NewValues | NVARCHAR(MAX) | NOT NULL |  |  | Values after the change (JSON). | CHECK ISJSON = 1. |
| ChangedBy | BIGINT | NOT NULL |  | FK → core.Users | User who made the change. |  |
| ChangedAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | UTC time of the change. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_RateHistory (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_RateHistory_Users_ChangedBy`: ChangedBy → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_RateHistory_PublicId` (PublicId)

**Indexes:**
- `IX_TRN_RateHistory_Source` (CompanyId, SourceType, SourceId, ChangedAt)
- `IX_TRN_RateHistory_ChangedBy` (ChangedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_RateHistory → one core.Users via ChangedBy (required)
- Not referenced by other tables

**Business rules:**
- Insert-only in practice; rows are never edited.


#### trn.TRN_Trips

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** A shipment: one truck load from a loading site to an unloading site for a customer. Prices, the driver rate and all amounts are calculated and frozen at save.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| ReceiptNo | NVARCHAR(30) | NOT NULL |  |  | Weighbridge / delivery receipt number as entered. | Required. |
| ReceiptNoKey | NVARCHAR(30) | NOT NULL |  |  | ReceiptNo normalised (upper-case, spaces removed) for uniqueness. | Unique among non-deleted trips in the company (ignores case and spaces). |
| TripDate | DATE | NOT NULL |  |  | Trip date. | Not in the future; not in a closed period. |
| LoadingDate | DATE | NOT NULL |  |  | Loading date; its month selects the monthly driver rate. | Not in the future; not in a closed period. |
| UnloadingDate | DATE | NULL |  |  | Unloading date. | ≥ LoadingDate when given (CHECK). |
| DriverId | BIGINT | NOT NULL |  | FK → trn.TRN_Drivers | Driver (and truck) that carried the load. |  |
| CustomerId | BIGINT | NOT NULL |  | FK → trn.TRN_Customers | Customer billed. |  |
| LoadingSiteId | BIGINT | NOT NULL |  | FK → trn.TRN_LoadingSites | Loading site. |  |
| UnloadingSiteId | BIGINT | NOT NULL |  | FK → trn.TRN_UnloadingSites | Unloading site. |  |
| MaterialId | BIGINT | NOT NULL |  | FK → trn.TRN_Materials | Material carried. |  |
| PartnerId | BIGINT | NULL |  | FK → trn.TRN_Partners | Partner company for sub-contracted trips (copied from the driver). | NULL for own/external fleet trips. |
| TruckPlateAtSave | VARCHAR(20) | NOT NULL |  |  | Driver's plate number copied at save, so later truck changes do not alter history. |  |
| FleetClass | VARCHAR(10) | NOT NULL |  |  | Fleet class at save (from the driver's ownership type). | CHECK IN ('Own','External'). |
| LoadedWeight | DECIMAL(18,3) | NOT NULL |  |  | Weight loaded, tons. | CHECK > 0; warning above truck capacity. |
| UnloadedWeight | DECIMAL(18,3) | NULL |  |  | Weight delivered, tons. | CHECK ≥ 0; warning above loaded × 1.02. |
| BillingQty | DECIMAL(18,3) | NOT NULL |  |  | Quantity billed to the customer (unloaded weight if "Bill on unloaded weight" is on and recorded, else loaded). | Calculated by the server. |
| WageQty | DECIMAL(18,3) | NOT NULL |  |  | Quantity the driver/partner is paid on (loaded if "Pay driver on loaded weight" is on, else unloaded, falling back to loaded). | Calculated by the server. |
| CustomerPrice | DECIMAL(18,4) | NOT NULL |  |  | Price per ton charged to the customer. | CHECK ≥ 0; defaults to the resolved tariff price. |
| TariffPrice | DECIMAL(18,4) | NULL |  |  | Tariff price found at save (NULL if no tariff matched). |  |
| DriverRate | DECIMAL(18,4) | NOT NULL |  |  | Driver rate per ton frozen at save (0 for partner trips). | CHECK ≥ 0. |
| RateSource | VARCHAR(10) | NOT NULL |  |  | Where the driver rate came from. | CHECK IN ('Monthly','Manual'). |
| RateMonth | CHAR(7) | NOT NULL |  |  | Month of the monthly rate used (YYYY-MM). | Format YYYY-MM. |
| PartnerRate | DECIMAL(18,4) | NULL |  |  | Partner rate per ton (partner trips). | Required when PartnerId is not NULL. |
| Revenue | DECIMAL(18,3) | NOT NULL |  |  | BillingQty × CustomerPrice. | Rounded half-up (BR-021). |
| DriverWage | DECIMAL(18,3) | NOT NULL |  |  | WageQty × DriverRate (0 for partner trips). | Rounded half-up. |
| PartnerCost | DECIMAL(18,3) | NOT NULL | 0 |  | WageQty × PartnerRate (partner trips), else 0. | Rounded half-up. |
| ShortageWeight | DECIMAL(18,3) | NULL |  |  | LoadedWeight − UnloadedWeight. | NULL until unloaded weight is recorded. |
| ShortagePct | DECIMAL(9,4) | NULL |  |  | ShortageWeight ÷ LoadedWeight × 100. |  |
| IsShortageFlagged | BIT | NOT NULL | 0 |  | 1 when ShortagePct is above the company tolerance (default 0.5 %). |  |
| IsTariffBreach | BIT | NOT NULL | 0 |  | 1 when CustomerPrice differs from TariffPrice; breach value = (price − tariff) × BillingQty. |  |
| IsManualRate | BIT | NOT NULL | 0 |  | 1 when the driver rate was typed manually instead of the monthly rate. |  |
| NeedsReview | BIT | NOT NULL | 0 |  | 1 for rows imported in tolerant mode with defaults applied. |  |
| ReviewNote | NVARCHAR(500) | NULL |  |  | What needs review. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Trip status. | CHECK IN ('Active','Void'). |
| InvoiceId | BIGINT | NULL |  | FK → trn.TRN_Invoices | Invoice the trip is billed on. | A trip belongs to at most one active invoice; set/cleared by the invoice service. |
| SettlementId | BIGINT | NULL |  | FK → trn.TRN_Settlements | Driver settlement that paid the wage. | A trip belongs to at most one active settlement (BR-031). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | Wage accrual or partner-cost entry posted on save. | NULL when wage accrual is off and the trip is not a partner trip. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| ImportBatchId | BIGINT | NULL |  | FK → cfg.ImportBatches | Import batch that created the trip. |  |
| Notes | NVARCHAR(1000) | NULL |  |  | Free-text notes. |  |
| CustomValues | NVARCHAR(MAX) | NULL |  |  | Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions). | CHECK ISJSON(CustomValues) = 1; values validated against the definitions by the application. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Trips (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Trips_TRN_Drivers_DriverId`: DriverId → trn.TRN_Drivers(Id) — required
- `FK_TRN_Trips_TRN_Customers_CustomerId`: CustomerId → trn.TRN_Customers(Id) — required
- `FK_TRN_Trips_TRN_LoadingSites_LoadingSiteId`: LoadingSiteId → trn.TRN_LoadingSites(Id) — required
- `FK_TRN_Trips_TRN_UnloadingSites_UnloadingSiteId`: UnloadingSiteId → trn.TRN_UnloadingSites(Id) — required
- `FK_TRN_Trips_TRN_Materials_MaterialId`: MaterialId → trn.TRN_Materials(Id) — required
- `FK_TRN_Trips_TRN_Partners_PartnerId`: PartnerId → trn.TRN_Partners(Id) — optional
- `FK_TRN_Trips_TRN_Invoices_InvoiceId`: InvoiceId → trn.TRN_Invoices(Id) — optional
- `FK_TRN_Trips_TRN_Settlements_SettlementId`: SettlementId → trn.TRN_Settlements(Id) — optional
- `FK_TRN_Trips_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_Trips_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional
- `FK_TRN_Trips_ImportBatches_ImportBatchId`: ImportBatchId → cfg.ImportBatches(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Trips_PublicId` (PublicId)
- `UX_TRN_Trips_CompanyId_ReceiptNoKey` (CompanyId, ReceiptNoKey) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Trips_Date` (CompanyId, TripDate) INCLUDE (Status)
- `IX_TRN_Trips_Unbilled` (CompanyId, CustomerId, Status) WHERE InvoiceId IS NULL
- `IX_TRN_Trips_Unsettled` (CompanyId, DriverId, Status) WHERE SettlementId IS NULL
- `IX_TRN_Trips_DriverId` (DriverId) — supports the foreign key
- `IX_TRN_Trips_CustomerId` (CustomerId) — supports the foreign key
- `IX_TRN_Trips_LoadingSiteId` (LoadingSiteId) — supports the foreign key
- `IX_TRN_Trips_UnloadingSiteId` (UnloadingSiteId) — supports the foreign key
- `IX_TRN_Trips_MaterialId` (MaterialId) — supports the foreign key
- `IX_TRN_Trips_PartnerId` (PartnerId) — supports the foreign key
- `IX_TRN_Trips_InvoiceId` (InvoiceId) — supports the foreign key
- `IX_TRN_Trips_SettlementId` (SettlementId) — supports the foreign key
- `IX_TRN_Trips_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_Trips_VoidedBy` (VoidedBy) — supports the foreign key
- `IX_TRN_Trips_ImportBatchId` (ImportBatchId) — supports the foreign key

**Relationships:**
- Many trn.TRN_Trips → one trn.TRN_Drivers via DriverId (required)
- Many trn.TRN_Trips → one trn.TRN_Customers via CustomerId (required)
- Many trn.TRN_Trips → one trn.TRN_LoadingSites via LoadingSiteId (required)
- Many trn.TRN_Trips → one trn.TRN_UnloadingSites via UnloadingSiteId (required)
- Many trn.TRN_Trips → one trn.TRN_Materials via MaterialId (required)
- Many trn.TRN_Trips → one trn.TRN_Partners via PartnerId (optional)
- Many trn.TRN_Trips → one trn.TRN_Invoices via InvoiceId (optional)
- Many trn.TRN_Trips → one trn.TRN_Settlements via SettlementId (optional)
- Many trn.TRN_Trips → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_Trips → one core.Users via VoidedBy (optional)
- Many trn.TRN_Trips → one cfg.ImportBatches via ImportBatchId (optional)
- One trn.TRN_Trips → many trn.TRN_InvoiceLines via TripId
- One trn.TRN_Trips → many trn.TRN_CreditNoteLines via TripId

**Business rules:**
- Margin = Revenue − DriverWage − PartnerCost (calculated, not stored).
- FR-011: void requires a reason and posts a reversal; blocked if invoiced, settled or in a closed period.
- BR-035: editing reverses the previous entry and posts a new one.
- Expired driver documents give a warning only; the trip is not blocked.


#### trn.TRN_Invoices

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Customer invoices: trip-based (one line per trip) or manual (free lines). Drafts have no number; approval assigns the number, posts the entry and queues e-invoicing.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NULL |  |  | Invoice number, assigned at approval. | NULL while Draft; unique within the company when set. |
| InvoiceType | VARCHAR(10) | NOT NULL |  |  | Trip-based or manual. | CHECK IN ('Trip','Manual'). |
| CustomerId | BIGINT | NOT NULL |  | FK → trn.TRN_Customers | Customer invoiced. |  |
| InvoiceDate | DATE | NOT NULL |  |  | Invoice date. | Not in a closed period. |
| DueDate | DATE | NOT NULL |  |  | Due date; default invoice date + customer payment terms. | ≥ InvoiceDate (CHECK). |
| Subtotal | DECIMAL(18,3) | NOT NULL |  |  | Σ trip revenue, or Σ quantity × unit price for manual lines. | CHECK ≥ 0 |
| Discount | DECIMAL(18,3) | NOT NULL | 0 |  | Discount amount. | 0 ≤ Discount ≤ Subtotal (CHECK). |
| ExtraFees | DECIMAL(18,3) | NOT NULL | 0 |  | Extra fees added. | CHECK ≥ 0 |
| ExtraFeesDescription | NVARCHAR(200) | NULL |  |  | What the fees are for. | Required when ExtraFees > 0. |
| BaseAmount | DECIMAL(18,3) | NOT NULL |  |  | Subtotal − Discount + ExtraFees. | CHECK (BaseAmount = Subtotal − Discount + ExtraFees). |
| TaxRate | DECIMAL(5,2) | NOT NULL |  |  | Tax rate % copied from the company at issue. | CHECK BETWEEN 0 AND 100. |
| Tax | DECIMAL(18,3) | NOT NULL |  |  | round(BaseAmount × TaxRate) (BR-023). | CHECK ≥ 0 |
| Total | DECIMAL(18,3) | NOT NULL |  |  | BaseAmount + Tax. | CHECK (Total = BaseAmount + Tax). |
| PaidAmount | DECIMAL(18,3) | NOT NULL | 0 |  | Total of active payments/receipts against the document (maintained by the server). | CHECK ≥ 0 |
| ReturnedAmount | DECIMAL(18,3) | NOT NULL | 0 |  | Total of active returns/credit notes against the document (maintained by the server). | CHECK ≥ 0 |
| RemainingAmount | DECIMAL(18,3) | NOT NULL |  |  | Total − active receipts − active credit notes. | CHECK ≥ 0. |
| Status | VARCHAR(10) | NOT NULL | 'Draft' |  | Invoice status. | CHECK IN ('Draft','Approved','Closed','Void'). Closed = fully paid/returned. |
| LayoutType | VARCHAR(10) | NOT NULL | 'TaxInvoice' |  | Print layout used. | CHECK IN ('Claim','TaxInvoice'). |
| Notes | NVARCHAR(1000) | NULL |  |  | Free-text notes. |  |
| ApprovedBy | BIGINT | NULL |  | FK → core.Users | User who approved. | Required when Status ≠ Draft. |
| ApprovedAt | DATETIME2 | NULL |  |  | Approval time. | Required when Status ≠ Draft. |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | Revenue entry posted at approval. |  |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Invoices (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Invoices_TRN_Customers_CustomerId`: CustomerId → trn.TRN_Customers(Id) — required
- `FK_TRN_Invoices_Users_ApprovedBy`: ApprovedBy → core.Users(Id) — optional
- `FK_TRN_Invoices_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_Invoices_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Invoices_PublicId` (PublicId)
- `UX_TRN_Invoices_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0 AND Number IS NOT NULL

**Indexes:**
- `IX_TRN_Invoices_Customer` (CompanyId, CustomerId, Status)
- `IX_TRN_Invoices_Date` (CompanyId, InvoiceDate)
- `IX_TRN_Invoices_CustomerId` (CustomerId) — supports the foreign key
- `IX_TRN_Invoices_ApprovedBy` (ApprovedBy) — supports the foreign key
- `IX_TRN_Invoices_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_Invoices_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_Invoices → one trn.TRN_Customers via CustomerId (required)
- Many trn.TRN_Invoices → one core.Users via ApprovedBy (optional)
- Many trn.TRN_Invoices → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_Invoices → one core.Users via VoidedBy (optional)
- One trn.TRN_Invoices → many trn.TRN_Trips via InvoiceId
- One trn.TRN_Invoices → many trn.TRN_InvoiceLines via InvoiceId
- One trn.TRN_Invoices → many trn.TRN_CreditNotes via InvoiceId
- One trn.TRN_Invoices → many trn.TRN_EInvoiceSubmissions via InvoiceId
- One trn.TRN_Invoices → many trn.TRN_Receipts via InvoiceId

**Business rules:**
- FR-041: with "Invoice approval" on (default), Save creates a Draft; off → Save approves directly.
- An approved e-invoice can only be cancelled by a credit note.
- Void blocked while active receipts or credit notes exist (BR-040).
- Invoicing is disabled until all required posting keys are mapped.


#### trn.TRN_InvoiceLines

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Invoice lines: one line per trip on trip invoices, or free description/quantity/price lines on manual invoices.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| InvoiceId | BIGINT | NOT NULL |  | FK → trn.TRN_Invoices | Invoice. |  |
| TripId | BIGINT | NULL |  | FK → trn.TRN_Trips | Trip billed by this line (trip invoices). | Required on Trip invoices, NULL on Manual invoices. |
| Description | NVARCHAR(300) | NULL |  |  | Line text (manual lines). | Required when TripId is NULL. |
| Quantity | DECIMAL(18,3) | NOT NULL |  |  | Quantity (tons for trips). | CHECK > 0. |
| UnitPrice | DECIMAL(18,4) | NOT NULL |  |  | Price per unit. | CHECK ≥ 0 |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Quantity × UnitPrice, rounded half-up. | CHECK ≥ 0 |
| SortOrder | INT | NOT NULL | 0 |  | Display order (ascending). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_InvoiceLines (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_InvoiceLines_TRN_Invoices_InvoiceId`: InvoiceId → trn.TRN_Invoices(Id) — required
- `FK_TRN_InvoiceLines_TRN_Trips_TripId`: TripId → trn.TRN_Trips(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_InvoiceLines_PublicId` (PublicId)

**Indexes:**
- `IX_TRN_InvoiceLines_InvoiceId` (InvoiceId) — supports the foreign key
- `IX_TRN_InvoiceLines_TripId` (TripId) — supports the foreign key
- `IX_TRN_InvoiceLines_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many trn.TRN_InvoiceLines → one trn.TRN_Invoices via InvoiceId (required)
- Many trn.TRN_InvoiceLines → one trn.TRN_Trips via TripId (optional)
- One trn.TRN_InvoiceLines → many trn.TRN_CreditNoteLines via InvoiceLineId

**Business rules:**
- A trip can only be on one active invoice — enforced through TRN_Trips.InvoiceId (not a unique index here, so a voided invoice's lines keep their history).


#### trn.TRN_CreditNotes

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Full or partial returns of an approved invoice. Also the only way to cancel an e-invoiced invoice.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Credit note number. | Unique within the company. |
| InvoiceId | BIGINT | NOT NULL |  | FK → trn.TRN_Invoices | Invoice being credited. | Invoice must be Approved. |
| CustomerId | BIGINT | NOT NULL |  | FK → trn.TRN_Customers | Customer (same as the invoice). | Must equal the invoice customer. |
| CreditDate | DATE | NOT NULL |  |  | Credit note date. | Not in a closed period. |
| Reason | NVARCHAR(500) | NOT NULL |  |  | Why the credit note was issued. | Required. |
| Subtotal | DECIMAL(18,3) | NOT NULL |  |  | Revenue of the returned trips/quantities. | CHECK ≥ 0 |
| DiscountShare | DECIMAL(18,3) | NOT NULL |  |  | Proportional share of the invoice discount. | BR-025. |
| FeesShare | DECIMAL(18,3) | NOT NULL |  |  | Proportional share of the invoice extra fees. | BR-025. |
| Tax | DECIMAL(18,3) | NOT NULL |  |  | Tax at the invoice's rate. | CHECK ≥ 0 |
| Total | DECIMAL(18,3) | NOT NULL |  |  | Subtotal − DiscountShare + FeesShare + Tax. | ≤ invoice RemainingAmount (BR-026). |
| Status | VARCHAR(10) | NOT NULL | 'Posted' |  | Status. | CHECK IN ('Posted','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_CreditNotes (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_CreditNotes_TRN_Invoices_InvoiceId`: InvoiceId → trn.TRN_Invoices(Id) — required
- `FK_TRN_CreditNotes_TRN_Customers_CustomerId`: CustomerId → trn.TRN_Customers(Id) — required
- `FK_TRN_CreditNotes_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_CreditNotes_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_CreditNotes_PublicId` (PublicId)
- `UX_TRN_CreditNotes_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_CreditNotes_InvoiceId` (InvoiceId) — supports the foreign key
- `IX_TRN_CreditNotes_CustomerId` (CustomerId) — supports the foreign key
- `IX_TRN_CreditNotes_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_CreditNotes_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_CreditNotes → one trn.TRN_Invoices via InvoiceId (required)
- Many trn.TRN_CreditNotes → one trn.TRN_Customers via CustomerId (required)
- Many trn.TRN_CreditNotes → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_CreditNotes → one core.Users via VoidedBy (optional)
- One trn.TRN_CreditNotes → many trn.TRN_CreditNoteLines via CreditNoteId
- One trn.TRN_CreditNotes → many trn.TRN_EInvoiceSubmissions via CreditNoteId

**Business rules:**
- BR-027: a credit note returning everything still open absorbs rounding so the invoice remaining is exactly 0.000.
- Posts the mirror of the invoice entry for the returned share.


#### trn.TRN_CreditNoteLines

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** The trips or quantities returned by a credit note.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| CreditNoteId | BIGINT | NOT NULL |  | FK → trn.TRN_CreditNotes | Credit note. |  |
| InvoiceLineId | BIGINT | NOT NULL |  | FK → trn.TRN_InvoiceLines | Invoice line being returned. | Must belong to the credit note's invoice. |
| TripId | BIGINT | NULL |  | FK → trn.TRN_Trips | Trip returned (trip invoices). |  |
| Quantity | DECIMAL(18,3) | NOT NULL |  |  | Quantity returned. | CHECK > 0; ≤ line quantity not yet returned. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Revenue returned. | CHECK ≥ 0 |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_CreditNoteLines (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_CreditNoteLines_TRN_CreditNotes_CreditNoteId`: CreditNoteId → trn.TRN_CreditNotes(Id) — required
- `FK_TRN_CreditNoteLines_TRN_InvoiceLines_InvoiceLineId`: InvoiceLineId → trn.TRN_InvoiceLines(Id) — required
- `FK_TRN_CreditNoteLines_TRN_Trips_TripId`: TripId → trn.TRN_Trips(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_CreditNoteLines_PublicId` (PublicId)

**Indexes:**
- `IX_TRN_CreditNoteLines_CreditNoteId` (CreditNoteId) — supports the foreign key
- `IX_TRN_CreditNoteLines_InvoiceLineId` (InvoiceLineId) — supports the foreign key
- `IX_TRN_CreditNoteLines_TripId` (TripId) — supports the foreign key
- `IX_TRN_CreditNoteLines_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many trn.TRN_CreditNoteLines → one trn.TRN_CreditNotes via CreditNoteId (required)
- Many trn.TRN_CreditNoteLines → one trn.TRN_InvoiceLines via InvoiceLineId (required)
- Many trn.TRN_CreditNoteLines → one trn.TRN_Trips via TripId (optional)
- Not referenced by other tables

**Business rules:**
- A returned trip becomes available for invoicing again (its InvoiceId is cleared).


#### trn.TRN_EInvoiceSubmissions

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Submissions of transport invoices and credit notes to the national e-invoicing system (JoFotara, UBL 2.1) and the responses.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| DocumentType | VARCHAR(10) | NOT NULL |  |  | Document submitted. | CHECK IN ('Invoice','CreditNote'). |
| InvoiceId | BIGINT | NULL |  | FK → trn.TRN_Invoices | Invoice submitted. | Required when DocumentType = Invoice, else NULL (CHECK). |
| CreditNoteId | BIGINT | NULL |  | FK → trn.TRN_CreditNotes | Credit note submitted. | Required when DocumentType = CreditNote, else NULL (CHECK). |
| Status | VARCHAR(12) | NOT NULL | 'Queued' |  | Submission state. | CHECK IN ('Queued','Submitted','Rejected','NotRequired'). |
| Icv | BIGINT | NULL |  |  | Invoice counter value sent to the national system, taken from cfg.EInvoiceSettings.IcvCounter. | Unique within the company across every module. |
| UniqueId | UNIQUEIDENTIFIER | NULL |  |  | Unique identifier returned by the national e-invoicing system. | Returned by the national system |
| SystemNumber | VARCHAR(50) | NULL |  |  | Document number assigned by the national system. |  |
| QrCode | NVARCHAR(MAX) | NULL |  |  | QR code content returned by the national system; printed on the invoice. |  |
| RequestPayload | NVARCHAR(MAX) | NULL |  |  | UBL 2.1 document sent (JSON/XML text) — kept for audit. |  |
| ResponsePayload | NVARCHAR(MAX) | NULL |  |  | Full response received — kept for audit. |  |
| Attempts | INT | NOT NULL | 0 |  | Number of attempts made so far. | CHECK ≥ 0 |
| LastError | NVARCHAR(1000) | NULL |  |  | Last error message returned. |  |
| SubmittedAt | DATETIME2 | NULL |  |  | UTC time accepted by the national system. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_EInvoiceSubmissions (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_EInvoiceSubmissions_TRN_Invoices_InvoiceId`: InvoiceId → trn.TRN_Invoices(Id) — optional
- `FK_TRN_EInvoiceSubmissions_TRN_CreditNotes_CreditNoteId`: CreditNoteId → trn.TRN_CreditNotes(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_EInvoiceSubmissions_PublicId` (PublicId)
- `UX_TRN_EInvoiceSubmissions_CompanyId_Icv` (CompanyId, Icv) WHERE IsDeleted = 0 AND Icv IS NOT NULL

**Indexes:**
- `IX_TRN_EInvoiceSubmissions_Queue` (CompanyId, Status) WHERE Status IN ('Queued','Rejected')
- `IX_TRN_EInvoiceSubmissions_InvoiceId` (InvoiceId) — supports the foreign key
- `IX_TRN_EInvoiceSubmissions_CreditNoteId` (CreditNoteId) — supports the foreign key

**Relationships:**
- Many trn.TRN_EInvoiceSubmissions → one trn.TRN_Invoices via InvoiceId (optional)
- Many trn.TRN_EInvoiceSubmissions → one trn.TRN_CreditNotes via CreditNoteId (optional)
- Not referenced by other tables

**Business rules:**
- Queued submissions are sent by a background job with retries.
- A rejected invoice is corrected by a credit note and a new invoice.


#### trn.TRN_Receipts

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Money received from customers — against an invoice or on account.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| CustomerId | BIGINT | NOT NULL |  | FK → trn.TRN_Customers | Customer paying. |  |
| InvoiceId | BIGINT | NULL |  | FK → trn.TRN_Invoices | Invoice paid; NULL = on account. | Invoice must belong to the customer. |
| ReceiptDate | DATE | NOT NULL |  |  | Receipt date. | Not in a closed period. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Amount received. | CHECK > 0; ≤ invoice remaining when against an invoice. |
| PaymentMethod | VARCHAR(10) | NOT NULL |  |  | How the money was paid or received. | CHECK IN ('Cash','Bank','CliQ'). Cash / Bank / CliQ |
| CashBoxId | BIGINT | NULL |  | FK → acc.CashBoxes | Cash box the money came from or went to. | Required when PaymentMethod = Cash; must be NULL otherwise (CHECK). |
| BankAccountId | BIGINT | NULL |  | FK → acc.BankAccounts | Bank account the money came from or went to. | Required when PaymentMethod = Bank or CliQ; must be NULL otherwise (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| AttachmentsCount | INT | NOT NULL | 0 |  | Number of paper attachments, printed on the voucher. No files are stored. | CHECK ≥ 0 |
| Notes | NVARCHAR(500) | NULL |  |  | Free-text notes. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Status. | CHECK IN ('Active','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Receipts (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Receipts_TRN_Customers_CustomerId`: CustomerId → trn.TRN_Customers(Id) — required
- `FK_TRN_Receipts_TRN_Invoices_InvoiceId`: InvoiceId → trn.TRN_Invoices(Id) — optional
- `FK_TRN_Receipts_CashBoxes_CashBoxId`: CashBoxId → acc.CashBoxes(Id) — optional
- `FK_TRN_Receipts_BankAccounts_BankAccountId`: BankAccountId → acc.BankAccounts(Id) — optional
- `FK_TRN_Receipts_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_Receipts_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Receipts_PublicId` (PublicId)
- `UX_TRN_Receipts_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Receipts_CustomerId` (CustomerId) — supports the foreign key
- `IX_TRN_Receipts_InvoiceId` (InvoiceId) — supports the foreign key
- `IX_TRN_Receipts_CashBoxId` (CashBoxId) — supports the foreign key
- `IX_TRN_Receipts_BankAccountId` (BankAccountId) — supports the foreign key
- `IX_TRN_Receipts_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_Receipts_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_Receipts → one trn.TRN_Customers via CustomerId (required)
- Many trn.TRN_Receipts → one trn.TRN_Invoices via InvoiceId (optional)
- Many trn.TRN_Receipts → one acc.CashBoxes via CashBoxId (optional)
- Many trn.TRN_Receipts → one acc.BankAccounts via BankAccountId (optional)
- Many trn.TRN_Receipts → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_Receipts → one core.Users via VoidedBy (optional)
- Not referenced by other tables

**Business rules:**
- Posts Dr cash/bank, Cr trade receivables; voiding posts the mirror entry.
- Receipt voucher prints the amount in Arabic words (BR-024).


#### trn.TRN_Advances

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Cash advances paid to drivers, deducted at settlement.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| DriverId | BIGINT | NOT NULL |  | FK → trn.TRN_Drivers | Driver receiving the advance. |  |
| AdvanceDate | DATE | NOT NULL |  |  | Advance date. | Not in a closed period. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Amount advanced. | CHECK > 0. |
| PaymentMethod | VARCHAR(10) | NOT NULL |  |  | How the money was paid or received. | CHECK IN ('Cash','Bank','CliQ'). |
| CashBoxId | BIGINT | NULL |  | FK → acc.CashBoxes | Cash box the money came from or went to. | Required when PaymentMethod = Cash; must be NULL otherwise (CHECK). |
| BankAccountId | BIGINT | NULL |  | FK → acc.BankAccounts | Bank account the money came from or went to. | Required when PaymentMethod = Bank or CliQ; must be NULL otherwise (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| Notes | NVARCHAR(500) | NULL |  |  | Free-text notes. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Status. | CHECK IN ('Active','Void'). |
| SettlementId | BIGINT | NULL |  | FK → trn.TRN_Settlements | Settlement that deducted the advance. | BR-032: a settled advance cannot be edited or voided until its settlement is voided. |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Advances (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Advances_TRN_Drivers_DriverId`: DriverId → trn.TRN_Drivers(Id) — required
- `FK_TRN_Advances_CashBoxes_CashBoxId`: CashBoxId → acc.CashBoxes(Id) — optional
- `FK_TRN_Advances_BankAccounts_BankAccountId`: BankAccountId → acc.BankAccounts(Id) — optional
- `FK_TRN_Advances_TRN_Settlements_SettlementId`: SettlementId → trn.TRN_Settlements(Id) — optional
- `FK_TRN_Advances_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_Advances_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Advances_PublicId` (PublicId)
- `UX_TRN_Advances_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Advances_DriverId` (DriverId) — supports the foreign key
- `IX_TRN_Advances_CashBoxId` (CashBoxId) — supports the foreign key
- `IX_TRN_Advances_BankAccountId` (BankAccountId) — supports the foreign key
- `IX_TRN_Advances_SettlementId` (SettlementId) — supports the foreign key
- `IX_TRN_Advances_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_Advances_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_Advances → one trn.TRN_Drivers via DriverId (required)
- Many trn.TRN_Advances → one acc.CashBoxes via CashBoxId (optional)
- Many trn.TRN_Advances → one acc.BankAccounts via BankAccountId (optional)
- Many trn.TRN_Advances → one trn.TRN_Settlements via SettlementId (optional)
- Many trn.TRN_Advances → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_Advances → one core.Users via VoidedBy (optional)
- Not referenced by other tables

**Business rules:**
- Posts Dr driver advances, Cr cash/bank.
- Editing reverses and re-posts (BR-035).


#### trn.TRN_Settlements

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Driver settlements: gross wages of open trips − open advances − deductions = net paid.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| DriverId | BIGINT | NOT NULL |  | FK → trn.TRN_Drivers | Driver settled. |  |
| SettlementDate | DATE | NOT NULL |  |  | Settlement date. | Not in a closed period. |
| GrossWages | DECIMAL(18,3) | NOT NULL |  |  | Σ wages of the included trips. | CHECK ≥ 0 |
| AdvancesTotal | DECIMAL(18,3) | NOT NULL |  |  | Σ included advances. | CHECK ≥ 0 |
| DeductionsTotal | DECIMAL(18,3) | NOT NULL |  |  | Σ deduction lines. | CHECK ≥ 0 |
| NetPayable | DECIMAL(18,3) | NOT NULL |  |  | GrossWages − AdvancesTotal − DeductionsTotal. | CHECK NetPayable ≥ 0 and = Gross − Advances − Deductions. |
| PaymentMethod | VARCHAR(10) | NOT NULL |  |  | How the money was paid or received. | CHECK IN ('Cash','Bank','CliQ'). |
| CashBoxId | BIGINT | NULL |  | FK → acc.CashBoxes | Cash box the money came from or went to. | Required when PaymentMethod = Cash; must be NULL otherwise (CHECK). |
| BankAccountId | BIGINT | NULL |  | FK → acc.BankAccounts | Bank account the money came from or went to. | Required when PaymentMethod = Bank or CliQ; must be NULL otherwise (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Status. | CHECK IN ('Active','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_Settlements (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_Settlements_TRN_Drivers_DriverId`: DriverId → trn.TRN_Drivers(Id) — required
- `FK_TRN_Settlements_CashBoxes_CashBoxId`: CashBoxId → acc.CashBoxes(Id) — optional
- `FK_TRN_Settlements_BankAccounts_BankAccountId`: BankAccountId → acc.BankAccounts(Id) — optional
- `FK_TRN_Settlements_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_Settlements_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_Settlements_PublicId` (PublicId)
- `UX_TRN_Settlements_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_Settlements_DriverId` (DriverId) — supports the foreign key
- `IX_TRN_Settlements_CashBoxId` (CashBoxId) — supports the foreign key
- `IX_TRN_Settlements_BankAccountId` (BankAccountId) — supports the foreign key
- `IX_TRN_Settlements_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_Settlements_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_Settlements → one trn.TRN_Drivers via DriverId (required)
- Many trn.TRN_Settlements → one acc.CashBoxes via CashBoxId (optional)
- Many trn.TRN_Settlements → one acc.BankAccounts via BankAccountId (optional)
- Many trn.TRN_Settlements → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_Settlements → one core.Users via VoidedBy (optional)
- One trn.TRN_Settlements → many trn.TRN_Trips via SettlementId
- One trn.TRN_Settlements → many trn.TRN_Advances via SettlementId
- One trn.TRN_Settlements → many trn.TRN_SettlementDeductions via SettlementId

**Business rules:**
- BR-031: includes all or a chosen subset of the driver's open items; each item in only one active settlement.
- BR-031a: with wage accrual off, the settlement debits driver wage expense instead of driver payable.
- Voiding releases the trips and advances (their SettlementId is cleared) and posts the mirror entry.


#### trn.TRN_SettlementDeductions

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Deduction lines on a settlement (shortage, fines, other).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| SettlementId | BIGINT | NOT NULL |  | FK → trn.TRN_Settlements | Settlement. |  |
| DeductionType | VARCHAR(15) | NOT NULL |  |  | Kind of deduction. | CHECK IN ('Shortage','Fine','Other'). |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Deduction amount. | CHECK > 0. |
| Note | NVARCHAR(300) | NULL |  |  | Explanation. | Required when DeductionType = Other. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_SettlementDeductions (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_SettlementDeductions_TRN_Settlements_SettlementId`: SettlementId → trn.TRN_Settlements(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_SettlementDeductions_PublicId` (PublicId)

**Indexes:**
- `IX_TRN_SettlementDeductions_SettlementId` (SettlementId) — supports the foreign key
- `IX_TRN_SettlementDeductions_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many trn.TRN_SettlementDeductions → one trn.TRN_Settlements via SettlementId (required)
- Not referenced by other tables


#### trn.TRN_PartnerPayments

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Payments to partner hauliers against their balance due.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| PartnerId | BIGINT | NOT NULL |  | FK → trn.TRN_Partners | Partner paid. |  |
| PaymentDate | DATE | NOT NULL |  |  | Payment date. | Not in a closed period. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Amount paid. | CHECK > 0; ≤ partner's current balance due. |
| PaymentMethod | VARCHAR(10) | NOT NULL |  |  | How the money was paid or received. | CHECK IN ('Cash','Bank','CliQ'). |
| CashBoxId | BIGINT | NULL |  | FK → acc.CashBoxes | Cash box the money came from or went to. | Required when PaymentMethod = Cash; must be NULL otherwise (CHECK). |
| BankAccountId | BIGINT | NULL |  | FK → acc.BankAccounts | Bank account the money came from or went to. | Required when PaymentMethod = Bank or CliQ; must be NULL otherwise (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| Notes | NVARCHAR(500) | NULL |  |  | Free-text notes. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Status. | CHECK IN ('Active','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_PartnerPayments (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TRN_PartnerPayments_TRN_Partners_PartnerId`: PartnerId → trn.TRN_Partners(Id) — required
- `FK_TRN_PartnerPayments_CashBoxes_CashBoxId`: CashBoxId → acc.CashBoxes(Id) — optional
- `FK_TRN_PartnerPayments_BankAccounts_BankAccountId`: BankAccountId → acc.BankAccounts(Id) — optional
- `FK_TRN_PartnerPayments_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_TRN_PartnerPayments_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_PartnerPayments_PublicId` (PublicId)
- `UX_TRN_PartnerPayments_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_TRN_PartnerPayments_PartnerId` (PartnerId) — supports the foreign key
- `IX_TRN_PartnerPayments_CashBoxId` (CashBoxId) — supports the foreign key
- `IX_TRN_PartnerPayments_BankAccountId` (BankAccountId) — supports the foreign key
- `IX_TRN_PartnerPayments_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_TRN_PartnerPayments_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many trn.TRN_PartnerPayments → one trn.TRN_Partners via PartnerId (required)
- Many trn.TRN_PartnerPayments → one acc.CashBoxes via CashBoxId (optional)
- Many trn.TRN_PartnerPayments → one acc.BankAccounts via BankAccountId (optional)
- Many trn.TRN_PartnerPayments → one acc.JournalEntries via JournalEntryId (optional)
- Many trn.TRN_PartnerPayments → one core.Users via VoidedBy (optional)
- Not referenced by other tables

**Business rules:**
- Posts Dr partner payable, Cr cash/bank.


#### trn.TRN_InvoiceLayouts

**Owner:** Transportation — uses the TRN_ prefix · **Scope:** Company-scoped

**Purpose:** Print layouts per company for the claim statement and the tax invoice.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| LayoutType | VARCHAR(10) | NOT NULL |  |  | Which document the layout is for. | CHECK IN ('Claim','TaxInvoice'); one row per type per company. |
| Title | NVARCHAR(200) | NOT NULL |  |  | Printed title. |  |
| BandText | NVARCHAR(300) | NULL |  |  | Text printed in the header band. |  |
| Grouping | VARCHAR(20) | NOT NULL | 'LoadingSite' |  | How trips are grouped in the annex. | CHECK IN ('LoadingSite','Route','Material'). |
| AnnexColumns | NVARCHAR(MAX) | NOT NULL |  |  | Columns shown in the trip annex, in order (JSON array). | CHECK ISJSON = 1. |
| Labels | NVARCHAR(MAX) | NULL |  |  | Custom column/label texts (JSON). |  |
| FooterText | NVARCHAR(500) | NULL |  |  | Printed footer. |  |
| Signatures | NVARCHAR(MAX) | NULL |  |  | Signature boxes (JSON array of titles). |  |
| ShowLogo | BIT | NOT NULL | 1 |  | Print the company logo. |  |
| ShowQr | BIT | NOT NULL | 1 |  | Print the e-invoice QR code. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TRN_InvoiceLayouts (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TRN_InvoiceLayouts_PublicId` (PublicId)
- `UX_TRN_InvoiceLayouts_CompanyId_LayoutType` (CompanyId, LayoutType) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


### Schema `acc` — Shared — one accounting ledger per company for all projects

#### acc.Accounts

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Chart of accounts per company, as a tree. Only leaf (postable) accounts receive entry lines.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Code | VARCHAR(30) | NOT NULL |  |  | Account code. | Unique within the company. |
| NameAr | NVARCHAR(200) | NOT NULL |  |  | Account name in Arabic. | Required. |
| NameEn | NVARCHAR(200) | NULL |  |  | Account name in English. |  |
| AccountType | VARCHAR(10) | NOT NULL |  |  | Account class. | CHECK IN ('Asset','Liability','Equity','Revenue','Expense'); a child has its parent's type. |
| ParentId | BIGINT | NULL |  | FK → acc.Accounts | Parent account; NULL = top level. | No cycles (application rule). |
| IsPostable | BIT | NOT NULL | 1 |  | 1 = leaf account that can receive entries. | An account with children cannot be postable. |
| OpeningDebit | DECIMAL(18,3) | NOT NULL | 0 |  | Opening debit balance. | CHECK ≥ 0 |
| OpeningCredit | DECIMAL(18,3) | NOT NULL | 0 |  | Opening credit balance. | CHECK ≥ 0; not both > 0. |
| IsControlAccount | BIT | NOT NULL | 0 |  | 1 = control account; every line needs a party (customer, driver, partner, supplier). |  |
| ControlPartyType | VARCHAR(10) | NULL |  |  | Party kind required on lines of a control account. | CHECK IN ('Customer','Driver','Partner','Supplier'); required when IsControlAccount = 1. |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Accounts (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Accounts_Accounts_ParentId`: ParentId → acc.Accounts(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Accounts_PublicId` (PublicId)
- `UX_Accounts_CompanyId_Code` (CompanyId, Code) WHERE IsDeleted = 0

**Indexes:**
- `IX_Accounts_ParentId` (ParentId) — supports the foreign key

**Relationships:**
- Many acc.Accounts → one acc.Accounts via ParentId (optional) — self-reference
- One acc.Accounts → many acc.Accounts via ParentId
- One acc.Accounts → many acc.PostingKeyMappings via AccountId
- One acc.Accounts → many acc.CashBoxes via AccountId
- One acc.Accounts → many acc.BankAccounts via AccountId
- One acc.Accounts → many acc.EntryLines via AccountId
- One acc.Accounts → many acc.ApArParties via ControlAccountId
- One acc.Accounts → many acc.ApArDocumentLines via AccountId

**Business rules:**
- Cannot be deleted while referenced by entry lines, documents, payments or posting-key mappings (BR-040).
- Opening balances must balance once data migration is complete (FR-110).


#### acc.PostingKeyMappings

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Maps each posting key used by automatic entries (e.g. TRN.DriverPayable) to an account.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| PostingKey | VARCHAR(60) | NOT NULL |  |  | Namespaced posting key. | Unique within the company. |
| AccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Account the key posts to. | Must be postable. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_PostingKeyMappings (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_PostingKeyMappings_Accounts_AccountId`: AccountId → acc.Accounts(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_PostingKeyMappings_PublicId` (PublicId)
- `UX_PostingKeyMappings_CompanyId_PostingKey` (CompanyId, PostingKey) WHERE IsDeleted = 0

**Indexes:**
- `IX_PostingKeyMappings_AccountId` (AccountId) — supports the foreign key

**Relationships:**
- Many acc.PostingKeyMappings → one acc.Accounts via AccountId (required)
- Not referenced by other tables

**Business rules:**
- Invoicing and posting are disabled until all required keys are mapped (Section 8.6).


#### acc.CostCentres

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Cost centres used to analyse entry lines.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Code | VARCHAR(30) | NOT NULL |  |  | Cost-centre code. | Unique within the company. |
| Name | NVARCHAR(150) | NOT NULL |  |  | Name. |  |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). Archived records stay visible in history but cannot be chosen on new documents. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_CostCentres (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_CostCentres_PublicId` (PublicId)
- `UX_CostCentres_CompanyId_Code` (CompanyId, Code) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- One acc.CostCentres → many acc.EntryLines via CostCentreId
- One acc.CostCentres → many acc.ApArDocumentLines via CostCentreId


#### acc.CashBoxes

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Cash boxes; each is linked to its ledger account.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Cash box name. | Required. |
| AccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Ledger account of the cash box. | Postable asset account. |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). Archived records stay visible in history but cannot be chosen on new documents. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_CashBoxes (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_CashBoxes_Accounts_AccountId`: AccountId → acc.Accounts(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_CashBoxes_PublicId` (PublicId)

**Indexes:**
- `IX_CashBoxes_AccountId` (AccountId) — supports the foreign key
- `IX_CashBoxes_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many acc.CashBoxes → one acc.Accounts via AccountId (required)
- One acc.CashBoxes → many trn.TRN_Receipts via CashBoxId
- One acc.CashBoxes → many trn.TRN_Advances via CashBoxId
- One acc.CashBoxes → many trn.TRN_Settlements via CashBoxId
- One acc.CashBoxes → many trn.TRN_PartnerPayments via CashBoxId
- One acc.CashBoxes → many acc.ApArPayments via CashBoxId


#### acc.BankAccounts

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Company bank accounts; each is linked to its ledger account.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Name | NVARCHAR(100) | NOT NULL |  |  | Display name. |  |
| BankName | NVARCHAR(100) | NOT NULL |  |  | Name of the bank. |  |
| AccountNumber | VARCHAR(40) | NULL |  |  | Bank account number. |  |
| Iban | VARCHAR(34) | NULL |  |  | IBAN for bank payments. | Country format; launch market JO + 28 characters (30 total). Required when PaymentMethod = Bank. |
| CurrencyCode | CHAR(3) | NOT NULL | 'JOD' |  | Account currency. | ISO 4217. |
| AccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Ledger account of the bank account. | Postable asset account. |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). Archived records stay visible in history but cannot be chosen on new documents. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_BankAccounts (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_BankAccounts_Accounts_AccountId`: AccountId → acc.Accounts(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_BankAccounts_PublicId` (PublicId)

**Indexes:**
- `IX_BankAccounts_AccountId` (AccountId) — supports the foreign key
- `IX_BankAccounts_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many acc.BankAccounts → one acc.Accounts via AccountId (required)
- One acc.BankAccounts → many trn.TRN_Receipts via BankAccountId
- One acc.BankAccounts → many trn.TRN_Advances via BankAccountId
- One acc.BankAccounts → many trn.TRN_Settlements via BankAccountId
- One acc.BankAccounts → many trn.TRN_PartnerPayments via BankAccountId
- One acc.BankAccounts → many acc.ApArPayments via BankAccountId


#### acc.Periods

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Monthly accounting periods per company, open or closed.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| PeriodMonth | CHAR(7) | NOT NULL |  |  | Calendar month. | Format YYYY-MM; unique within the company. |
| Status | VARCHAR(10) | NOT NULL | 'Open' |  | Open or closed. | CHECK IN ('Open','Closed'). |
| ClosedBy | BIGINT | NULL |  | FK → core.Users | User who closed/reopened. | Required when Closed. |
| ClosedAt | DATETIME2 | NULL |  |  | When closed. | Required when Closed. |
| Reason | NVARCHAR(500) | NULL |  |  | Reason for closing or reopening. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_Periods (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_Periods_Users_ClosedBy`: ClosedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_Periods_PublicId` (PublicId)
- `UX_Periods_CompanyId_PeriodMonth` (CompanyId, PeriodMonth) WHERE IsDeleted = 0

**Indexes:**
- `IX_Periods_ClosedBy` (ClosedBy) — supports the foreign key

**Relationships:**
- Many acc.Periods → one core.Users via ClosedBy (optional)
- Not referenced by other tables

**Business rules:**
- BR-036: a closed period allows no posting, edit, void or import in any module.


#### acc.JournalEntries

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Journal entries — automatic (posted by documents in any module) and manual. Corrections are reversal entries; posted entries are never edited.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| EntryDate | DATE | NOT NULL |  |  | Entry date = source document date (BR-034). | Not in a closed period. |
| PeriodMonth | CHAR(7) | NOT NULL |  |  | Period of EntryDate (YYYY-MM). | Must match EntryDate. |
| Description | NVARCHAR(500) | NOT NULL |  |  | Entry narration. | Required. |
| SourceModule | VARCHAR(10) | NOT NULL |  |  | Module that owns the source document. | CHECK IN ('TRN','ACC') (extended as modules are added); ACC for manual, opening and AP/AR entries. |
| SourceType | VARCHAR(30) | NULL |  |  | Type of source document within the module. | TRN: Trip, Invoice, CreditNote, Receipt, Advance, Settlement, PartnerPayment · ACC: ApAr, Opening, Manual. |
| SourceId | BIGINT | NULL |  |  | Internal id of the source document (no FK — the source may be in a project table). | Required for automatic entries; validated by the application. |
| IsManual | BIT | NOT NULL | 0 |  | 1 = entered by a user; 0 = automatic. |  |
| Status | VARCHAR(10) | NOT NULL | 'Posted' |  | Entry status. | CHECK IN ('Posted','Reversed','Void'). |
| ReversesEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | Entry this one reverses. |  |
| ReversedByEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | Entry that reversed this one. | Set when Status = Reversed. |
| TotalDebit | DECIMAL(18,3) | NOT NULL |  |  | Σ line debits. | CHECK TotalDebit = TotalCredit (BR-033). |
| TotalCredit | DECIMAL(18,3) | NOT NULL |  |  | Σ line credits. |  |
| ExportBatchId | BIGINT | NULL |  | FK → acc.JournalExportBatches | Export batch that included the entry. |  |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_JournalEntries (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_JournalEntries_JournalEntries_ReversesEntryId`: ReversesEntryId → acc.JournalEntries(Id) — optional
- `FK_JournalEntries_JournalEntries_ReversedByEntryId`: ReversedByEntryId → acc.JournalEntries(Id) — optional
- `FK_JournalEntries_JournalExportBatches_ExportBatchId`: ExportBatchId → acc.JournalExportBatches(Id) — optional
- `FK_JournalEntries_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_JournalEntries_PublicId` (PublicId)
- `UX_JournalEntries_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_JournalEntries_Period` (CompanyId, PeriodMonth)
- `IX_JournalEntries_Source` (CompanyId, SourceModule, SourceType, SourceId)
- `IX_JournalEntries_ReversesEntryId` (ReversesEntryId) — supports the foreign key
- `IX_JournalEntries_ReversedByEntryId` (ReversedByEntryId) — supports the foreign key
- `IX_JournalEntries_ExportBatchId` (ExportBatchId) — supports the foreign key
- `IX_JournalEntries_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many acc.JournalEntries → one acc.JournalEntries via ReversesEntryId (optional) — self-reference
- Many acc.JournalEntries → one acc.JournalEntries via ReversedByEntryId (optional) — self-reference
- Many acc.JournalEntries → one acc.JournalExportBatches via ExportBatchId (optional)
- Many acc.JournalEntries → one core.Users via VoidedBy (optional)
- One acc.JournalEntries → many trn.TRN_Trips via JournalEntryId
- One acc.JournalEntries → many trn.TRN_Invoices via JournalEntryId
- One acc.JournalEntries → many trn.TRN_CreditNotes via JournalEntryId
- One acc.JournalEntries → many trn.TRN_Receipts via JournalEntryId
- One acc.JournalEntries → many trn.TRN_Advances via JournalEntryId
- One acc.JournalEntries → many trn.TRN_Settlements via JournalEntryId
- One acc.JournalEntries → many trn.TRN_PartnerPayments via JournalEntryId
- One acc.JournalEntries → many acc.JournalEntries via ReversesEntryId
- One acc.JournalEntries → many acc.JournalEntries via ReversedByEntryId
- One acc.JournalEntries → many acc.EntryLines via JournalEntryId
- One acc.JournalEntries → many acc.ApArDocuments via JournalEntryId
- One acc.JournalEntries → many acc.ApArPayments via JournalEntryId
- One acc.JournalEntries → many acc.ApArReturns via JournalEntryId

**Business rules:**
- BR-033: every entry balances; unbalanced entries are refused.
- Source documents link back through SourceModule + SourceType + SourceId (no FK).


#### acc.EntryLines

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Debit and credit lines of a journal entry.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| JournalEntryId | BIGINT | NOT NULL |  | FK → acc.JournalEntries | Entry. |  |
| LineNo | INT | NOT NULL |  |  | Line order within the entry. | Unique per entry. |
| AccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Account posted. | Must be postable and active. |
| Debit | DECIMAL(18,3) | NOT NULL | 0 |  | Debit amount. | CHECK ≥ 0; exactly one of Debit/Credit > 0. |
| Credit | DECIMAL(18,3) | NOT NULL | 0 |  | Credit amount. | CHECK ≥ 0 |
| CostCentreId | BIGINT | NULL |  | FK → acc.CostCentres | Cost centre. |  |
| PartyModule | VARCHAR(10) | NULL |  |  | Module that owns the party. | CHECK IN ('TRN','ACC'); required when PartyType is set. |
| PartyType | VARCHAR(15) | NULL |  |  | Kind of party for control-account lines. | TRN: Customer, Driver, Partner · ACC: ApArParty. |
| PartyId | BIGINT | NULL |  |  | Internal id of the party (no FK). | Required with PartyType. |
| Note | NVARCHAR(300) | NULL |  |  | Free-text note for this line. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_EntryLines (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_EntryLines_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — required
- `FK_EntryLines_Accounts_AccountId`: AccountId → acc.Accounts(Id) — required
- `FK_EntryLines_CostCentres_CostCentreId`: CostCentreId → acc.CostCentres(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_EntryLines_PublicId` (PublicId)

**Indexes:**
- `IX_EntryLines_Account` (AccountId) INCLUDE (Debit, Credit)
- `IX_EntryLines_Party` (CompanyId, PartyModule, PartyType, PartyId)
- `IX_EntryLines_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_EntryLines_CostCentreId` (CostCentreId) — supports the foreign key

**Relationships:**
- Many acc.EntryLines → one acc.JournalEntries via JournalEntryId (required)
- Many acc.EntryLines → one acc.Accounts via AccountId (required)
- Many acc.EntryLines → one acc.CostCentres via CostCentreId (optional)
- Not referenced by other tables

**Business rules:**
- Party (module + type + id) is required on control-account lines and must match the account's ControlPartyType.


#### acc.PendingPostings

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Postings that failed for a technical reason, waiting for automatic or manual retry.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| SourceModule | VARCHAR(10) | NOT NULL |  |  | Module that owns the document waiting to post. | CHECK IN ('TRN','ACC'). |
| SourceType | VARCHAR(30) | NOT NULL |  |  | Type of the document waiting to post. |  |
| SourceId | BIGINT | NOT NULL |  |  | Internal id of the document waiting to post (no FK). | One open row per document (UX filtered ResolvedAt IS NULL). |
| Reason | NVARCHAR(1000) | NOT NULL |  |  | Failure reason. |  |
| Attempts | INT | NOT NULL | 0 |  | Number of attempts made so far. | CHECK ≥ 0 |
| LastAttemptAt | DATETIME2 | NULL |  |  | Last retry time. |  |
| ResolvedAt | DATETIME2 | NULL |  |  | When posted successfully. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_PendingPostings (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_PendingPostings_PublicId` (PublicId)

**Indexes:**
- `IX_PendingPostings_Open` (CompanyId, ResolvedAt) WHERE ResolvedAt IS NULL
- `UX_PendingPostings_Source` (CompanyId, SourceModule, SourceType, SourceId) WHERE ResolvedAt IS NULL AND IsDeleted = 0 — UNIQUE

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### acc.JournalExportBatches

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Logged exports of journal entries (JSON or CSV) for external accounting tools.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| BatchNo | VARCHAR(30) | NOT NULL |  |  | Batch number. |  |
| PeriodFrom | CHAR(7) | NOT NULL |  |  | First period exported (YYYY-MM). |  |
| PeriodTo | CHAR(7) | NOT NULL |  |  | Last period (YYYY-MM). | ≥ PeriodFrom. |
| Format | VARCHAR(5) | NOT NULL |  |  | File format. | CHECK IN ('JSON','CSV'). |
| EntryCount | INT | NOT NULL |  |  | Entries exported. | CHECK ≥ 0 |
| TotalDebit | DECIMAL(18,3) | NOT NULL |  |  | Σ debits exported. | CHECK ≥ 0 |
| TotalCredit | DECIMAL(18,3) | NOT NULL |  |  | Σ credits exported. | Equals TotalDebit. |
| ExportedBy | BIGINT | NOT NULL |  | FK → core.Users | User. |  |
| ExportedAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | Time. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_JournalExportBatches (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_JournalExportBatches_Users_ExportedBy`: ExportedBy → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_JournalExportBatches_PublicId` (PublicId)

**Indexes:**
- `IX_JournalExportBatches_ExportedBy` (ExportedBy) — supports the foreign key
- `IX_JournalExportBatches_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many acc.JournalExportBatches → one core.Users via ExportedBy (required)
- One acc.JournalExportBatches → many acc.JournalEntries via ExportBatchId


#### acc.ApArParties

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Suppliers and customers kept by Accounting itself (non-transport payables and receivables).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| PartyRole | VARCHAR(10) | NOT NULL |  |  | Supplier or customer. | CHECK IN ('Supplier','Customer'). |
| Name | NVARCHAR(200) | NOT NULL |  |  | Display name. | Required; trimmed. |
| TaxNumber | VARCHAR(30) | NULL |  |  | Tax registration number. | Required on parties receiving a tax invoice when e-invoicing is enabled (application rule). |
| Phone | VARCHAR(20) | NULL |  |  | Phone number. | Market mobile format; launch market 07[789]XXXXXXX or 06XXXXXXX (configurable per market). |
| Address | NVARCHAR(300) | NULL |  |  | Postal / street address. |  |
| ControlAccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Payable or receivable control account. | Must be a control account. |
| PaymentTermsDays | INT | NOT NULL | 0 |  | Credit days. | CHECK ≥ 0 |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Record status. | CHECK IN ('Active','Archived'). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArParties (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArParties_Accounts_ControlAccountId`: ControlAccountId → acc.Accounts(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArParties_PublicId` (PublicId)

**Indexes:**
- `IX_ApArParties_ControlAccountId` (ControlAccountId) — supports the foreign key
- `IX_ApArParties_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many acc.ApArParties → one acc.Accounts via ControlAccountId (required)
- One acc.ApArParties → many trn.TRN_Customers via ApArPartyId
- One acc.ApArParties → many acc.ApArDocuments via PartyId


#### acc.ApArDocuments

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Supplier bills (payables) and receivable documents for non-transport income.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| DocumentKind | VARCHAR(15) | NOT NULL |  |  | Kind of document. | CHECK IN ('SupplierBill','Receivable'); numbered per kind. |
| PartyId | BIGINT | NOT NULL |  | FK → acc.ApArParties | Supplier or customer. | Party role must match the kind. |
| DocumentDate | DATE | NOT NULL |  |  | Document date. | Not in a closed period. |
| DueDate | DATE | NOT NULL |  |  | Date payment is due. | DueDate ≥ document date (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| Subtotal | DECIMAL(18,3) | NOT NULL |  |  | Sum of line amounts before discount, fees and tax. | CHECK ≥ 0 |
| Tax | DECIMAL(18,3) | NOT NULL | 0 |  | Sales tax amount. | CHECK ≥ 0; rounded half-up to the currency decimals (BR-021). |
| Total | DECIMAL(18,3) | NOT NULL |  |  | Total amount including tax. | CHECK ≥ 0 |
| PaidAmount | DECIMAL(18,3) | NOT NULL | 0 |  | Total of active payments/receipts against the document (maintained by the server). | CHECK ≥ 0 |
| ReturnedAmount | DECIMAL(18,3) | NOT NULL | 0 |  | Total of active returns/credit notes against the document (maintained by the server). | CHECK ≥ 0 |
| RemainingAmount | DECIMAL(18,3) | NOT NULL |  |  | Total − PaidAmount − ReturnedAmount (maintained by the server). | CHECK ≥ 0. |
| Status | VARCHAR(10) | NOT NULL | 'Posted' |  | Status. | CHECK IN ('Posted','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArDocuments (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArDocuments_ApArParties_PartyId`: PartyId → acc.ApArParties(Id) — required
- `FK_ApArDocuments_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_ApArDocuments_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArDocuments_PublicId` (PublicId)
- `UX_ApArDocuments_CompanyId_DocumentKind_Number` (CompanyId, DocumentKind, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_ApArDocuments_PartyId` (PartyId) — supports the foreign key
- `IX_ApArDocuments_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_ApArDocuments_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many acc.ApArDocuments → one acc.ApArParties via PartyId (required)
- Many acc.ApArDocuments → one acc.JournalEntries via JournalEntryId (optional)
- Many acc.ApArDocuments → one core.Users via VoidedBy (optional)
- One acc.ApArDocuments → many acc.ApArDocumentLines via DocumentId
- One acc.ApArDocuments → many acc.ApArPayments via DocumentId
- One acc.ApArDocuments → many acc.ApArReturns via DocumentId
- One acc.ApArDocuments → many acc.ApArEInvoiceSubmissions via ApArDocumentId

**Business rules:**
- Supplier bill: Dr expense/asset + sales tax input, Cr supplier payable (FR-123).
- Receivable: Dr customer receivable, Cr revenue + sales tax output (FR-124); queued for e-invoicing when enabled.


#### acc.ApArDocumentLines

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Lines of AP/AR documents.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| DocumentId | BIGINT | NOT NULL |  | FK → acc.ApArDocuments | Document. |  |
| Description | NVARCHAR(300) | NOT NULL |  |  | Line text. | Required. |
| AccountId | BIGINT | NOT NULL |  | FK → acc.Accounts | Expense, asset or revenue account. | Postable. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Line amount before tax. | CHECK > 0. |
| TaxRate | DECIMAL(5,2) | NOT NULL |  |  | Tax rate %. | CHECK BETWEEN 0 AND 100. |
| CostCentreId | BIGINT | NULL |  | FK → acc.CostCentres | Cost centre. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArDocumentLines (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArDocumentLines_ApArDocuments_DocumentId`: DocumentId → acc.ApArDocuments(Id) — required
- `FK_ApArDocumentLines_Accounts_AccountId`: AccountId → acc.Accounts(Id) — required
- `FK_ApArDocumentLines_CostCentres_CostCentreId`: CostCentreId → acc.CostCentres(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArDocumentLines_PublicId` (PublicId)

**Indexes:**
- `IX_ApArDocumentLines_DocumentId` (DocumentId) — supports the foreign key
- `IX_ApArDocumentLines_AccountId` (AccountId) — supports the foreign key
- `IX_ApArDocumentLines_CostCentreId` (CostCentreId) — supports the foreign key
- `IX_ApArDocumentLines_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many acc.ApArDocumentLines → one acc.ApArDocuments via DocumentId (required)
- Many acc.ApArDocumentLines → one acc.Accounts via AccountId (required)
- Many acc.ApArDocumentLines → one acc.CostCentres via CostCentreId (optional)
- Not referenced by other tables


#### acc.ApArPayments

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Payments to suppliers and collections from customers against AP/AR documents.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| DocumentId | BIGINT | NOT NULL |  | FK → acc.ApArDocuments | Document paid. |  |
| PaymentDate | DATE | NOT NULL |  |  | Date. | Not in a closed period. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Amount. | CHECK > 0; ≤ document remaining. |
| PaymentMethod | VARCHAR(10) | NOT NULL |  |  | How the money was paid or received. | CHECK IN ('Cash','Bank','CliQ'). |
| CashBoxId | BIGINT | NULL |  | FK → acc.CashBoxes | Cash box the money came from or went to. | Required when PaymentMethod = Cash; must be NULL otherwise (CHECK). |
| BankAccountId | BIGINT | NULL |  | FK → acc.BankAccounts | Bank account the money came from or went to. | Required when PaymentMethod = Bank or CliQ; must be NULL otherwise (CHECK). |
| Reference | NVARCHAR(100) | NULL |  |  | External reference, e.g. bank transfer or cheque number. |  |
| AttachmentsCount | INT | NOT NULL | 0 |  | Number of paper attachments, printed on the voucher. No files are stored. | CHECK ≥ 0 |
| Status | VARCHAR(10) | NOT NULL | 'Active' |  | Status. | CHECK IN ('Active','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArPayments (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArPayments_ApArDocuments_DocumentId`: DocumentId → acc.ApArDocuments(Id) — required
- `FK_ApArPayments_CashBoxes_CashBoxId`: CashBoxId → acc.CashBoxes(Id) — optional
- `FK_ApArPayments_BankAccounts_BankAccountId`: BankAccountId → acc.BankAccounts(Id) — optional
- `FK_ApArPayments_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_ApArPayments_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArPayments_PublicId` (PublicId)
- `UX_ApArPayments_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_ApArPayments_DocumentId` (DocumentId) — supports the foreign key
- `IX_ApArPayments_CashBoxId` (CashBoxId) — supports the foreign key
- `IX_ApArPayments_BankAccountId` (BankAccountId) — supports the foreign key
- `IX_ApArPayments_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_ApArPayments_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many acc.ApArPayments → one acc.ApArDocuments via DocumentId (required)
- Many acc.ApArPayments → one acc.CashBoxes via CashBoxId (optional)
- Many acc.ApArPayments → one acc.BankAccounts via BankAccountId (optional)
- Many acc.ApArPayments → one acc.JournalEntries via JournalEntryId (optional)
- Many acc.ApArPayments → one core.Users via VoidedBy (optional)
- Not referenced by other tables


#### acc.ApArReturns

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Returns against AP/AR documents (payable returns and receivable returns).

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| Number | VARCHAR(30) | NOT NULL |  |  | Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued. | Gap-free and unique within the company; never reused, kept when the document is voided (BR-037, BR-038). |
| DocumentId | BIGINT | NOT NULL |  | FK → acc.ApArDocuments | Document returned against. |  |
| ReturnDate | DATE | NOT NULL |  |  | Date. | Not in a closed period. |
| Amount | DECIMAL(18,3) | NOT NULL |  |  | Amount before tax. | CHECK > 0. |
| Tax | DECIMAL(18,3) | NOT NULL | 0 |  | Sales tax amount. | CHECK ≥ 0; rounded half-up to the currency decimals (BR-021). |
| Total | DECIMAL(18,3) | NOT NULL |  |  | Amount + Tax. | ≤ document remaining. |
| Reason | NVARCHAR(500) | NOT NULL |  |  | Reason text. | Required. |
| Status | VARCHAR(10) | NOT NULL | 'Posted' |  | Status. | CHECK IN ('Posted','Void'). |
| JournalEntryId | BIGINT | NULL |  | FK → acc.JournalEntries | The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry. | Set only by the posting service; NULL while unposted or waiting in acc.PendingPostings. |
| VoidReason | NVARCHAR(500) | NULL |  |  | Why the record was voided. | Required when Status = Void (CHECK). |
| VoidedBy | BIGINT | NULL |  | FK → core.Users | User who voided the record. | Required when Status = Void (CHECK). |
| VoidedAt | DATETIME2 | NULL |  |  | UTC time the record was voided. | Required when Status = Void (CHECK). |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArReturns (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArReturns_ApArDocuments_DocumentId`: DocumentId → acc.ApArDocuments(Id) — required
- `FK_ApArReturns_JournalEntries_JournalEntryId`: JournalEntryId → acc.JournalEntries(Id) — optional
- `FK_ApArReturns_Users_VoidedBy`: VoidedBy → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArReturns_PublicId` (PublicId)
- `UX_ApArReturns_CompanyId_Number` (CompanyId, Number) WHERE IsDeleted = 0

**Indexes:**
- `IX_ApArReturns_DocumentId` (DocumentId) — supports the foreign key
- `IX_ApArReturns_JournalEntryId` (JournalEntryId) — supports the foreign key
- `IX_ApArReturns_VoidedBy` (VoidedBy) — supports the foreign key

**Relationships:**
- Many acc.ApArReturns → one acc.ApArDocuments via DocumentId (required)
- Many acc.ApArReturns → one acc.JournalEntries via JournalEntryId (optional)
- Many acc.ApArReturns → one core.Users via VoidedBy (optional)
- One acc.ApArReturns → many acc.ApArEInvoiceSubmissions via ApArReturnId


#### acc.ApArEInvoiceSubmissions

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Submissions of Accounting receivable documents and their returns to the national e-invoicing system.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| DocumentType | VARCHAR(10) | NOT NULL |  |  | Document submitted. | CHECK IN ('Invoice','CreditNote'). |
| ApArDocumentId | BIGINT | NULL |  | FK → acc.ApArDocuments | Receivable document (DocumentType = Invoice). | Required for Invoice, else NULL; must be a Receivable. |
| ApArReturnId | BIGINT | NULL |  | FK → acc.ApArReturns | Receivable return (DocumentType = CreditNote). | Required for CreditNote, else NULL. |
| Status | VARCHAR(12) | NOT NULL | 'Queued' |  | Submission state. | CHECK IN ('Queued','Submitted','Rejected','NotRequired'). |
| Icv | BIGINT | NULL |  |  | Invoice counter value sent to the national system, taken from cfg.EInvoiceSettings.IcvCounter. | Unique within the company across every module. From the company-wide counter in cfg.EInvoiceSettings |
| UniqueId | UNIQUEIDENTIFIER | NULL |  |  | Unique identifier returned by the national e-invoicing system. | Returned by the national system |
| SystemNumber | VARCHAR(50) | NULL |  |  | Document number assigned by the national system. |  |
| QrCode | NVARCHAR(MAX) | NULL |  |  | QR code content returned by the national system; printed on the invoice. |  |
| RequestPayload | NVARCHAR(MAX) | NULL |  |  | UBL 2.1 document sent (JSON/XML text) — kept for audit. |  |
| ResponsePayload | NVARCHAR(MAX) | NULL |  |  | Full response received — kept for audit. |  |
| Attempts | INT | NOT NULL | 0 |  | Number of attempts made so far. | CHECK ≥ 0 |
| LastError | NVARCHAR(1000) | NULL |  |  | Last error message returned. |  |
| SubmittedAt | DATETIME2 | NULL |  |  | UTC time accepted by the national system. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ApArEInvoiceSubmissions (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ApArEInvoiceSubmissions_ApArDocuments_ApArDocumentId`: ApArDocumentId → acc.ApArDocuments(Id) — optional
- `FK_ApArEInvoiceSubmissions_ApArReturns_ApArReturnId`: ApArReturnId → acc.ApArReturns(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ApArEInvoiceSubmissions_PublicId` (PublicId)
- `UX_ApArEInvoiceSubmissions_CompanyId_Icv` (CompanyId, Icv) WHERE IsDeleted = 0 AND Icv IS NOT NULL

**Indexes:**
- `IX_ApArEInvoiceSubmissions_Queue` (CompanyId, Status) WHERE Status IN ('Queued','Rejected')
- `IX_ApArEInvoiceSubmissions_ApArDocumentId` (ApArDocumentId) — supports the foreign key
- `IX_ApArEInvoiceSubmissions_ApArReturnId` (ApArReturnId) — supports the foreign key

**Relationships:**
- Many acc.ApArEInvoiceSubmissions → one acc.ApArDocuments via ApArDocumentId (optional)
- Many acc.ApArEInvoiceSubmissions → one acc.ApArReturns via ApArReturnId (optional)
- Not referenced by other tables


### Schema `cfg` — Shared — settings, numbering, custom fields, views, reports, locks, imports

#### cfg.ImportBatches

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Log of every import (trips, master data, chart of accounts, entries) with row counts.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| EntityType | VARCHAR(40) | NOT NULL |  |  | Namespaced entity imported. |  |
| FileName | NVARCHAR(260) | NOT NULL |  |  | Uploaded file name. |  |
| Mode | VARCHAR(10) | NOT NULL | 'Strict' |  | Strict = reject any bad row; Tolerant = apply defaults and flag Needs review. | CHECK IN ('Strict','Tolerant'). |
| TotalRows | INT | NOT NULL |  |  | Rows in the file. | CHECK ≥ 0 |
| ImportedRows | INT | NOT NULL |  |  | Rows imported. | CHECK ≥ 0 |
| RejectedRows | INT | NOT NULL |  |  | Rows rejected. | Imported + Rejected ≤ Total. |
| ImportedBy | BIGINT | NOT NULL |  | FK → core.Users | User. |  |
| ImportedAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | Time. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_ImportBatches (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_ImportBatches_Users_ImportedBy`: ImportedBy → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_ImportBatches_PublicId` (PublicId)

**Indexes:**
- `IX_ImportBatches_ImportedBy` (ImportedBy) — supports the foreign key
- `IX_ImportBatches_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many cfg.ImportBatches → one core.Users via ImportedBy (required)
- One cfg.ImportBatches → many trn.TRN_Trips via ImportBatchId

**Business rules:**
- Imports are refused for dates in a closed period.


#### cfg.CompanySettings

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Key/value settings per company for every module; keys carry the module prefix.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| SettingKey | VARCHAR(80) | NOT NULL |  |  | Namespaced key. | Unique within the company. |
| SettingValue | NVARCHAR(1000) | NOT NULL |  |  | Value as text (parsed by the module). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_CompanySettings (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_CompanySettings_PublicId` (PublicId)
- `UX_CompanySettings_CompanyId_SettingKey` (CompanyId, SettingKey) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables

**Business rules:**
- Transport defaults: TRN.BillOnUnloadedWeight = true; TRN.PayDriverOnLoadedWeight = false; TRN.ShortageTolerancePct = 0.5; TRN.LockTariffs = false; TRN.InvoiceApproval = true; TRN.AccrueDriverWage = true.
- Every change is audited.


#### cfg.NumberSequences

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Gap-free number sequences per document type and company, incremented only by the server.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| DocumentType | VARCHAR(40) | NOT NULL |  |  | Namespaced document type. | Unique within the company. |
| Prefix | VARCHAR(10) | NOT NULL |  |  | Number prefix, e.g. INV-. |  |
| Digits | TINYINT | NOT NULL | 5 |  | Zero-padded width. | CHECK BETWEEN 3 AND 10. |
| NextNumber | BIGINT | NOT NULL | 1 |  | Next number to issue. | BR-038a: can never be set at or below an issued number. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_NumberSequences (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_NumberSequences_PublicId` (PublicId)
- `UX_NumberSequences_CompanyId_DocumentType` (CompanyId, DocumentType) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables

**Business rules:**
- Incremented inside the issuing transaction with an update lock so numbers stay unique and gap-free under concurrency (BR-038).


#### cfg.EInvoiceSettings

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** National e-invoicing connection for each company, shared by every module that issues tax invoices.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| IsEnabled | BIT | NOT NULL | 0 |  | E-invoicing switched on. |  |
| Connector | VARCHAR(30) | NOT NULL | 'JoFotara' |  | Connector used. |  |
| SellerTaxNumber | VARCHAR(30) | NULL |  |  | Seller tax number registered with the national system. | Required when enabled. |
| RegistrationType | VARCHAR(30) | NULL |  |  | Taxpayer registration type. |  |
| IncomeSourceSequence | VARCHAR(30) | NULL |  |  | Income source sequence issued by the tax authority. | Required when enabled. |
| ClientIdEncrypted | VARBINARY(512) | NULL |  |  | Encrypted API client id. | Never returned to the browser. |
| SecretKeyEncrypted | VARBINARY(512) | NULL |  |  | Encrypted API secret. | Never returned to the browser. |
| Environment | VARCHAR(10) | NOT NULL | 'Test' |  | Target environment. | CHECK IN ('Test','Production'). |
| IcvCounter | BIGINT | NOT NULL | 0 |  | Last invoice counter value issued for the company. | Incremented under lock; one counter for all modules. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_EInvoiceSettings (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_EInvoiceSettings_PublicId` (PublicId)
- `UX_EInvoiceSettings_CompanyId` (CompanyId) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### cfg.CustomFieldDefinitions

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Admin-defined extra fields for module entities (launch: trips, drivers, customers). Values live in each record's CustomValues JSON.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| EntityType | VARCHAR(40) | NOT NULL |  |  | Namespaced entity. |  |
| FieldKey | VARCHAR(50) | NOT NULL |  |  | Key used in CustomValues JSON. | Unique per entity; letters, digits, underscore. |
| LabelAr | NVARCHAR(100) | NOT NULL |  |  | Label in Arabic. | Required. |
| LabelEn | NVARCHAR(100) | NULL |  |  | Label in English. |  |
| FieldType | VARCHAR(10) | NOT NULL |  |  | Field type. | CHECK IN ('Text','Number','Date','List','Checkbox','LongText'). |
| Options | NVARCHAR(MAX) | NULL |  |  | Choices for List fields (JSON array). | Required when FieldType = List. |
| DefaultValue | NVARCHAR(200) | NULL |  |  | Default value. |  |
| HelpText | NVARCHAR(300) | NULL |  |  | Help shown under the field. |  |
| IsRequired | BIT | NOT NULL | 0 |  | Value required on save. |  |
| ShowAsColumn | BIT | NOT NULL | 0 |  | Show as a list column. |  |
| SortOrder | INT | NOT NULL | 0 |  | Display order (ascending). |  |
| IsHidden | BIT | NOT NULL | 0 |  | Hidden without deleting. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_CustomFieldDefinitions (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_CustomFieldDefinitions_PublicId` (PublicId)
- `UX_CustomFieldDefinitions_CompanyId_EntityType_FieldKey` (CompanyId, EntityType, FieldKey) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### cfg.StandardFieldConfigs

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Per-company overrides of built-in fields: rename, hide, require, reorder.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| EntityType | VARCHAR(40) | NOT NULL |  |  | Namespaced entity. |  |
| FieldKey | VARCHAR(50) | NOT NULL |  |  | Built-in field key. |  |
| Label | NVARCHAR(100) | NULL |  |  | Replacement label; NULL = default. |  |
| IsHidden | BIT | NOT NULL | 0 |  | Hide the field. | Fields required by business rules cannot be hidden. |
| IsRequired | BIT | NOT NULL | 0 |  | Make required. |  |
| SortOrder | INT | NOT NULL | 0 |  | Display order (ascending). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_StandardFieldConfigs (Id)`

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_StandardFieldConfigs_PublicId` (PublicId)
- `UX_StandardFieldConfigs_CompanyId_EntityType_FieldKey` (CompanyId, EntityType, FieldKey) WHERE IsDeleted = 0

**Relationships:**
- References: none (besides standard keys)
- Not referenced by other tables


#### cfg.TableColumnViews

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Saved column layouts for list screens: company default (UserId NULL) or per user.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| UserId | BIGINT | NULL |  | FK → core.Users | Owner; NULL = company default. |  |
| ViewKey | VARCHAR(80) | NOT NULL |  |  | Namespaced list key. |  |
| Columns | NVARCHAR(MAX) | NOT NULL |  |  | Visible columns, order and widths (JSON). | CHECK ISJSON = 1. |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_TableColumnViews (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_TableColumnViews_Users_UserId`: UserId → core.Users(Id) — optional

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_TableColumnViews_PublicId` (PublicId)

**Indexes:**
- `UX_TableColumnViews_Key` (CompanyId, ViewKey, UserId) WHERE IsDeleted = 0 — UNIQUE
- `IX_TableColumnViews_UserId` (UserId) — supports the foreign key

**Relationships:**
- Many cfg.TableColumnViews → one core.Users via UserId (optional)
- Not referenced by other tables

**Business rules:**
- One layout per company + view + user (or company default).


#### cfg.SavedReports

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Report-builder definitions, private or shared within the company.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| OwnerUserId | BIGINT | NOT NULL |  | FK → core.Users | Report owner. |  |
| Name | NVARCHAR(150) | NOT NULL |  |  | Report name. |  |
| Source | VARCHAR(60) | NOT NULL |  |  | Namespaced data source. |  |
| Definition | NVARCHAR(MAX) | NOT NULL |  |  | Columns, filters, grouping and sorting (JSON). | CHECK ISJSON = 1. |
| IsShared | BIT | NOT NULL | 0 |  | Visible to other users with access to the source. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_SavedReports (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_SavedReports_Users_OwnerUserId`: OwnerUserId → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_SavedReports_PublicId` (PublicId)

**Indexes:**
- `IX_SavedReports_OwnerUserId` (OwnerUserId) — supports the foreign key
- `IX_SavedReports_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many cfg.SavedReports → one core.Users via OwnerUserId (required)
- One cfg.SavedReports → many cfg.DashboardPins via SavedReportId

**Business rules:**
- Results always respect the viewer's permissions.


#### cfg.DashboardPins

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Saved reports pinned to a user's dashboard, with warning and critical thresholds.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Dashboard owner. |  |
| SavedReportId | BIGINT | NOT NULL |  | FK → cfg.SavedReports | Pinned report. |  |
| Measure | VARCHAR(60) | NOT NULL |  |  | Measure shown on the card. |  |
| WarningThreshold | DECIMAL(18,3) | NULL |  |  | Value at which the card turns to warning. |  |
| CriticalThreshold | DECIMAL(18,3) | NULL |  |  | Value at which the card shows exceeded. |  |
| SortOrder | INT | NOT NULL | 0 |  | Display order (ascending). |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_DashboardPins (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_DashboardPins_Users_UserId`: UserId → core.Users(Id) — required
- `FK_DashboardPins_SavedReports_SavedReportId`: SavedReportId → cfg.SavedReports(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_DashboardPins_PublicId` (PublicId)

**Indexes:**
- `IX_DashboardPins_UserId` (UserId) — supports the foreign key
- `IX_DashboardPins_SavedReportId` (SavedReportId) — supports the foreign key
- `IX_DashboardPins_CompanyId` (CompanyId) WHERE IsDeleted = 0 — company filter

**Relationships:**
- Many cfg.DashboardPins → one core.Users via UserId (required)
- Many cfg.DashboardPins → one cfg.SavedReports via SavedReportId (required)
- Not referenced by other tables


#### cfg.RecordLocks

**Owner:** Shared across all projects — no prefix · **Scope:** Company-scoped

**Purpose:** Edit locks: opening a record for editing locks it for other users.

| Column | Type | Null | Default | Key | Description | Rules / validation |
|---|---|---|---|---|---|---|
| Id *(std)* | BIGINT IDENTITY(1,1) | NOT NULL |  | PK | Internal surrogate key. Never shown in URLs, screens or exports. |  |
| PublicId *(std)* | UNIQUEIDENTIFIER | NOT NULL | NEWID() | UQ | Random public identifier; hashed into every URL instead of Id (SEC-22). | Unique; never changes. |
| TenantId *(std)* | BIGINT | NOT NULL |  | FK → core.Tenants | Owning tenant — tenant isolation filter on every query. | Set by the server from the signed-in context; never from user input. |
| CompanyId *(std)* | BIGINT | NOT NULL |  | FK → core.Companies | Owning company — company isolation filter on every query. | Must belong to TenantId; set by the server. |
| EntityType | VARCHAR(60) | NOT NULL |  |  | Namespaced entity locked. |  |
| EntityId | BIGINT | NOT NULL |  |  | Locked row id. |  |
| UserId | BIGINT | NOT NULL |  | FK → core.Users | Lock holder. |  |
| AcquiredAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | When locked. |  |
| LastActivityAt | DATETIME2 | NOT NULL | SYSUTCDATETIME() |  | Last activity; lock expires 90 s after it. |  |
| CreatedAt *(std)* | DATETIME2(3) | NOT NULL | SYSUTCDATETIME() |  | UTC time the row was created. |  |
| CreatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who created the row; NULL only for rows created by a system job. |  |
| UpdatedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the last update. |  |
| UpdatedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who last updated the row. |  |
| IsDeleted *(std)* | BIT | NOT NULL | 0 |  | Soft-delete flag. 1 = deleted; hidden by the global query filter everywhere. | No hard deletes (BR-041). |
| DeletedAt *(std)* | DATETIME2(3) | NULL |  |  | UTC time of the soft delete. | Required when IsDeleted = 1 (CHECK). |
| DeletedBy *(std)* | BIGINT | NULL |  | FK → core.Users | User who soft-deleted the row. | Required when IsDeleted = 1 (CHECK). |
| DeleteReason *(std)* | NVARCHAR(500) | NULL |  |  | Why the row was deleted. | Required when IsDeleted = 1 (CHECK). |
| RowVersion *(std)* | ROWVERSION | NOT NULL | (automatic) |  | Concurrency token; a stale update is refused. |  |

**Primary key:** `PK_RecordLocks (Id)`

**Foreign keys** (all `ON DELETE NO ACTION`):
- `FK_RecordLocks_Users_UserId`: UserId → core.Users(Id) — required

Standard foreign keys: TenantId → core.Tenants, CompanyId → core.Companies, CreatedBy → core.Users, UpdatedBy → core.Users, DeletedBy → core.Users

**Unique constraints:**
- `UX_RecordLocks_PublicId` (PublicId)
- `UX_RecordLocks_CompanyId_EntityType_EntityId` (CompanyId, EntityType, EntityId) WHERE IsDeleted = 0

**Indexes:**
- `IX_RecordLocks_UserId` (UserId) — supports the foreign key

**Relationships:**
- Many cfg.RecordLocks → one core.Users via UserId (required)
- Not referenced by other tables

**Business rules:**
- BR-039: released on close (soft-deleted) or after 90 s without activity.
- Unique among active (not deleted) locks, so only one user holds a record.

## 6. Changes in revision 4
- Added `VoidReason`, `VoidedBy`, `VoidedAt` to every voidable document that lacked them: TRN_CreditNotes (VoidedBy/VoidedAt), TRN_Receipts, TRN_Advances, TRN_Settlements, TRN_PartnerPayments, JournalEntries, ApArDocuments, ApArPayments, ApArReturns — the PRD requires a reason for every void.
- `ApArPayments.Status` and `ApArReturns.Status` now have defined values (Active/Void, Posted/Void) and their numbers are unique per company.
- `TRN_Tariffs`: unique rule on party type + party + material + loading site + unloading site + ValidFrom, so two identical lines can never tie in price resolution.
- `core.Users`: added `EmailConfirmedAt` — the PRD requires email verification at sign-up (flow 9.1) and Identity needs a place to record it.
- Every column now has a description, default, and validation rules; named primary keys, foreign keys, unique constraints and supporting indexes are listed per table.

## 7. Design decisions to be aware of
- `core.Users.Email` is unique platform-wide: one email = one account in one tenant (confirmed by the owner, 8 Oct 2026). A person working for two tenants uses two emails; inside one tenant a user can have a role in several companies.
- A trip is on at most one active invoice and one active settlement — enforced through `TRN_Trips.InvoiceId` and `TRN_Trips.SettlementId`, not by unique indexes on line tables, so voided documents keep their history.
- Transient rows (record locks, sessions, recovery codes) also follow soft delete; release/revoke = soft delete. Filtered unique indexes keep them correct.