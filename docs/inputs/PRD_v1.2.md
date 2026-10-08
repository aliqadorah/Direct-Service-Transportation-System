# Direct Service Transportation — Product Requirements Document

## Section 1 — Introduction & Product Overview

### 1.1 Document Control
| Item | Value |
|---|---|
| Product name | Direct Service Transportation |
| Document | Product Requirements Document (PRD) |
| Version | 1.2 |
| Date | 4 October 2026 |
| Status | Approved for build |
| Product owner | Direct Service Company |

### 1.2 Purpose of This Document
This PRD defines what the product must do, for whom, and under what rules. It is the single reference for product, design, engineering, QA and implementation teams. Any change to scope or rules is made through a new, versioned revision of this document.

### 1.3 Product Summary
Direct Service Transportation is a cloud-based SaaS operations-and-accounting platform for bulk-haulage transport companies — companies that move bulk materials such as minerals, fertiliser, aggregates and grain by truck. It covers the full business cycle:
1. Record each shipment (trip) from a loading site to an unloading site.
2. Price the trip for the customer, and for the driver or partner haulier.
3. Invoice customers, handle returns (credit notes) and collect payments.
4. Pay drivers through advances and settlements, and pay partner hauliers.
5. Keep a complete double-entry general ledger with financial statements.

Every financial event automatically produces a balanced journal entry, so operations and accounting stay reconciled at all times.

### 1.4 Problem Statement
Bulk-haulage companies typically run on spreadsheets and disconnected tools, which leads to recurring, costly failures:
- Revenue leakage: prices are entered below the agreed customer tariff, and nobody notices.
- Wage disputes: driver rates are recalculated retroactively when a rate list changes.
- Broken driver balances: advances get edited or deleted, so balances can no longer be traced.
- Tax-audit exposure: invoice numbers have gaps or duplicates, and tax is calculated by hand.
- Data loss and overwrites: several staff edit the same shared files, with no audit trail.
- No reconciliation: operations and accounting live in separate places; month-end closing is manual.
- Late visibility: weight shortages, unbilled trips, overdue receivables and expiring driver documents are discovered too late.

### 1.5 Solution & Value Proposition
- One source of truth: trips, invoices, receipts, driver settlements, partner payments and the ledger in one platform.
- Rules the platform enforces, so users cannot bypass them: no unbalanced journal entry; no activity in a closed period; gap-free invoice numbering; corrections by reversal, never by deletion; no deletion of anything referenced by another record; driver wage rates frozen at the moment a trip is saved.
- Fast, accurate data entry: keyboard-driven quick-entry row and bulk import with column matching and duplicate detection.
- Automatic pricing: customer and partner tariffs (most specific rule wins, validity dates); monthly driver rates per loading site, separately for own fleet and external trucks; tariff breaches flagged and reported.
- Tax-ready documents: claim statements with a per-shipment annex; tax invoices with QR code; receipt, payment and journal vouchers with the amount in words; national e-invoicing submission.
- Real-time teamwork: many users at the same time with record locking and no overwrites.
- Built-in full accounting: chart of accounts, journal, general ledger, trial balance, P&L, balance sheet, payables and receivables, statements.
- Configurable fields and layouts: tenant administrators add custom fields, rename/hide/require fields, design invoice layouts, build and save reports.

### 1.6 Delivery Model — SaaS
- Access and hosting: multi-tenant cloud service accessed through a web browser, no installation. The provider handles hosting, backups, upgrades, monitoring and security.
- Tenants: a tenant is one subscribing organisation and may run one or more legal companies, each with fully isolated data, numbering sequences, tax settings and ledger.
- Subscriptions: plans, limits and billing are defined in Section 5.
- Onboarding: self-service sign-up with guided setup (company profile, chart of accounts, master data, prices, users).
- Language: Arabic-first right-to-left interface, with English as a second language.

### 1.7 Target Customers
Bulk-haulage companies that run their own fleet alongside external (contracted) trucks, and that subcontract loads to partner haulage companies.

### 1.8 Document Conventions
- Requirement IDs: US-xx user stories · FR-xxx functional requirements · BR-xxx business rules · NFR-xxx non-functional requirements.
- Priority: MoSCoW — Must / Should / Could / Won't (this release).
- "Shall" marks a mandatory requirement; "should" marks a recommendation.
- Values confirmed during review (trial length, targets, retention, etc.) are listed in Section 19.
- Deletion: wherever this document says a record is deleted, removed or cleared, it means soft-deleted (BR-041); no record is ever hard-deleted.
- Units in examples: money in JOD with 3 decimals; weights in tonnes with 3 decimals.

## Section 2 — Vision & Goals

### 2.1 Vision
Give every bulk-haulage company one platform to run its whole cycle, from loading a truck to closing the books — accurate, auditable and live for every user — with no IT infrastructure of its own, and adaptable by its own administrators to the way it works.

### 2.2 Product Goals
| ID | Goal |
|---|---|
| G-01 | Capture every shipment once, quickly and correctly: keyboard quick entry and bulk import, with validation, duplicate prevention and automatic pricing at entry. |
| G-02 | Price consistently and automatically: customer and partner prices from tariffs, driver wages from monthly loading-site rates; deviations flagged, and blockable where the company chooses. |
| G-03 | Reconcile operations to the ledger to the last fils: every invoice, return, receipt, advance, settlement and partner payment posts a balanced entry automatically; sub-ledgers always agree with the general ledger. |
| G-04 | Guarantee auditability: nothing financial is deleted — everything is reversed, numbers are retained, referenced records are protected, and every change is logged with user and time. |
| G-05 | Give management live visibility: dashboard KPIs, driver productivity, weight shortages, unbilled trips, receivables ageing, document-expiry alerts and custom reports. |
| G-06 | Comply with tax requirements: sales tax, gap-free invoice numbering and national e-invoicing for the launch market, designed to allow additional markets. |
| G-07 | Enable real-time teamwork: many users work on the same company data at the same time, with record locking and no lost or overwritten work. |
| G-08 | Configurable fields and layouts: tenant administrators configure fields, labels, invoice layouts and reports. |
| G-09 | Deliver as a dependable SaaS: fast self-service onboarding, strict tenant and company isolation, high availability, and secure, backed-up data. |

## Section 3 — Users, Personas, Roles & Permissions

### 3.1 User Levels
| Level | Who | Description |
|---|---|---|
| Platform | Service provider staff | Operate the SaaS service. No access to tenant business data by default; support access requires a time-limited grant from the tenant and every action is audited. |
| Tenant | Staff of the subscribing organisation | Use the platform inside their own tenant. A user may access one or more companies of the tenant, with a role per company. |
| External parties | Drivers, partner haulage companies, customers | Not system users. They receive printed or exported documents: statements, vouchers, invoices and claim statements. |

### 3.2 Personas
| Persona | Default role | Main needs |
|---|---|---|
| Tenant Owner | Owner | Manage the subscription and plan, create and archive companies, appoint administrators; full access to all business functions. |
| System Administrator | Administrator | Configure each company: account mapping, tax and options, periods, monthly site prices, tariffs, users and roles, custom fields and invoice layouts; run maintenance actions; review the audit log. |
| Accountant | Accountant | Issue, approve, return and void invoices. Record receipts, driver advances, settlements and partner payments. Review and post journal entries. Operate the accounting module: general ledger, accounts payable and accounts receivable, statements, financial reports. Submit e-invoices. |
| Dispatcher / Data-entry clerk | Dispatcher | Enter trips fast (quick entry, import); edit or void trips before invoicing; maintain drivers (with their truck details), customers, sites, materials and prices; view driver balances and reports. Cannot invoice or handle money. |
| Manager / Viewer | Viewer | Read-only access to trips, invoices, driver accounts, journal and reports; personal dashboard with pinned indicators. |
| Provider Support Agent | Platform role | Diagnose tenant issues under a tenant-granted, time-limited, audited access window. |
| Driver (external) | — | Receives settlement statement, payment voucher and trip statement. |
| Partner haulage company (external) | — | Receives a statement of shipments carried by its trucks and payments made to it. |
| Customer (external) | — | Receives claim statement / tax invoice with shipment annex, credit notes, receipt vouchers and account statements. |

### 3.3 Permission Model
- Permissions are assigned per user per company (e.g. Accountant in Company A, Viewer in Company B).
- Every operation checks its permission on the server; hiding a button is never the only control.
- A role is a named set of permission keys. Tenants use preset roles or create custom roles.
- Every grant, revoke or role change is recorded in the audit log with user and time.

### 3.4 Permission Keys
| Group | Key | Grants |
|---|---|---|
| Trips | trips.view | View trips and trip history |
| | trips.edit | Create, import and edit trips |
| | trips.void | Void trips with a reason |
| Invoicing | inv.view | View invoices, drafts and credit notes |
| | inv.issue | Create invoices (trip-based and manual), approve drafts, issue credit notes, design invoice layouts, submit e-invoices |
| | inv.void | Void invoices and credit notes |
| Receipts | rcpt.manage | Record and void customer receipts |
| Drivers & partners | drv.view | View driver and partner accounts and statements |
| | drv.advance | Record, edit and void driver advances |
| | drv.settle | Settle drivers, void settlements, record partner payments |
| Journal | je.view | View automatically generated journal entries |
| | je.export | Post entries to the ledger, retry pending postings, export journal batches |
| Accounting | acct.view | View the general ledger, trial balance, financial statements and account statements |
| | acct.post | Create, edit and void manual journal entries; import entries |
| | acct.apar | Manage payable and receivable documents, payments and returns |
| | acct.setup | Manage the chart of accounts, cost centres and banks |
| | acct.maint | Run accounting maintenance: bulk deletion of unlinked entries, clearing a company's data, or replacing the chart of accounts. Each action requires typed confirmation, offers a backup first, is refused in closed periods or for records linked to documents, and is logged. |
| Reports & master data | reports.view | Access all reports, the report builder and saved reports |
| | masters.manage | Manage master data, tariffs, monthly site prices and partners; enter manual driver-rate overrides |
| Administration | settings.manage | Company settings, account mapping, tax, periods, numbering, configuration of fields and layouts |
| | users.manage | Invite, edit, disable, unlock and archive users; assign roles |
| Tenant | tenant.manage | Subscription, plan and billing; create and archive companies; tenant-wide settings |

### 3.5 Role Presets
| Role | Permissions |
|---|---|
| Owner | All keys, including tenant.manage |
| Administrator | All keys except tenant.manage (includes acct.maint) |
| Accountant | trips.view, inv.*, rcpt.manage, drv.*, je.*, acct.view, acct.post, acct.apar, reports.view |
| Dispatcher | trips.*, drv.view, masters.manage, reports.view |
| Viewer | trips.view, inv.view, drv.view, je.view, acct.view, reports.view |
| Custom | Any combination selected by the administrator |

### 3.6 Rules
- BR-001 — A tenant shall always have at least one active Owner; the last Owner cannot be disabled, archived or demoted.
- BR-002 — Each company shall always have at least one active user with users.manage.
- BR-003 — Users are never deleted once they have performed any action; they can only be disabled or archived, so the audit trail keeps their identity.
- BR-004 — A manual driver-rate override requires masters.manage; so does any customer-price deviation from the tariff when the company enables the tariff lock.

## Section 4 — Scope

### 4.1 In Scope
| Area | In scope |
|---|---|
| Operations | Trips: full entry form, keyboard quick-entry row, bulk import (Excel/CSV/paste); voiding with reversal; weight-shortage tracking against a tolerance; partner (subcontracted) shipments; record history |
| Pricing | Customer and partner tariff table (most-specific rule wins, validity dates, breach flag and report, optional lock); monthly loading-site driver rates for own fleet and external trucks, frozen into each trip at save |
| Invoicing | Trip-based and manual invoices; draft → approval workflow; discounts, extra fees, sales tax; credit notes (full or partial returns); invoice layout designer (claim statement with shipment annex, tax-invoice layout); QR code on printed invoices |
| Cash, drivers & partners | Customer receipts (against an invoice or on account); driver advances; driver settlements with deductions; partner payments; printed receipt, payment and journal vouchers with the amount in words |
| Ledger integration | Automatic balanced journal entries for every financial event; reversal on void; account mapping to the chart of accounts; immediate posting to the accounting module with a pending queue for failures; journal export (JSON/CSV) |
| Accounting module | Chart of accounts (import/export); journal (manual entries, editing of posted entries with change history, import/export); general ledger, trial balance, profit & loss, balance sheet; accounts payable and accounts receivable documents, payments and returns; banks, cost centres, periods; account statements; report builder; maintenance zone |
| E-invoicing | National e-invoicing: build, validate and submit invoices and credit notes directly from the platform; capture of the response and QR code |
| Reporting | Dashboard with personal pinned indicators and thresholds; operational, driver and weight reports; account statements (customer, driver, ledger account, cash/bank, receivables ageing, driver balances); custom report builder with saved reports; export of every screen to Excel and PDF |
| Master data | Drivers with their registered truck (plate, truck type, capacity, ownership type, partner link), truck details edited from the driver's profile, and document-expiry tracking (licence, registration, insurance, inspection); customers, partners, materials, loading sites, unloading sites, ownership types; rename cascade; referential-integrity protection; archive instead of delete |
| Configuration | Custom fields (6 types) for trips, drivers and customers; rename, hide, require and reorder standard fields; choice of trip-table columns |
| Collaboration | Real-time multi-user work with record locking; live updates between users; global search |
| SaaS platform | Self-service tenant sign-up and guided setup; multi-company per tenant with full isolation; users, roles and permissions per company; subscription plans and billing; audit log; automated backups; tenant data export |
| Localisation | Arabic (RTL) and English interface; launch market Jordan (JOD with 3 decimals, 16 % sales tax, national e-invoicing); currency, tax rate and e-invoicing connector configurable per company so other markets can be added |

### 4.2 Out of Scope
- Payroll and HR.
- Fleet telematics and GPS tracking.
- Fuel-card integration.
- Native mobile apps (the web application is responsive, but designed primarily for desktop data entry).
- Free-form formulas or scripting by users.
- Multi-currency transactions within a single company (each company works in one base currency).

## Section 5 — SaaS Model: Tenancy, Subscriptions & Onboarding

### 5.1 Tenancy Model
- Tenant → Companies → Users. A tenant is one subscribing organisation containing one or more companies (legal entities). Users belong to the tenant and are given access to specific companies.
- Tenant isolation: every record carries its tenant identifier; no user, report, search, export or API call can read or change another tenant's data.
- Company isolation: inside a tenant, each company has its own master data, trips and documents, numbering sequences, tax and e-invoicing settings, periods, chart of accounts and ledger. Reports and searches always run within the selected company.
- Company switcher: a user with access to several companies switches between them without signing in again.

### 5.2 Tenant Lifecycle
| Status | Description | User access |
|---|---|---|
| Trial | Starts at sign-up; lasts 14 days | Full access within the trial plan's limits |
| Active | Subscription paid and current | Full access within the plan's limits |
| Past due | A payment failed; grace period of 7 days with reminders to the Owner | Full access with a warning banner |
| Suspended | Grace period ended without payment | Read-only: view, print and export, but not create or change data |
| Cancelled | Owner cancelled, or tenant suspended for 30 days | Owner only, for data export, during a retention period of 90 days |
| Deleted | Retention period ended | None; all tenant data is soft-deleted and permanently inaccessible to users (no hard deletion); personal data handled per SEC-19 |

### 5.3 Subscription Plans & Billing
- All business flows in Section 4 are available in every plan; plans differ only in limits.
- The provider defines plans, prices and limits from the platform console without a code change. Limits include number of companies, active users, trips per month, and storage for attachments and exports.
- Billing cycle: monthly or annual, billed in advance; payment by card or bank transfer through a payment gateway.
- The tenant receives a tax invoice for each subscription charge; the Owner can view and download invoices and billing history.
- Upgrades apply immediately with pro-rated charges; downgrades apply at the next renewal, only if current usage fits the lower plan's limits.
- When a limit is reached, creating the limited item is blocked with a clear message and an upgrade option; existing data is never affected or hidden.

### 5.4 Onboarding & Guided Setup
After sign-up the Owner follows a setup checklist; progress is saved, steps can be done in any order and resumed later.
1. Create account: Owner name, verified email, password, organisation name, and mandatory two-factor authentication set-up.
2. Company profile: name, tax number, address, phone and logo (printed on documents).
3. Market settings: country, base currency and decimals, sales-tax rate, e-invoicing credentials (optional at this stage).
4. Chart of accounts: start from the built-in transport chart template, or import the company's own chart.
5. Account mapping: map the required posting keys to accounts; invoicing stays disabled until mapping is complete.
6. Master data: add or import drivers, customers, partners, materials, loading sites and unloading sites.
7. Prices: customer and partner tariffs; current month's driver rates per loading site for both fleet types.
8. Users: invite staff by email and assign a role per company.
- Demo company: a separate company filled with sample data for training, without touching real data.
- Data import: historical trips and opening balances via the import wizard; rows needing checks are flagged for review.

### 5.5 Provider Console (Platform Administration)
Internal console for provider staff only:
- Tenants: list with status, plan, usage against limits, sign-up and renewal dates.
- Plans: create and edit plans, prices and limits.
- Account actions: extend a trial, apply a credit, suspend or reactivate a tenant (all audited).
- Support access: request access to a tenant; the Owner approves a time-limited window; every action in the window is logged and visible to the tenant.
- Service announcements: planned-maintenance notices shown inside the application.
- Platform health: usage and health metrics.

### 5.6 Data Ownership & Portability
- The tenant owns its data.
- The Owner can export a full copy of any company's data at any time: master data, trips and documents to Excel; the full ledger to Excel; a complete machine-readable package as JSON.
- After cancellation the export stays available for the retention period; then the tenant's data is soft-deleted (hidden and inaccessible, never hard-deleted) and the Owner receives a confirmation.

### 5.7 Rules
- BR-010 — A tenant in Suspended status shall be read-only: no document may be created, approved, voided or posted, and no master data may be changed.
- BR-011 — Reactivating a tenant (by payment or provider action) shall restore full access immediately with no data change.
- BR-012 — A plan downgrade shall be refused while usage exceeds the target plan's limits; the message lists which limits are exceeded.
- BR-013 — Reaching a plan limit shall never block viewing, printing or exporting existing data.
- BR-014 — Provider staff shall have no access to tenant business data without an approved, time-limited support window; all actions within it are written to the tenant's audit log.

## Section 6 — User Stories & Acceptance Criteria

### 6.1 Trips
**US-01 — Quick trip entry.** As a dispatcher, I want to record a shipment in one row using only the keyboard, so that I can enter dozens of receipts per hour.
- Quick-entry row at the top of the Trips screen; Tab moves between fields; Enter saves and keeps the row open.
- After save only receipt number and unloaded weight are cleared; driver, customer, material and route are remembered, including in later sessions.
- Customer price pre-filled from the tariff; driver wage pre-filled from the monthly site rate with a note naming month and site.
- Duplicate receipt number within the company rejected inline; future date rejected.
- The last ten saved trips are listed under the row, each with a History button (every action on the trip — created, edited, voided, invoiced, settled — with user and time) and an Edit button (available only while not invoiced or settled).
- A Created Trips button at the top of the table opens the full list of all created trips, with filters and search.

**US-02 — Bulk trip import.** As a dispatcher, I want to import trips from Excel/CSV or pasted rows, so that large batches don't have to be typed.
- Columns matched automatically by name (including Arabic alternatives); mapping adjustable.
- Preview before commit: valid rows, rejected rows with reasons, detected duplicates.
- Tolerant mode: missing optional values take defaults; row flagged Needs review.
- No driver-rate column → wage from monthly site rate; no rate available → row rejected.

**US-03 — Void a trip.** As a dispatcher, I want to void a wrongly entered trip with a reason, so that the record and its accounting are corrected without deleting anything.
- Reason required; reversal entry posted; status Void; record kept.
- Refused if invoiced, settled, or dated in a closed period.

**US-04 — Weight shortage tracking.** As a manager, I want each trip's shortage checked against a tolerance, so that losses are visible early.
- Shortage and shortage % calculated automatically; trips above company tolerance (default 0.5 %) flagged.
- Shortage report by driver, site and period.

### 6.2 Pricing
**US-05 — Driver wage from monthly site rates.** As an accountant, I want each trip to take the driver wage from the monthly rate of its loading site, so that wages are consistent and cannot be typed wrongly.
- Per month and loading site: own-fleet and external-fleet rates per ton.
- On save the trip stores the rate for its loading month and the driver's fleet type (from the driver's ownership type), with rate source and month; the rate is then frozen.
- No rate → trip cannot be saved; message names site, month and fleet type.
- A user with masters.manage may enter a manual rate; the trip is flagged Manual and audited.
- Later rate changes never alter saved trips; a site rate with trips in its month cannot be deleted.

**US-06 — Customer and partner tariffs.** As an administrator, I want prices to come from a tariff table, so that they are applied correctly and deviations are caught.
- Tariff line keyed by party × material × loading site × unloading site; any key can be "all"; optional validity dates.
- Most specific line wins; ties → latest start date.
- Deviating trips flagged; breach report shows loss or excess per trip.
- With the tariff lock on, deviations are blocked for users without masters.manage.

### 6.3 Invoicing
**US-07 — Invoice unbilled trips.** Only active, unbilled trips of the selected customer; subtotal, discount (capped at subtotal), extra fees, sales tax, total; blocked until account mapping is complete and for closed-period dates.
**US-08 — Manual invoice.** Free-text lines (description, quantity, unit price) with the same totals, tax, numbering, approval and posting rules.
**US-09 — Draft and approval.** Draft has no number and posts nothing; prints with DRAFT stamp; approval assigns the next gap-free number, posts the entry and queues e-invoicing if enabled; deleting a draft releases its trips.
**US-10 — Credit note.** Selected trips returned with their share of discount, fees and tax; reversal entry posted; trips become unbilled; cannot exceed the invoice's remaining balance.
**US-11 — Void an invoice.** Reversal posted; number kept; trips released; refused while active receipts or credit notes exist.
**US-12 — Claim statement with shipment annex.** Claim statement (default) and tax-invoice layouts; administrator edits title, band text, grouping, columns and labels with live preview, saved per company; body shows grouped totals, annex lists each shipment (truck, dates, weights, price, amount, driver, route); page number, QR code and signature footer; manual invoices print their lines.
**US-13 — National e-invoicing.** Company settings hold seller tax number, registration details and credentials; platform builds and validates the e-invoice, listing anything missing; submits directly and records unique ID, system number and QR code (printed); status Not required / Queued / Submitted / Rejected; rejected shows the reason and can be resubmitted.

### 6.4 Cash, Drivers & Partners
**US-14 — Customer receipt.** Against an invoice (≤ remaining balance) or on account; cash, bank transfer or CliQ; receipt entry posted; voiding posts a reversal; receipt voucher with amount in words and number of attachments.
**US-15 — Driver advance.** Numbered; posts an entry; editable only while unsettled (reverse and re-post); voiding requires a reason; payment voucher printable.
**US-16 — Driver settlement.** Gathers open trip wages and open advances, plus deductions; net = wages − advances − deductions; negative net refused; items linked and locked; entry posted; voiding releases items and reverses; statement and payment voucher printable.
**US-17 — Partner shipments and payments.** A trip is a partner trip when a partner is selected on it (pre-filled when the driver is linked to a partner company); it uses the partner tariff and carries no driver wage; partner payments post an entry; partner statements and reports available.

### 6.5 Ledger & Accounting
**US-18 — Automatic journal entries.** Each financial event posts its defined entry; voiding posts the linked mirror entry; unbalanced or closed-period entries refused; entries post immediately; failures wait in a pending list with reason and retry.
**US-19 — Period closing.** No posting, edit, void or import in a closed period (operations and accounting); closing and reopening requires settings.manage and is audited.
**US-20 — Accounting operations.** Chart of accounts import/export (with debit and credit opening-balance columns); manual journal entries (create, edit with change history, void by reversal, import with preview rejecting unbalanced lines, unknown accounts or closed periods); general ledger, trial balance, profit & loss, balance sheet, account statements; accounts payable and accounts receivable documents, payments and returns with printed documents.

### 6.6 Reporting
**US-21 — Dashboard.** Today's figures, driver document-expiry alerts (licence, registration, insurance, inspection), unposted entries, unbilled trips; any saved report can be pinned with a measure and threshold that colours the card; clicking opens the report.
**US-22 — Account statements and ageing.** Opening balance, movements, running and closing balance; ageing buckets not due, 1–30, 31–60, 61–90, over 90 days as of a chosen date; unapplied payments shown separately.
**US-23 — Report builder.** Data source, row and column grouping, measures, filters, sort, top-N; detail mode; save by name, export, pin to dashboard; custom fields available automatically.

### 6.7 Master Data & Configuration
**US-24 — Driver records with truck details.** As a dispatcher, I want complete driver records including the driver's truck, so that no unlicensed or uninsured truck is used unnoticed. National ID (10 digits, unique); phones (local format); address; licence number, type and expiry; payment method cash, bank (IBAN validated) or CliQ. The truck is registered with the driver and its details are edited from the driver's profile: plate number (unique in the company), truck type, capacity in tons, ownership type (Company = own fleet; any other type = external fleet), optional partner company link, and registration, insurance and inspection expiry dates. Documents expired or expiring within 30 days raise alerts.
**US-25 — Protected deletion.** Referenced records cannot be deleted; message lists references with counts (e.g. "Cannot delete — linked to 4 trips · 1 tariff line. Archive instead."); drivers, customers, partners and users with history can only be archived; renaming a material or site updates it everywhere.
**US-26 — Custom fields.** Text, number, date, list, checkbox, long text; default, help text, required, show-as-column; appear in forms, tables, exports, imports and report builder; a field with data can only be hidden, and its type cannot change.
**US-27 — Configure standard fields.** Rename, hide, require, reorder; choose trip-table columns; core fields (receipt number, date, driver, customer, sites, material, loaded weight, price, wage) can be renamed but never hidden; required fields block saving with "Field «X» is required".

### 6.8 Collaboration & Platform
**US-28 — Real-time teamwork.** Changes appear for other users within seconds without refresh; opening a record for editing locks it ("X is editing this record"); lock released on close or after inactivity; document numbers unique and gap-free under concurrent issuing.
**US-29 — Global search.** Keyboard shortcut; tolerates Arabic spelling variants (hamza, ta marbuta); result opens the matching screen filtered to the record.
**US-30 — Record history and audit.** Each record shows created, edited, voided with user and time, plus links to the invoice or settlement that includes it; company audit log of every add, edit, void, archive, setting change and maintenance action.
**US-31 — Sign-up and guided setup.** Sign-up creates the tenant, Owner account and first company; checklist tracks the eight onboarding steps; any order; resumable.
**US-32 — Invite users and assign roles.** Invitation link expires after a set time; preset or custom roles; disable, unlock, reset password, archive — all audited.
**US-33 — Manage subscription.** Usage against each limit; upgrades immediate; downgrades at renewal only if usage fits; subscription invoices downloadable.
**US-34 — Multiple companies.** Company switcher changes all screens, sequences and reports to the selected company; data never crosses companies.

## Section 7 — Functional Requirements

### 7.1 Trips

#### 7.1.1 Trip Fields
| Field | Type | Required | Rule |
|---|---|---|---|
| Receipt number | Text | Yes | Unique within the company; comparison ignores letter case and spaces. |
| Trip date | Date | Yes | Not in the future; not in a closed period. |
| Loading date | Date | Yes | Not in the future; its month decides the driver rate; defaults to the trip date. |
| Unloading date | Date | No | On or after the loading date. |
| Driver | Selection | Yes | Active drivers only. The driver's registered truck (plate, type, capacity) is shown with the selection. |
| Customer | Selection | Yes | Active customers only. |
| Loading site | Selection | Yes | Active loading sites only. |
| Unloading site | Selection | Yes | Active unloading sites only. |
| Material | Selection | Yes | Active materials only. |
| Loaded weight (t) | Number, 3 decimals | Yes | Greater than 0; warning if it exceeds the driver's truck capacity. |
| Unloaded weight (t) | Number, 3 decimals | No | Warning if it exceeds loaded weight × 1.02. |
| Customer price (per ton) | Money | Yes | Pre-filled from the customer tariff. |
| Partner | Selection | No | Pre-filled when the driver is linked to a partner company; selecting a partner makes the trip a partner trip. |
| Driver rate (per ton) | Money | Yes, non-partner trips | From the monthly site rate, frozen at save; 0 for partner trips. |
| Partner rate (per ton) | Money | Yes, partner trips | Pre-filled from the partner tariff. |
| Notes | Text | No | — |
| Custom fields | As configured | As configured | Defined by the administrator. |

System-stored, not user-editable: fleet type, rate source (Monthly/Manual), rate month, tariff price; shortage, revenue, wage, partner cost; flags (Manual rate, Tariff breach, Needs review); status, invoice link, settlement link; created/updated user and time.

#### 7.1.2 Trip Status
| Status | Meaning | Allowed actions |
|---|---|---|
| Active – Unbilled | Saved, not invoiced | View, Edit, Void, History |
| Active – Invoiced | In an approved invoice or draft | View, History (invoice must be returned or voided first) |
| Active – Settled | Wage included in a driver settlement | View, History (settlement must be voided first) |
| Void | Cancelled with reason, reversal posted | View, History |
A trip can be both invoiced and settled.

#### 7.1.3 Requirements
| ID | Requirement | Priority |
|---|---|---|
| FR-001 | Create a trip with the fields in 7.1.1 from the full trip form or the quick-entry row. | Must |
| FR-002 | Validate every trip on save against 7.1.1; block saving on a missing required field or failed rule, naming the field. Warnings (capacity, unloaded > loaded × 1.02) do not block saving. | Must |
| FR-003 | Driver's truck: selecting a driver shows the driver's registered truck (plate, type, capacity); the truck's capacity is used for the loaded-weight warning. | Must |
| FR-004 | Fleet type from the driver's ownership type: Company → own fleet; any other type → external fleet. Partner trip when a partner is selected (pre-filled from the driver's partner link). | Must |
| FR-005 | Live calculations: billing qty = unloaded weight when "Bill on unloaded weight" is on (default on) and unloaded is recorded, else loaded; wage qty = loaded weight when "Pay driver on loaded weight" is on (default off), else unloaded (fallback loaded); revenue = billing qty × price; driver wage = wage qty × driver rate (0 for partner trips); partner cost = wage qty × partner rate; margin = revenue − wage − partner cost; 3-decimal rounding. | Must |
| FR-006 | Automatic pricing: customer price and partner rate from the tariff, driver rate from the monthly site rate (7.2); the form shows each value's source. | Must |
| FR-007 | Quick-entry row: Tab between fields, Enter saves; only receipt number and unloaded weight cleared; last driver, customer, material and route remembered per user across sessions; duplicate receipt and future dates rejected inline. | Must |
| FR-008 | Last ten trips saved by the user listed under the row, each with History and Edit (Edit only for unbilled, unsettled trips). | Must |
| FR-009 | Created Trips button opens the full list: filters (status, unbilled/invoiced, settled, own/external fleet, own fleet/partner, shortage, tariff breach, manual rate, needs review, void), date range, search, paging, sort, configurable columns, Excel/PDF export. | Must |
| FR-010 | Edit only while unbilled, unsettled and not in a closed period; wage entry reversed and re-posted; driver rate kept unless loading site, loading month or driver changed (then re-resolved); a typed different rate follows the manual-rate rules (7.2). | Must |
| FR-011 | Void with required reason: reversal entry, status Void, record kept; blocked if invoiced, settled or in a closed period. | Must |
| FR-012 | Shortage = loaded − unloaded; shortage % = shortage ÷ loaded × 100; flagged above company tolerance (default 0.5 %) in the list and shortage report. | Must |
| FR-013 | Import from .xlsx, CSV/TSV or paste: automatic column matching (Arabic alternatives) with adjustable mapping; preview of valid, rejected (with reasons) and duplicate rows; tolerant mode with defaults and Needs review flag; monthly site rate when no driver-rate column, row rejected when no rate; import log (user, time, file, counts). | Must |
| FR-014 | A user with trips.edit can clear Needs review after checking; recorded in history. | Must |
| FR-015 | Trip history: created, each edit with changed fields (old → new), voided, added to/removed from invoice or credit note, settled/released — with user and time. | Must |
| FR-016 | Wage accrual on save per 7.5: own/external → wage expense to driver payable; partner → partner cost to partner payable. | Must |
| FR-017 | Custom trip fields appear in form, quick-entry row (when marked as column), list, exports, imports and report builder. | Must |
| FR-018 | Trip statement for a driver, customer or partner over a period, with weights, amounts and totals (print/export). | Should |

### 7.2 Pricing

#### 7.2.1 Customer & Partner Tariffs
| Field | Rule |
|---|---|
| Party type | Customer or Partner |
| Party | A specific customer or partner, or All |
| Material | A specific material, or All |
| Loading site | A specific site, or All |
| Unloading site | A specific site, or All |
| Price per ton | Money, 3 decimals, ≥ 0 |
| Valid from / Valid to | Optional; Valid to on or after Valid from |
| Notes | Optional |

| ID | Requirement | Priority |
|---|---|---|
| FR-019 | Maintain tariff lines for customers and partners: add, edit, archive, import, export. | Must |
| FR-020 | A line matches a trip when each key equals the trip's value or is All, and the trip date is within the validity dates (if set). | Must |
| FR-021 | Most specific line wins: party = 8, material = 4, loading site = 2, unloading site = 1; highest score wins; tie → latest Valid from. (All keys All = 0; party = 8; party + material = 12; all four = 15.) | Must |
| FR-022 | Winning customer line fills the customer price, winning partner line fills the partner rate (form, quick entry, import); trip stores tariff price beside the price used; no match → user enters the price. | Must |
| FR-023 | Tariff breach flag when price ≠ tariff price; breach amount = (price − tariff price) × billing quantity; negative = loss, positive = excess. | Must |
| FR-024 | Breach report by customer/partner, period and route, with loss and excess totals; exportable. | Must |
| FR-025 | Tariff lock setting: deviating prices saved only by users with masters.manage; each deviation audited. | Must |
| FR-026 | Tariff history (add, edit, archive with user and time); tariff changes never alter saved trips. | Must |

#### 7.2.2 Monthly Loading-Site Driver Rates
| ID | Requirement | Priority |
|---|---|---|
| FR-027 | Per month (YYYY-MM) and loading site: own-fleet and external-fleet rates per ton. | Must |
| FR-028 | Month strip with status (no rates / partial / complete); selecting a month lists all active loading sites for entry; save and print. | Must |
| FR-029 | Rates entered for each month separately; a new month never inherits the previous month's rates. | Must |
| FR-030 | New-trip resolution: monthly rate and no different typed rate → Monthly; monthly rate and different typed rate → Manual (flag, masters.manage); no rate and partner trip → 0; no rate and masters.manage types a rate → Manual; otherwise blocked with message naming site, month and fleet type. | Must |
| FR-031 | Trip stores rate, source (Monthly/Manual) and rate month at save; later table changes never alter saved trips. | Must |
| FR-032 | On edit the rate is kept unless loading site, loading month or driver changes (then re-resolved by FR-030); a different typed rate follows manual rules. | Must |
| FR-033 | Import: no driver-rate column → monthly rate; no rate → row rejected; supplied rate differing from monthly → Manual. | Must |
| FR-034 | Editing a month that already has trips warns that only new trips use the new rates. | Must |
| FR-035 | Clearing both rates of a site in a month with trips is refused; renaming a loading site keeps its rates linked. | Must |
| FR-036 | Rate history: old value, new value, user, time. | Must |
| FR-037 | Dashboard warning when the current month has loading sites without rates. | Should |

### 7.3 Invoicing, Returns & E-invoicing

#### 7.3.1 Invoice Fields
| Field | Rule |
|---|---|
| Customer | Required; active customers only |
| Invoice type | Trip-based or Manual |
| Invoice date | Required; not in the future; not in a closed period |
| Due date | Defaults to invoice date + the customer's payment terms (days); editable |
| Lines | Trip-based: selected unbilled trips. Manual: description, quantity, unit price (each > 0) |
| Discount | 0 ≤ discount ≤ subtotal |
| Extra fees | ≥ 0, with a description |
| Tax rate | From company settings; shown on the invoice |
| Notes | Optional; printed |
| Layout | Claim statement (default) or tax invoice |
System-stored: number (at approval), subtotal, base, tax, total, paid, returned, remaining balance, status, e-invoice status and references, created/approved/voided user and time.

#### 7.3.2 Invoice Status
| Status | Meaning |
|---|---|
| Draft | Not approved; no number, no entry; editable or deletable |
| Approved – Unpaid | Numbered and posted; no receipts yet |
| Approved – Partially paid / Partially returned | Remaining balance > 0 after receipts or credit notes |
| Approved – Closed | Remaining balance = 0 |
| Void | Cancelled with reason; reversal posted; number kept |

#### 7.3.3 Requirements — Invoices
| ID | Requirement | Priority |
|---|---|---|
| FR-038 | Trip picker: selecting a customer lists only its active, unbilled trips, with filters (period, loading site, unloading site, material), select-all and live totals. | Must |
| FR-039 | Manual invoice with description, quantity, unit price lines; same totals, tax, numbering, approval, posting and printing rules. | Must |
| FR-040 | Totals calculated live and stored at approval per Section 8.3. | Must |
| FR-041 | Invoice approval setting: on → Save creates a Draft; off → Save approves directly; Save & approve available to inv.issue either way. | Must |
| FR-042 | A draft reserves its trips; can be edited, printed with DRAFT stamp, approved or deleted (deletion is a soft delete and releases the trips). | Must |
| FR-043 | Approval assigns the next gap-free number, posts the invoice entry, marks trips invoiced and queues e-invoicing if enabled. | Must |
| FR-044 | Approval blocked when account mapping is incomplete, date in a closed period, any trip no longer active and unbilled, or total ≤ 0. | Must |
| FR-045 | Void (inv.void, reason): reversal posted, number kept, trips released, VOID stamp; blocked with active receipts or credit notes, or in a closed period. | Must |
| FR-046 | An invoice accepted by the national e-invoicing system cannot be voided; it is cancelled with a full credit note. | Must |
| FR-047 | Invoice list: filters (status, customer, period, paid/unpaid, overdue, e-invoice status), search by number, totals row, Excel/PDF export. | Must |
| FR-048 | Invoice view: lines/trips, totals, receipts, credit notes, remaining balance, e-invoice status, full history with user and time. | Must |

#### 7.3.4 Requirements — Credit Notes
| ID | Requirement | Priority |
|---|---|---|
| FR-049 | Created from an approved invoice: trip-based selects returned trips; manual selects lines and quantities; reason required. | Must |
| FR-050 | Returned share carries proportional discount, fees and tax (BR-025 to BR-027); cannot exceed remaining balance. | Must |
| FR-051 | Own number sequence; posts the mirror of the invoice entry for the returned share; returned trips become unbilled. | Must |
| FR-052 | Void (inv.void, reason): reversal posted, trips invoiced again; blocked if any trip was re-invoiced or in a closed period. | Must |
| FR-053 | Printout shows its number, original invoice number and date, returned items, totals and amount in words. | Must |

#### 7.3.5 Requirements — Layouts & Printing
| ID | Requirement | Priority |
|---|---|---|
| FR-054 | Two layouts per company: claim statement (page 1 totals grouped by loading site, route or material; page 2 annex per shipment) and tax invoice. | Must |
| FR-055 | Layout designer (inv.issue) with live preview, saved per company: title, header band text, grouping, annex columns and order, labels, logo, footer text, signature blocks. | Must |
| FR-056 | Annex columns: receipt number, truck plate, loading/unloading dates, loaded/unloaded/billed weight, price, amount, driver, loading/unloading site, material, custom trip fields. | Must |
| FR-057 | Every printed invoice and credit note shows company and customer details with tax numbers, number and date, totals with tax, amount in words, page X of Y, QR code and signature footer; manual invoices print their lines. | Must |
| FR-058 | Print/PDF for drafts (DRAFT), approved and void (VOID) documents; every print logged. | Must |

#### 7.3.6 Requirements — National E-invoicing
| ID | Requirement | Priority |
|---|---|---|
| FR-059 | Company settings: enable/disable, seller tax number, registration details, income-source sequence, credentials (encrypted, never shown again), test/production environment. | Must |
| FR-060 | Build each approved invoice and credit note in the national format and validate before submission, listing missing or invalid fields. | Must |
| FR-061 | Automatic submission on approval; manual submission by inv.issue; automatic retries, then Rejected/Failed list. | Must |
| FR-062 | Store unique ID, system number, QR code and submission time; QR printed on the document. | Must |
| FR-063 | Status Not required / Queued / Submitted / Rejected; rejected shows the reason and can be corrected and resubmitted. | Must |
| FR-064 | Credit notes reference the original invoice's unique ID and cannot be submitted before the invoice is accepted. | Must |
| FR-065 | E-invoicing report by status and period with totals; exportable. | Must |
| FR-066 | E-invoicing connector selected per company, so other markets can be added without changing invoicing flows. | Should |

### 7.4 Cash, Drivers & Partners

#### 7.4.1 Customer Receipts
| Field | Rule |
|---|---|
| Customer | Required; active customers |
| Receipt date | Required; not in the future; not in a closed period |
| Invoice | Optional; only the customer's approved invoices with remaining balance > 0; empty = on account |
| Amount | > 0; against an invoice ≤ its remaining balance |
| Payment method | Cash, bank transfer, CliQ |
| Cash box / bank account | Required; cash → cash boxes; transfer and CliQ → bank accounts |
| Reference | Required for bank transfer and CliQ |
| Attachments count | Number, printed on the voucher |
| Notes | Optional |

| ID | Requirement | Priority |
|---|---|---|
| FR-067 | Record a receipt; numbered from the receipt sequence; posts debit cash/bank, credit trade receivables. | Must |
| FR-068 | Against an invoice: reduces its remaining balance and updates status. On account: reduces the customer balance and shows as unapplied in statements and ageing. | Must |
| FR-069 | Void (rcpt.manage, reason): reversal posted, invoice balance restored, number kept; blocked in a closed period. | Must |
| FR-070 | Receipt voucher: number, date, customer, amount in figures and words, method, reference, invoice number, attachments count, signatures. | Must |
| FR-071 | Receipts list: filters (customer, period, method, cash box/bank, on account/against invoice, status), totals, Excel/PDF export. | Must |

#### 7.4.2 Driver Advances
| Field | Rule |
|---|---|
| Driver | Required; active drivers |
| Advance date | Required; not in the future; not in a closed period |
| Amount | > 0 |
| Method / cash box or bank / reference | As for receipts |
| Reason / notes | Optional on creation; required on void |

| ID | Requirement | Priority |
|---|---|---|
| FR-072 | Record an advance; numbered from the advance sequence; posts debit driver advances, credit cash/bank. | Must |
| FR-073 | Edit only while unsettled and not in a closed period; reverses and re-posts; change recorded (old → new). | Must |
| FR-074 | Void (drv.advance, reason): reversal, number kept; blocked if settled or in a closed period. | Must |
| FR-075 | Payment voucher: number, date, driver, amount in figures and words, method, reference, signatures (paid by / received by). | Must |

#### 7.4.3 Driver Settlements
| ID | Requirement | Priority |
|---|---|---|
| FR-076 | Driver account view: open trips with wages, open advances, gross / advances / net due; settlement history; running balance. | Must |
| FR-077 | Create a settlement by driver and date; all open trips and advances up to that date pre-selected; user may untick items. | Must |
| FR-078 | Deduction lines: type (shortage, fine, other), amount ≥ 0, note; shortage amounts can be suggested from flagged trips. | Must |
| FR-079 | Net per 8.5, must be ≥ 0; user selects method, cash box/bank and reference. | Must |
| FR-080 | Confirm: next settlement number, items linked and locked, settlement entry posted; blocked in a closed period or when net < 0. | Must |
| FR-081 | Void (drv.settle, reason): reversal, items released, number kept; blocked in a closed period. | Must |
| FR-082 | Settlement statement (driver and truck plate, trips, advances, deductions, gross, net, amount in words, signatures) printed with a payment voucher for the net. | Must |
| FR-083 | Driver statement for any period: opening balance, wages, advances, deductions, settlements, running and closing balance; print/export. | Must |

#### 7.4.4 Partner Payments & Balances
| ID | Requirement | Priority |
|---|---|---|
| FR-084 | Partner account view: partner trips with partner cost, payments, current balance due. | Must |
| FR-085 | Record a partner payment (partner, date, amount > 0, method, cash box/bank, reference, notes); numbered; posts debit partner payable, credit cash/bank. A payment exceeding the partner's current balance due is blocked, with a message showing the balance. | Must |
| FR-086 | Void (drv.settle, reason): reversal, number kept; blocked in a closed period. | Must |
| FR-087 | Payment voucher in the FR-075 format. | Must |
| FR-088 | Partner statement for any period: opening balance, shipments carried (date, receipt, route, weight, rate, cost), payments, running and closing balance; print/export. | Must |

#### 7.4.5 Common Rules
| ID | Requirement | Priority |
|---|---|---|
| FR-089 | Every cash and bank document is dated, numbered from its own gap-free company sequence, and blocked in a closed period. | Must |
| FR-090 | Vouchers can be reprinted any time, marked Copy and logged. | Must |
| FR-091 | Cash box and bank balances on the dashboard and in the cash/bank statement, updated by every receipt and payment. | Must |

### 7.5 Ledger Integration

#### 7.5.1 Account Mapping (Posting Keys)
Each posting key maps to an active, postable (leaf) account.
| Posting key | Used by |
|---|---|
| Driver wage expense | Trip (own/external fleet); settlement when wage accrual is off |
| Driver payable | Trip, settlement |
| Partner cost expense | Trip (partner) |
| Partner payable | Trip (partner), partner payment |
| Trade receivables | Invoice, credit note, receipt |
| Discount allowed | Invoice, credit note |
| Transport revenue | Invoice, credit note |
| Other revenue (extra fees) | Invoice, credit note |
| Sales tax output | Invoice, credit note |
| Sales tax input | Payable documents (supplier bills), payable returns |
| Driver advances | Advance, settlement |
| Driver deductions | Settlement |
| Cash boxes | One account per cash box |
| Bank accounts | One account per bank account |

| ID | Requirement | Priority |
|---|---|---|
| FR-092 | Account mapping screen (settings.manage): every posting key with its account and status mapped / missing. | Must |
| FR-093 | Only active, postable accounts selectable; a mapped account cannot be deactivated. | Must |
| FR-094 | While any required key is unmapped, invoicing and posting are disabled; a banner links to the mapping screen. | Must |
| FR-095 | Mapping changes apply to new entries only; audited (old → new account). | Must |

#### 7.5.2 Automatic Posting
| ID | Requirement | Priority |
|---|---|---|
| FR-096 | Every financial event (trip, invoice, credit note, receipt, advance, settlement, partner payment) posts its entry automatically in the same operation, per 8.6. | Must |
| FR-097 | Entry stores number, date (= document date), description, source document type and number, and lines (account, debit or credit, optional cost centre, party where relevant). | Must |
| FR-098 | Entries must balance and be dated in an open period; otherwise the operation is refused with a clear message. | Must |
| FR-099 | Voiding or editing a document posts a linked reversal (reverses / reversed by); originals never changed or deleted. | Must |
| FR-100 | Setting "Accrue driver wage on trip save" (default on). When off, trips post no wage entry and the settlement debits driver wage expense (gross) instead of driver payable. Partner costs always post on trip save. | Must |
| FR-101 | If an entry cannot be posted for a technical reason, the document is saved and the entry waits in Pending postings with the reason; je.export users retry one or all; pending count on the dashboard. | Must |

#### 7.5.3 Journal View & Export
| ID | Requirement | Priority |
|---|---|---|
| FR-102 | Automatic entries list (je.view): filters period, source type, account, party, status (posted/pending/reversed), number; links to source document and reversal. | Must |
| FR-103 | Journal voucher for any entry: number, date, description, lines, totals, amount in words, signatures. | Must |
| FR-104 | Journal export (je.export) for a period as JSON or CSV batches; each batch numbered and logged (user, time, period, count, totals); re-export allowed. | Must |

#### 7.5.4 Reconciliation
| ID | Requirement | Priority |
|---|---|---|
| FR-105 | Sub-ledger reconciliation report per control account (trade receivables, driver payable, partner payable, driver advances): ledger balance vs sum of party balances as of a date; difference must be 0.000; unmatched entries listed. | Must |
| FR-106 | Manual journal lines on control accounts must name a party (customer, driver or partner). | Must |

### 7.6 Accounting

#### 7.6.1 Chart of Accounts
| Field | Rule |
|---|---|
| Account code | Required; unique within the company |
| Account name | Required; Arabic and English |
| Account type | Asset, Liability, Equity, Revenue, Expense |
| Parent account | Optional; builds the tree |
| Postable | Only leaf accounts are postable |
| Opening balance | Separate debit and credit amounts |
| Active | Yes / No |

| ID | Requirement | Priority |
|---|---|---|
| FR-107 | Maintain the chart as a tree (acct.setup): add, edit, move, deactivate; built-in transport chart template for new companies. | Must |
| FR-108 | An account with entry lines, documents, payments or a mapping link can only be deactivated; a parent with children cannot become postable. | Must |
| FR-109 | Import/export the chart (Excel) with separate debit and credit opening-balance columns; preview rejects duplicate codes, unknown parents, invalid types. | Must |
| FR-110 | Opening balances post one balanced opening entry. Only while the company is in initial setup / data migration (until the administrator marks migration complete or the first period is closed), an unbalanced opening may be accepted: the difference goes to an Opening balance difference equity account, and a clear warning is shown on the dashboard, trial balance and balance sheet until that account's balance is 0.000. After setup, unbalanced openings are refused. | Must |

#### 7.6.2 Manual Journal Entries
| ID | Requirement | Priority |
|---|---|---|
| FR-111 | Create manual entries (acct.post): date, description, lines (account, debit or credit, cost centre, party, line note); journal sequence; balanced; open period. | Must |
| FR-112 | Edit posted manual entries in an open period with full change history; automatic entries change only through their source document. | Must |
| FR-113 | Void manual entries by reversal with a reason; number kept. | Must |
| FR-114 | Import entries (Excel/CSV) with preview rejecting unbalanced entries, unknown or non-postable accounts, closed periods, missing party on control accounts. | Must |
| FR-115 | Journal list with filters (period, number, account, cost centre, party, manual/automatic, status), Excel/PDF export, printable journal voucher. | Must |

#### 7.6.3 Financial Reports
| ID | Requirement | Priority |
|---|---|---|
| FR-116 | General ledger per account and period: opening, movements, running and closing balance. | Must |
| FR-117 | Trial balance for a period at a chosen tree level: opening, movements, closing in debit/credit columns; mismatch highlighted. | Must |
| FR-118 | Profit & loss for a period grouped by the account tree; optional cost-centre filter. | Must |
| FR-119 | Balance sheet as of a date; current period profit or loss shown within equity. | Must |
| FR-120 | Account statement for any account, optionally by party. | Must |
| FR-121 | Drill-down from any report line to the source entry or document; Excel and PDF export. | Must |

#### 7.6.4 Accounts Payable & Accounts Receivable
| ID | Requirement | Priority |
|---|---|---|
| FR-122 | Accounting parties (suppliers and customers for non-transport documents): name, tax number, phone, address, control account, payment terms; transport customers available as receivable parties. | Must |
| FR-123 | Supplier bills (acct.apar): supplier, date, due date, reference, lines (description, expense/asset account, amount, cost centre), input tax, total; numbered; posts debit expense/asset and sales tax input, credit supplier payable. | Must |
| FR-124 | Receivable documents for non-transport income: customer, date, due date, lines (description, revenue account, amount), output tax, total; numbered and posted. | Must |
| FR-125 | Payments and collections against documents, full or partial, by cash, bank transfer or CliQ; each posts and updates remaining balance. | Must |
| FR-126 | Returns on payable and receivable documents up to the remaining balance; mirror entry for the returned share. | Must |
| FR-127 | Printed documents with amount in words; void by reversal with reason; blocked with linked payments/returns or in a closed period. | Must |
| FR-128 | Payables and receivables ageing by party (not due, 1–30, 31–60, 61–90, over 90) as of a date. | Must |

#### 7.6.5 Banks, Cash Boxes & Cost Centres
| ID | Requirement | Priority |
|---|---|---|
| FR-129 | Cash boxes and bank accounts (acct.setup): name, bank, account number/IBAN, currency, linked ledger account, active. | Must |
| FR-130 | Cash/bank statement: opening, receipts, payments, running and closing balance. | Must |
| FR-131 | Cost centres (code, name, active) selectable on any journal line; cost-centre report of revenues and expenses. | Must |

#### 7.6.6 Periods
| ID | Requirement | Priority |
|---|---|---|
| FR-132 | Period list (one row per month, Open/Closed); close/reopen requires settings.manage and a reason; audited. | Must |
| FR-133 | A closed period blocks every posting, edit, void and import dated in it, across operations and accounting. | Must |
| FR-134 | Pre-close check lists pending postings, unapproved drafts and unreconciled control accounts; user can close after confirming. | Should |

#### 7.6.7 Maintenance Zone
| ID | Requirement | Priority |
|---|---|---|
| FR-135 | Maintenance actions (acct.maint): delete selected unlinked entries; delete all entries of a company; replace the chart of accounts; full reset of the company's accounting data. All maintenance deletions are soft deletions: records are flagged deleted, excluded from all screens, reports and balances, and kept in the database. | Must |
| FR-136 | Protections: typed confirmation word; backup offered first; refused in closed periods and for entries linked to documents; maintenance log (user, time, action, counts). | Must |

### 7.7 Reports & Dashboard

#### 7.7.1 Dashboard
| ID | Requirement | Priority |
|---|---|---|
| FR-137 | Today and month-to-date figures: trips, tonnage, revenue, driver wages, partner costs, margin, invoiced, collections. | Must |
| FR-138 | Alert cards opening filtered lists: driver documents expired/expiring within 30 days (licence, truck registration, insurance, inspection); unbilled trips (count, value, oldest); overdue receivables; drafts awaiting approval; rejected e-invoices; pending postings; trips flagged Needs review / Shortage / Tariff breach; loading sites without current-month rates; opening balance difference (during setup). | Must |
| FR-139 | Cash and bank balance cards, one per cash box and bank account. | Must |
| FR-140 | Pin any saved report with a measure and threshold (normal / warning / exceeded colouring); click opens the report; reorder or remove pins. | Must |
| FR-141 | Cards shown only when permissions allow; figures refresh live. | Must |

#### 7.7.2 Standard Operational Reports
| Report | Contents |
|---|---|
| Trips report | All trips, filters as in FR-009, totals |
| Unbilled trips | By customer, with age and value |
| Shortage report | Flagged trips by driver, loading site, period |
| Tariff breach report | Per FR-024 |
| Driver productivity | Per driver and period: trips, tons, revenue, wage, margin, shortage % |
| Customer revenue | Per customer and period: trips, tons, revenue, discount, net |
| Route & site analysis | Per site pair: trips, tons, revenue, cost, margin |
| Material volume | Tons and revenue per material and period |
| Partner shipments | Per partner: trips, tons, cost, payments, balance |
| Margin analysis | Revenue − wages − partner costs by customer, route, driver or month |
| Monthly summary | Per month: trips, tons, revenue, costs, margin, invoiced, collected |

#### 7.7.3 Standard Financial Reports & Statements
| Report | Contents |
|---|---|
| Customer statement | Opening, invoices, credit notes, receipts, running and closing balance; unapplied receipts shown |
| Driver statement | Per FR-083 |
| Partner statement | Per FR-088 |
| Ledger account statement | Per FR-120 |
| Cash / bank statement | Per FR-130 |
| Receivables ageing | Per customer: not due, 1–30, 31–60, 61–90, over 90; unapplied payments separate |
| Payables ageing | Per FR-128 |
| Driver balances | Open wages, open advances, net due |
| Partner balances | Cost, payments, balance due |
| Registers | Invoices, credit notes, receipts, advances, settlements, partner payments |
| E-invoicing report | Per FR-065 |
| Sub-ledger reconciliation | Per FR-105 |

| ID | Requirement | Priority |
|---|---|---|
| FR-142 | Provide every report in 7.7.2 and 7.7.3 with the listed contents. | Must |

#### 7.7.4 Report Builder
| ID | Requirement | Priority |
|---|---|---|
| FR-143 | Data sources: trips, invoices, credit notes, receipts, advances, settlements, partner payments, journal lines, drivers, customers; custom fields included automatically. | Must |
| FR-144 | Row and optional column grouping (dates by day, month, quarter, year), measures (sum, count, average, min, max), filters, sort, top-N; detail mode. | Must |
| FR-145 | Save as private or shared with the company; shared reports editable by owner or administrator. | Must |
| FR-146 | Run, export (Excel/PDF) and pin saved reports. | Must |
| FR-147 | A data source is available only with its view permission. | Must |

#### 7.7.5 Common Report Behaviour
| ID | Requirement | Priority |
|---|---|---|
| FR-148 | Period or as-of date, relevant filters, selected company only, totals. | Must |
| FR-149 | Drill-down from every line to its source record or a filtered list. | Must |
| FR-150 | Excel and PDF export of every screen and report; printouts carry company header, title, filters, print date and user, page X of Y, RTL for Arabic. | Must |
| FR-151 | Large exports run in the background with a notification when ready. | Should |

### 7.8 Master Data

#### 7.8.1 Drivers (with Truck Details)
| Group | Field | Rule |
|---|---|---|
| Identity | Full name | Required |
| | National ID | Required; 10 digits; unique within the company |
| | Phone 1 / Phone 2 | Phone 1 required; local mobile format |
| | Address | Optional |
| Licence | Licence number, type, expiry date | Required |
| Payment | Payment method | Cash, Bank or CliQ |
| | Bank name, IBAN | Required when Bank; IBAN validated |
| | CliQ alias | Required when CliQ |
| Truck | Plate number | Required; unique within the company |
| | Truck type, trailer number | Optional |
| | Capacity (t) | Required; > 0 |
| | Ownership type | Required; from the ownership-type list |
| | Partner company | Optional; when the truck works for a partner |
| | Registration, insurance, inspection expiry | Required dates |
| Other | Notes, custom fields | As configured |

| ID | Requirement | Priority |
|---|---|---|
| FR-152 | Maintain drivers with the fields above (masters.manage); truck details are part of the driver's profile and edited there. | Must |
| FR-153 | Truck detail changes recorded in driver history (old → new, user, time); saved trips keep the fleet type and partner they had at save. | Must |
| FR-154 | Licence, registration, insurance and inspection expired or expiring within 30 days raise alerts on the dashboard and driver list. | Must |
| FR-155 | Selecting a driver with an expired document on a trip shows a warning naming the document; the trip can still be saved. | Must |
| FR-156 | Driver profile: account summary (open wages, open advances, net due), recent trips, advances, settlements, history. | Must |

#### 7.8.2 Customers
| Field | Rule |
|---|---|
| Name | Required; unique among active customers |
| Tax number | Optional; required for e-invoicing when tax rules demand a buyer identifier |
| Commercial registration / national ID | Optional |
| Phone, email, address, city | Optional; phone validated |
| Contact person | Optional |
| Payment terms (days) | Default 0; used for due dates |
| Notes, custom fields | As configured |

| ID | Requirement | Priority |
|---|---|---|
| FR-157 | Maintain customers; profile shows balance, unbilled trips, open invoices, receipts, tariffs, history. | Must |

#### 7.8.3 Partners
| Field | Rule |
|---|---|
| Name | Required; unique among active partners |
| Tax number, phone, address, contact person | Optional |
| Payment details | Bank name, IBAN or CliQ alias; optional |
| Notes | Optional |

| ID | Requirement | Priority |
|---|---|---|
| FR-158 | Maintain partners; profile shows linked drivers, partner trips, payments, balance, tariffs, history. | Must |

#### 7.8.4 Materials & Sites
| ID | Requirement | Priority |
|---|---|---|
| FR-159 | Materials: name (unique), unit (default ton), active. | Must |
| FR-160 | Loading sites and unloading sites as two separate lists: name (unique in its list), area/city, active. | Must |

#### 7.8.5 Ownership Types
| ID | Requirement | Priority |
|---|---|---|
| FR-161 | Ownership-type list (e.g. Company, Owner-driver, Rented), each classified own fleet or external fleet; the built-in Company type (own fleet) cannot be deleted or reclassified. | Must |

#### 7.8.6 Common Master-Data Rules
| ID | Requirement | Priority |
|---|---|---|
| FR-162 | Referenced records cannot be deleted; refusal lists references with counts and offers Archive; archived records hidden from new-entry lists but kept in history, reports and filters; restorable. | Must |
| FR-163 | Rename cascade by identifier; already-issued printed documents keep the name they were issued with. | Must |
| FR-164 | Import/export of every master-data list (Excel/CSV) with column matching, preview and duplicate detection on unique keys. | Must |
| FR-165 | Lists with search, filters (active/archived and key fields), sort, configurable columns, Excel/PDF export. | Must |
| FR-166 | History of every master-data record (created, edits old → new, archived, restored) with user and time. | Must |
| FR-167 | The trip form selects only existing active master data; customers, materials and sites cannot be created from the trip form and are added only from their master-data screens. | Must |

### 7.9 Administration & Configuration

#### 7.9.1 Company Settings
| Group | Settings |
|---|---|
| Company profile | Name, tax number, commercial registration, address, phones, email, logo (printed on documents) |
| Market | Country, base currency and decimals, sales-tax rate, tax inclusive/exclusive, phone and IBAN validation formats |
| Operations | Bill on unloaded weight (default on); Pay driver on loaded weight (default off); Shortage tolerance % (default 0.5); Lock tariffs (default off); Invoice approval (default on); Accrue driver wage on trip save (default on) |
| Numbering | Prefix and digits per sequence (invoices, credit notes, receipts, advances, settlements, partner payments, journal entries); next number shown |
| E-invoicing | Per FR-059 |
| Accounting | Account mapping (7.5.1), periods (7.6.6), setup/migration status (FR-110) |

| ID | Requirement | Priority |
|---|---|---|
| FR-168 | Settings screen (settings.manage) with the groups above; every change audited (old → new). | Must |
| FR-169 | A sequence's next number can never be set at or below an issued number; prefix changes apply to new documents only. | Must |
| FR-170 | Changing the weight-basis settings applies to new trips only, after a confirmation naming the effect; saved trips keep their amounts. | Must |
| FR-171 | Administrator can mark Data migration complete, disabling the opening-balance difference mechanism; irreversible. | Must |

#### 7.9.2 Users & Roles
| ID | Requirement | Priority |
|---|---|---|
| FR-172 | Invite users by email (users.manage) with a role per company; link expires after 7 days; resend or cancel. | Must |
| FR-173 | Preset or custom roles; presets cannot be deleted; a custom role in use cannot be deleted until users are reassigned. | Must |
| FR-174 | Edit, disable/enable, unlock, send password reset, archive; BR-001 to BR-003 apply; all audited. | Must |
| FR-175 | Password policy: minimum 8 characters, letters and digits (configurable per tenant); 5 failed sign-ins → 15-minute lock. | Must |
| FR-176 | Inactive sessions expire after a tenant-configurable time; users see active sessions and sign out of all; administrators can sign out any user. | Must |
| FR-177 | Two-factor authentication (authenticator app) is mandatory for every user, including provider staff; it is set up at first sign-in and cannot be disabled. Recovery codes are issued at set-up; resetting a user's 2FA requires users.manage and is audited. | Must |
| FR-178 | User preferences: language (Arabic/English), default company, date format. | Must |

#### 7.9.3 Field Configuration
| ID | Requirement | Priority |
|---|---|---|
| FR-179 | Custom fields for trips, drivers, customers: text, number, date, list, checkbox, long text; label (Arabic/English), options, default, help, required, show-as-column, order. | Must |
| FR-180 | Custom fields appear in forms, quick-entry row (when a column), lists, exports, imports, invoice annex (trips) and report builder. | Must |
| FR-181 | A custom field with data can only be hidden; its type cannot change. | Must |
| FR-182 | Standard fields: rename, hide, require, reorder; core trip fields renamable but never hidden; required field blocks saving with "Field «X» is required". | Must |
| FR-183 | Administrator sets default trip-table columns and order; each user can save a personal column view. | Must |
| FR-184 | Preview of the configured form before saving. | Should |

#### 7.9.4 Collaboration & Search
| ID | Requirement | Priority |
|---|---|---|
| FR-185 | Changes appear for other users of the same company within a few seconds without refresh. | Must |
| FR-186 | Record locking (BR-039); others see "X is editing this record" and can view read-only. | Must |
| FR-187 | Global search (keyboard shortcut) across receipt and document numbers, drivers (name, national ID, plate, phone), customers, partners; Arabic spelling variants tolerated; results respect permissions. | Must |

#### 7.9.5 Audit Log & Notifications
| ID | Requirement | Priority |
|---|---|---|
| FR-188 | Company audit log of every add, edit, void, archive, restore, setting and permission change, period close/reopen, maintenance action, import, export, sign-in and failed sign-in, with user, time, record and old → new values. | Must |
| FR-189 | Audit log read-only for all users including administrators; filter by user, action, record type, period; exportable. | Must |
| FR-190 | In-app notifications: rejected e-invoices, finished exports, failed postings, accepted invitations, support-access requests. | Should |

## Section 8 — Business Rules & Calculations

### 8.1 Money & Rounding
- BR-020 — Amounts are calculated in the currency's smallest unit (fils for JOD; 1 JOD = 1,000 fils) and shown with the currency's decimals (3 for JOD).
- BR-021 — Rounding is half-up to the currency's decimals, applied at each calculated amount (trip revenue, wage, discount share, tax), never only at the final total.
- BR-022 — Sales tax is exclusive by default, 16 % for the launch market; rate and inclusive/exclusive mode are company settings.
- BR-023 — Tax is calculated once on the invoice base: tax = round(base × rate).
- BR-024 — Every voucher prints its amount in Arabic words using the currency's main and fractional units.

### 8.2 Trip Calculations
| Item | Formula |
|---|---|
| Billing quantity | Unloaded weight if Bill on unloaded weight is on (default) and recorded; otherwise loaded weight |
| Wage quantity | Loaded weight if Pay driver on loaded weight is on; otherwise unloaded weight (default), falling back to loaded |
| Revenue | Billing quantity × customer price |
| Driver wage | Wage quantity × driver rate (0 for partner trips) |
| Partner cost | Wage quantity × partner rate (partner trips) |
| Margin | Revenue − driver wage − partner cost |
| Shortage | Loaded − unloaded |
| Shortage % | Shortage ÷ loaded × 100; flagged above tolerance (default 0.5 %) |
| Tariff breach | (Price − tariff price) × billing quantity; negative = loss, positive = excess |

Example: loaded 32.000 t, unloaded 31.850 t, price 6.000, driver rate 3.500 → revenue 31.850 × 6.000 = 191.100 JOD; wage 31.850 × 3.500 = 111.475 JOD; margin 79.625 JOD; shortage 0.150 t = 0.47 % (within tolerance).

### 8.3 Invoice & Credit Note Calculations
| Item | Formula |
|---|---|
| Subtotal | Σ revenue of invoiced trips (or Σ quantity × unit price for manual lines) |
| Discount | 0 ≤ discount ≤ subtotal |
| Extra fees | ≥ 0 |
| Base | Subtotal − discount + extra fees |
| Tax | round(base × tax rate) |
| Total | Base + tax |
| Remaining balance | Total − active receipts − active credit notes |

Example: 4 trips totalling 720.000; discount 20.000; fees 5.000 → base 705.000; tax 112.800; total 817.800 JOD.
- BR-025 — Returned trips carry a share of the invoice's discount and fees in proportion to their revenue; tax follows the invoice's rate.
- BR-026 — A credit note cannot exceed the invoice's remaining balance.
- BR-027 — A credit note returning everything still open absorbs any rounding difference, leaving the remaining balance at exactly 0.000.

Credit-note example (one trip of 180.000 returned from the invoice above): share 25 %; discount share 5.000; fees share 1.250; base 176.250; tax 28.200; total 204.450 JOD.

### 8.4 Pricing Resolution
- BR-028 — Tariffs: most specific matching line wins (party 8, material 4, loading site 2, unloading site 1); ties → latest Valid from.
- BR-029 — Driver rate: company + loading site + month of loading date; fleet type from the driver's ownership type (Company → own-fleet rate; any other → external-fleet rate); resolution per FR-030.
- BR-030 — The driver rate is frozen into the trip at save; tariff or monthly-rate changes never alter saved trips.

### 8.5 Driver Settlement
| Item | Formula |
|---|---|
| Gross wages | Σ wages of the driver's open active trips |
| Advances | Σ the driver's open active advances |
| Deductions | Entered at settlement (shortage, fines, other), each ≥ 0 |
| Net payable | Gross − advances − deductions; must be ≥ 0 |

Example: gross 1,250.000 − advances 400.000 − deductions 35.000 = net 815.000 JOD.
- BR-031 — A settlement includes all, or a user-selected subset, of the driver's open items; an item belongs to only one active settlement.
- BR-031a — When Accrue driver wage on trip save is off, trips post no wage entry and the settlement debits driver wage expense (gross) instead of driver payable.
- BR-032 — A settled advance cannot be edited or voided until its settlement is voided.

### 8.6 Automatic Journal Entries
Each posting key is mapped to an account (7.5.1); invoicing and posting are disabled until all required keys are mapped.
| Event | Debit | Credit | Notes |
|---|---|---|---|
| Trip saved (own/external fleet) | Driver wage expense | Driver payable | Only when wage accrual is on |
| Trip saved (partner) | Partner cost expense | Partner payable | Always |
| Invoice approved | Trade receivables (total); Discount allowed (discount) | Transport revenue (subtotal); Other revenue (fees); Sales tax output (tax) | Manual invoices use the same keys |
| Credit note | Mirror of the invoice entry for the returned share | | Linked to the invoice |
| Customer receipt | Cash / bank | Trade receivables | Against an invoice or on account |
| Driver advance | Driver advances | Cash / bank | |
| Driver settlement (accrual on) | Driver payable (gross) | Driver advances; Driver deductions; Cash/bank (net) | Net ≥ 0 |
| Driver settlement (accrual off) | Driver wage expense (gross) | Driver advances; Driver deductions; Cash/bank (net) | Net ≥ 0 |
| Partner payment | Partner payable | Cash / bank | ≤ partner balance due |
| Supplier bill | Expense/asset accounts; Sales tax input | Supplier payable (total) | FR-123 |
| Payable return | Supplier payable | Expense/asset accounts; Sales tax input | FR-126 |
| Supplier payment | Supplier payable | Cash / bank | FR-125 |
| Receivable document | Customer receivable (total) | Revenue accounts; Sales tax output | FR-124 |
| Receivable return | Revenue accounts; Sales tax output | Customer receivable | FR-126 |
| Collection | Cash / bank | Customer receivable | FR-125 |
| Opening balances | Accounts with debit openings | Accounts with credit openings | FR-110; difference to Opening balance difference during setup only |
| Void of any event | Mirror lines | Mirror lines | New linked entry; original kept |

- BR-033 — Every entry must balance; an unbalanced entry is refused.
- BR-034 — An entry is dated with its source document's date and refused if that date is in a closed period.
- BR-035 — Editing a trip or advance reverses its entry and posts a new one; the original is never changed in place.

### 8.7 Periods, Numbering & Locks
- BR-036 — A period is one calendar month; a closed period allows no posting, edit, void or import in operations or accounting.
- BR-037 — Each company has separate sequences for invoices, credit notes, receipts, advances, settlements, partner payments, journal entries, supplier bills, receivable documents, AP/AR payments and returns, each with a configurable prefix (e.g. TRP-00001, STL-00001).
- BR-038 — Numbers are assigned by the server at approval, gap-free and unique even under concurrent issuing; drafts have no number; voided documents keep their number.
- BR-038a — A sequence's next number can never be set at or below an issued number.
- BR-039 — Opening a record for editing locks it for others; the lock is released on close or after 90 seconds without activity.

### 8.8 Referential Integrity
- BR-040 — A record cannot be deleted while referenced; the refusal lists each referencing type with its count and offers Archive or Void.
- BR-041 — Soft deletion for all entities: every delete in the system (master data, drafts, configuration, users, maintenance actions, tenant deletion) is a soft delete. The record is flagged as deleted with the deleting user, time and reason, is excluded from all screens, lists, searches, reports and balances, and remains in the database. No record is hard-deleted. Administrators can restore soft-deleted records where business rules allow; every delete and restore is audited.
| Record | Referenced by |
|---|---|
| Customer | Trips, invoices, receipts, credit notes, tariffs |
| Driver | Trips, advances, settlements |
| Partner | Trips, drivers, partner payments, tariffs |
| Material / site | Trips, tariffs, monthly site rates |
| Ownership type | Drivers |
| Invoice | Active receipts, active credit notes |
| Trip | Invoice, settlement |
| Advance | Settlement |
| Ledger account | Entry lines, documents, payments, account mapping |
| Journal entry | Linked invoices, AP/AR documents, payments, returns |

### 8.9 Validation Rules
| Field | Rule |
|---|---|
| Receipt number | Unique within the company (ignores case and spaces) |
| Trip and loading dates | Not in the future; not in a closed period |
| Loaded weight | > 0; warning above the driver's truck capacity |
| Unloaded weight | Warning above loaded × 1.02 |
| Driver national ID | 10 digits; unique within the company |
| Truck plate | Unique within the company |
| Phone | Local mobile format (launch market: 07[789] or 06 + 7 digits) |
| IBAN | Country format (launch market: JO + 28 characters) |
| CliQ alias | Required when payment method is CliQ |
| Customer name | Unique among active customers |
| Receipt against invoice | ≤ invoice remaining balance |
| Credit note | ≤ invoice remaining balance |
| Settlement | Net ≥ 0 |
| Partner payment | ≤ partner's current balance due |
| Opening balances | Must balance once data migration is marked complete |
| Driver documents | Expired document → warning only; trip not blocked |
Phone and IBAN formats are configurable per market.

## Section 9 — User Flows

### 9.1 Sign-up & First-time Setup
1. Owner signs up (name, email, password, organisation), verifies the email and sets up mandatory two-factor authentication.
2. Platform creates tenant, Owner account and first company; trial starts.
3. Setup checklist shows the eight steps with status Not started / In progress / Done.
4. Company profile: name, tax number, address, logo.
5. Market settings: currency, tax rate, e-invoicing credentials (optional).
6. Chart of accounts: transport template or import; enter or import opening balances.
7. Account mapping: map every posting key (invoicing disabled until complete).
8. Master data: drivers (with truck details), customers, partners, materials, loading and unloading sites.
9. Prices: customer and partner tariffs; current month's driver rates for every loading site.
10. Users: invite staff with a role per company.
11. When historical data is loaded, mark Data migration complete.
Alternatives/errors: unbalanced openings during setup → Opening balance difference with persistent warning; after migration → refused. Missing mapping key → banner "Complete account mapping (N missing)". Duplicate national ID or plate on import → row rejected with reason.

### 9.2 Month-start: Enter Monthly Driver Rates
1. Pricing → Monthly site rates; new month shows No rates.
2. Select the month; all active loading sites listed.
3. Enter own-fleet and external-fleet rates; save; month becomes Partial or Complete.
4. New trips of that month take rates automatically.
Alternatives/errors: trip before rates → blocked naming site, month, fleet type (manual rate possible with masters.manage); editing a used month → warning that only new trips use new rates; clearing both rates of a used site → refused; dashboard warning while sites lack rates.

### 9.3 Record a Trip (Quick Entry)
1. Quick-entry row pre-filled with the user's last driver, customer, material, route.
2. Type receipt number; duplicates flagged immediately.
3. Select driver; truck (plate, type, capacity) shown; fleet type and partner from the driver.
4. Enter dates and weights.
5. Customer price from tariff and driver rate from monthly site rate, sources shown.
6. Enter saves: validation, rate frozen, wage accrual posted, trip added to recent list with History and Edit.
7. Row clears only receipt number and unloaded weight.
Alternatives/errors: partner trip → partner rate, wage 0, partner cost posted; tariff deviation → Tariff breach flag (blocked under tariff lock without masters.manage); overweight → warning; expired driver document → warning, save allowed; closed-period or future date → blocked.

### 9.4 Import Trips
1. Trips → Import: upload Excel/CSV or paste.
2. Automatic column matching; adjustable.
3. Preview: valid, rejected (with reasons), duplicates.
4. Choose strict or tolerant mode; commit.
5. Entries posted; tolerant rows flagged Needs review; import logged.
Errors: no monthly rate and no rate column → row rejected; unknown driver, customer or site → row rejected (master data not created from import).

### 9.5 Edit or Void a Trip
1. Open the trip from the recent or Created Trips list; history visible.
2. Edit: save → wage entry reversed and re-posted; rate re-resolved only if loading site, month or driver changed.
3. Void: reason → reversal posted, status Void.
Blocked when invoiced, settled or in a closed period, with guidance (e.g. "Return or void invoice TRP-00045 first").

### 9.6 Invoice a Customer
1. Invoicing → New → Trip-based; select customer.
2. Filter and select unbilled trips; live totals.
3. Discount, extra fees, due date, notes; choose layout.
4. Save → Draft (approval on by default): no number, no entry, trips reserved.
5. Review and print draft (DRAFT stamp).
6. Approve → gap-free number, entry posted, trips invoiced, queued for e-invoicing.
7. Print claim statement with annex, or tax invoice, with QR code.
Alternatives/errors: Save & approve (inv.issue); manual invoice with free lines; deleting a draft releases trips; approval blocked when mapping incomplete, closed period, trip no longer unbilled, or total ≤ 0.

### 9.7 E-invoicing Submission
1. On approval the invoice is validated; missing fields listed.
2. Submitted automatically: Queued → Submitted.
3. Response stored (unique ID, system number, QR); QR printed.
Errors: validation fails → stays Queued with fields to fix; Rejected → reason shown, fix and resubmit; technical failure → retries, then Rejected/Failed list and dashboard alert; credit note submitted only after the original is accepted.

### 9.8 Return (Credit Note) or Void an Invoice
1. Approved invoice → Return; select trips/lines; reason.
2. Proportional discount, fees and tax calculated, capped at remaining balance.
3. Confirm → numbered, posted, returned trips unbilled, submitted for e-invoicing.
4. Void instead only with no active receipts or credit notes and not accepted by e-invoicing; otherwise full credit note.

### 9.9 Record a Customer Receipt
1. Receipts → New; select customer.
2. Optionally select an open invoice (remaining balance shown).
3. Amount, method, cash box/bank, reference, attachments count.
4. Save → numbered, posted, invoice balance updated; print receipt voucher.
Errors: amount above remaining balance → blocked; no invoice → on-account, shown as unapplied.

### 9.10 Driver Advance & Settlement
Advance: Drivers → driver → New advance; amount, method, cash box/bank; save → posted; print payment voucher.
Settlement:
1. Drivers → driver → Settle; choose date.
2. Open trips and advances up to the date pre-selected; untick allowed.
3. Add deductions; net calculated.
4. Method and cash box/bank → Confirm.
5. Numbered, items linked and locked, entry posted; print statement and payment voucher.
Errors: net < 0 → blocked; closed period → blocked; void settlement with reason → items released, reversal posted.

### 9.11 Partner Payment
1. Partners → partner; current balance due shown.
2. New payment: amount, method, cash box/bank → save → posted; print voucher.
Error: amount above balance due → blocked with balance shown.

### 9.12 Month-end Close
1. Accounting → Periods → month → Close.
2. Pre-close check lists pending postings, unapproved drafts, unreconciled control accounts.
3. Resolve or confirm; reason; period Closed.
4. Nothing dated in that month can be posted, edited, voided or imported.
Alternative: Reopen requires settings.manage and a reason; audited.

### 9.13 Concurrent Work
- Second user opening a record sees "X is editing this record" (read-only); lock released on close or after 90 seconds idle.
- Simultaneous approvals receive distinct consecutive server numbers; no gaps or duplicates.
- Changes appear on other screens within a few seconds.

### 9.14 Configure Fields (Administrator)
1. Settings → Field configuration → Trips, Drivers or Customers.
2. Add a custom field, or rename, hide, require, reorder standard fields.
3. Preview → Save; forms, lists, exports, imports and report builder update.
Errors: hiding a core trip field → refused; deleting a field with data → refused, Hide offered; type change with data → disabled.

## Section 10 — Non-Functional Requirements

### 10.1 Quality Targets
| ID | Area | Requirement |
|---|---|---|
| NFR-01 | Performance | Screens and saves under 1 second; monthly reports under 3 seconds; large exports as background jobs. |
| NFR-02 | Capacity | At least 25 concurrent users and 500,000 trips per company without slowing down; horizontal scaling by adding instances. |
| NFR-03 | Availability | At least 99.5 % monthly uptime; planned maintenance announced 48 hours ahead; zero-downtime releases. |
| NFR-04 | Data safety | A document and its journal entry saved in one database transaction; repeated submits idempotent, never duplicates. |
| NFR-05 | Backup & recovery | Automated encrypted backups; RPO ≤ 15 minutes, RTO ≤ 4 hours; restore tested quarterly. |
| NFR-06 | Security | TLS in transit, encryption at rest; tenant/company isolation and permissions enforced server-side on every request (Section 14). |
| NFR-07 | Usability | Arabic-first RTL with English; full keyboard use on data-entry screens; clear field-specific error messages. |
| NFR-08 | Compatibility | Latest Chrome, Edge, Firefox, Safari; correct A4 Arabic printing and PDF; Excel files open correctly in Excel and Google Sheets. |
| NFR-09 | Multi-market | A new market needs only configuration and an e-invoicing connector; no change to business flows. |
| NFR-10 | Records retention | Audit log and financial records kept at least 7 years (or as tax rules require); unalterable. |

### 10.2 Technology Constraints
| ID | Area | Requirement |
|---|---|---|
| NFR-11 | Platform | Backend on the latest stable .NET release — .NET 10 (LTS) at the time of writing — with the latest C# and ASP.NET Core. |
| NFR-12 | Frontend | Web interface built with Blazor on the latest .NET release (Blazor Web App, .NET 10), supporting Arabic RTL and English. |
| NFR-13 | Database | Latest Microsoft SQL Server release (SQL Server 2025), accessed through the latest Entity Framework Core with versioned migrations. |
| NFR-14 | Architecture | Layered (clean) architecture: domain, application, infrastructure, API/UI; Section 8 business rules in domain/application layers, not in UI or database. |
| NFR-15 | API | All features exposed through a versioned API documented with OpenAPI. |
| NFR-16 | Real-time | Live updates and record locks (FR-185, FR-186) via SignalR. |
| NFR-17 | Background jobs | E-invoicing, exports, notifications and retries as background jobs that never block data entry. |
| NFR-18 | Observability | Structured logging, health checks and metrics; no secrets or personal data in logs. |
| NFR-19 | Testing | Every Section 8 business rule covered by automated unit and integration tests on every build (Section 15). |
| NFR-20 | Single Master Database | The system uses one shared SQL Server database — the Master Database — which is also used by the other projects in the same product family. All tenants and companies share it; isolation is enforced by TenantId and CompanyId on every row (SEC-06). This system's tables live in their own database schema, and shared entities (tenants, users, companies) are reused from the Master Database rather than duplicated. |
| NFR-21 | Soft deletion | All entities support soft deletion through a global query filter; no hard deletes (BR-041). |
| NFR-22 | Hosting | Hosting provider and data residency are decided by the product owner after the build; the system must stay deployable to any provider (container-ready, configuration-driven, no provider-specific services in business code). |

## Section 11 — Logical Data Model

> The physical design (table names with the TRN_ prefix, schemas, columns, keys) is defined in `claude/Database_Design.md`, which takes precedence over this section where they differ.

### 11.1 General Conventions
- Every business record carries TenantId and CompanyId, a unique Id, and created/updated user and time stamps.
- Money stored as decimal with 3 decimals (decimal(18,3) in SQL Server); weights 3 decimals; rates and prices 4 decimals.
- Posted financial records are never deleted; they change status (Void / Archived) and keep their links.
- Soft deletion: every entity carries IsDeleted, DeletedBy, DeletedAt and DeleteReason; all queries exclude soft-deleted rows by default (global query filter); no row is ever hard-deleted.
- Public identifiers: every entity exposed in URLs has a hashed, non-sequential public identifier used instead of its internal key (SEC-22).
- Every editable record carries a version stamp for concurrent-edit detection.

### 11.2 Platform & Access
| Entity | Key fields | Relationships |
|---|---|---|
| Tenant | Name, status, plan, trial end, renewal date | Has Companies, Users, subscription invoices |
| Plan | Name, price, billing cycle, limits | Used by Tenants |
| Company | Name, tax number, registration, address, logo, currency, decimals, tax rate, tax mode, time zone, migration-complete flag | Belongs to Tenant; owns all business data |
| User | Name, email, password hash, language, status, 2FA enrolment and recovery codes (hashed), last sign-in, failed attempts, locked until | Belongs to Tenant; has company roles |
| Role | Name, preset/custom, permission keys | Belongs to Tenant |
| UserCompanyRole | User, Company, Role | Role per user per company |
| SupportAccessGrant | Requested by, approved by, start, end, reason | Belongs to Tenant |

### 11.3 Master Data
| Entity | Key fields | Relationships |
|---|---|---|
| Driver | Name, national ID, phones, address, licence no./type/expiry, payment method, bank, IBAN, CliQ alias, status, custom values; truck: plate, type, trailer no., capacity, ownership type, registration/insurance/inspection expiry | Exactly one truck per driver (stored in the driver record); optional Partner; has Trips, Advances, Settlements |
| OwnershipType | Name, fleet class (Own/External), built-in flag | Used by Drivers |
| Customer | Name, tax number, registration/ID, contacts, payment terms, status, custom values | Has Trips, Invoices, Receipts, Credit notes |
| Partner | Name, tax number, contacts, payment details, status | Has Drivers, Trips, Partner payments |
| Material | Name, unit, status | Used by Trips, Tariffs |
| LoadingSite / UnloadingSite | Name, area, status | Used by Trips, Tariffs; loading sites by monthly rates |

### 11.4 Pricing
| Entity | Key fields | Relationships |
|---|---|---|
| Tariff | Party type, party/All, material/All, loading site/All, unloading site/All, price, valid from/to, status | Resolves customer price / partner rate |
| MonthlySiteRate | Month, loading site, own-fleet rate, external-fleet rate | Unique per company + month + site |
| RateHistory | Reference, old values, new values, user, time | History of tariffs and monthly rates |

### 11.5 Operations
| Entity | Key fields | Relationships |
|---|---|---|
| Trip | Receipt no., trip/loading/unloading dates, driver, customer, sites, material, partner, weights, customer price, tariff price, driver rate, rate source, rate month, partner rate, fleet type, truck plate at save, revenue, wage, partner cost, shortage, flags, status, notes, custom values | Belongs to Driver, Customer, sites, Material, optional Partner; optional Invoice and Settlement; has Journal entries |
| ImportBatch | Entity type, file name, user, time, row counts, mode | Has imported records |

### 11.6 Invoicing & Cash
| Entity | Key fields | Relationships |
|---|---|---|
| Invoice | Number, type, customer, date, due date, subtotal, discount, fees, base, tax rate, tax, total, paid, returned, remaining, status, layout, notes | Has InvoiceLines, Receipts, Credit notes, E-invoice record, Journal entries |
| InvoiceLine | Trip or description/qty/unit price, amount | Belongs to Invoice |
| CreditNote | Number, invoice, date, reason, subtotal, discount share, fees share, tax, total, status | Belongs to Invoice; has lines, Journal entry, E-invoice record |
| EInvoiceSubmission | Document, status, unique ID, system number, QR, response, attempts, last error | Belongs to Invoice or Credit note |
| Receipt | Number, customer, optional invoice, date, amount, method, cash box/bank, reference, attachments count, status | Has Journal entry |
| Advance | Number, driver, date, amount, method, cash box/bank, reference, status | Optional Settlement link |
| Settlement | Number, driver, date, gross, advances, deductions, net, method, cash box/bank, status | Links Trips and Advances; has deduction lines and Journal entry |
| PartnerPayment | Number, partner, date, amount, method, cash box/bank, reference, status | Has Journal entry |
| InvoiceLayout | Layout type, title, band text, grouping, columns, labels, footer, signatures | One set per Company |

### 11.7 Accounting
| Entity | Key fields | Relationships |
|---|---|---|
| Account | Code, name AR/EN, type, parent, postable, opening debit/credit, status | Tree; used by entry lines, mapping, cash boxes/banks |
| PostingKeyMapping | Posting key, account | One per key per Company |
| CostCentre | Code, name, status | Used by entry lines |
| CashBox / BankAccount | Name, bank, account no./IBAN, linked account, status | Used by cash documents |
| JournalEntry | Number, date, period, description, source type/id, manual/automatic, status, reverses/reversed by | Has EntryLines |
| EntryLine | Account, debit, credit, cost centre, party type/id, note | Belongs to JournalEntry |
| PendingPosting | Source document, reason, attempts | Awaits retry |
| Period | Month, status, closed by/at, reason | One per month per Company |
| AP/AR Party | Name, tax number, contacts, control account, terms | Has AP/AR documents |
| APDocument / ARDocument | Number, party, date, due date, lines, tax, total, paid, returned, remaining, status | Have lines, payments, returns, Journal entries |
| APARPayment / APARReturn | Number, document, date, amount, method, cash box/bank, status | Belong to an AP/AR document |

### 11.8 Configuration & Audit
| Entity | Key fields | Relationships |
|---|---|---|
| CompanySetting | Key, value | Belongs to Company |
| NumberSequence | Document type, prefix, digits, next number | One per type per Company; server-incremented |
| CustomFieldDefinition | Entity, label AR/EN, type, options, default, help, required, show-as-column, order, hidden | Values stored with owning record |
| StandardFieldConfig | Entity, field, label, hidden, required, order | Belongs to Company |
| SavedReport | Name, source, design, owner, shared flag | Can be pinned |
| DashboardPin | User, report, measure, threshold | Belongs to User |
| RecordLock | Record, user, acquired at, last activity | Released on close or 90 s idle |
| AuditLog | Time, user, company, action, record type/id, old/new values | Append-only, read-only |
| MaintenanceLog | Time, user, action, counts | Read-only |
| Notification | User, type, message, link, read flag | Belongs to User |

### 11.9 Key Uniqueness Rules (per Company)
Trip receipt number; driver national ID; truck plate; active customer name; active partner name; material name; site name within its list; account code; document number per sequence; monthly rate per month + loading site.

## Section 12 — Integrations & API

### 12.1 External Integrations
| ID | System | Direction | Purpose | Key requirements |
|---|---|---|---|---|
| INT-01 | National e-invoicing system (launch market: JoFotara) | Outbound + response | Submit approved invoices and credit notes; receive unique ID, system number, QR | Server-to-server in the required format (UBL 2.1 for the launch market); credentials per company, encrypted; test and production environments; automatic retry and rejection handling; connector per company (FR-059 to FR-066) |
| INT-02 | Payment gateway | Outbound + webhook | Subscription payments (card / bank transfer) | Card data never stored; results via secure webhook; updates tenant status (5.2) |
| INT-03 | Email service | Outbound | Verification, invitations, password reset, billing notices, notifications | Verified sender domain; Arabic and English templates; delivery failures logged |
Payment-gateway and email providers are selected by the product owner after the build. The system integrates through provider-neutral interfaces (payment, email) so a provider can be plugged in by configuration without changing business code.

### 12.2 Platform API (internal)
| ID | Requirement |
|---|---|
| API-01 | Every Section 7 feature is available through the platform's REST API, used only by the platform's own Blazor interface. No public API for tenants' external systems in this release. |
| API-02 | Versioned (e.g. /api/v1) and documented with OpenAPI for the development team. |
| API-03 | Every request authenticated and scoped to one tenant and one selected company; permissions (3.4) checked on every call. |
| API-04 | Lists support paging, sorting, filtering; responses never expose other tenants' identifiers. |
| API-05 | Create and approve calls accept an idempotency key (NFR-04). |
| API-06 | Standard error format (problem details) with message in the user's language, field name and error code. |
| API-07 | Rate limiting per tenant. |

### 12.3 Data Exchange (Files)
| ID | Exchange | Format |
|---|---|---|
| DX-01 | Import of trips, master data, chart of accounts, journal entries | Excel (.xlsx), CSV |
| DX-02 | Export of lists and reports | Excel, PDF |
| DX-03 | Journal export batches (FR-104) | JSON, CSV |
| DX-04 | Full company data export (5.6) | Excel + JSON package |

### 12.4 Real-time Channel
| ID | Requirement |
|---|---|
| RT-01 | SignalR pushes record changes, lock status and notifications to connected users of the same company only. |
| RT-02 | On connection loss the interface reconnects automatically and refreshes changed data. |

## Section 13 — UI/UX Requirements

### 13.1 Design Principles
| ID | Principle |
|---|---|
| UX-01 | Built for daily data entry: dense, calm business interface; no decorative effects (glass, heavy shadows, animations) that slow reading or entry. |
| UX-02 | Arabic first: full RTL with mirrored icons and navigation; English switches to LTR; numbers readable in both directions. |
| UX-03 | Keyboard first on entry screens: Tab/Enter through every form; shortcuts for common actions; focus always visible. |
| UX-04 | One look across the product: shared components behave the same everywhere. |
| UX-05 | Status never by colour alone: every badge carries text and an icon. |

### 13.2 Visual Foundations
| ID | Requirement |
|---|---|
| UX-06 | Brand colours follow the Direct Service light theme: Primary #0F4C5C (buttons, links, active states), Primary dark #0A3540 (headers, hover), Primary tint #E4EDEE (selected rows, highlights), Text #171A1C, Secondary text #5B6570, Borders #E3E7E6, Page background #F6F8F7, Surfaces/cards #FFFFFF; plus semantic success, warning, error and info colours. All text meets 4.5:1 contrast. The Direct Service logo appears in the app shell, sign-in page and printed documents. |
| UX-07 | Typefaces follow the Direct Service brand: IBM Plex Sans Arabic for Arabic and Inter for English (both open licence, weights 300–700, tabular figures for amounts and weights); fallback Segoe UI, Tahoma, then system sans-serif. Base size 14–16 px, line height 1.5. |
| UX-08 | Density: compact spacing scale (8–32 px) for tables and forms, still comfortable to click. |
| UX-09 | Icons: one consistent SVG icon set, never emoji; icon-only buttons have screen-reader labels and tooltips. |
| UX-10 | Light theme only in this release. |

### 13.3 Layout & Navigation
| ID | Requirement |
|---|---|
| UX-11 | App shell: side navigation (right in Arabic) grouped Operations, Billing & Cash, Accounting, Reports, Master Data, Settings; top bar with company switcher, global search, notifications, user menu. |
| UX-12 | Breadcrumbs and page titles; predictable back/forward; every record and filtered list has its own URL, using hashed record identifiers (SEC-22). |
| UX-13 | Menu items without permission hidden; direct access shows a clear no-access message. |
| UX-14 | Designed for ≥ 1280 px; tablets supported; phones for viewing and approvals; no horizontal page scroll (wide tables scroll inside their frame). |

### 13.4 Common Components
| ID | Component | Requirement |
|---|---|---|
| UX-15 | Data tables | Sticky header, sorting, column filters, configurable/resizable columns, server-side paging, totals row, multi-select with bulk actions where allowed, Excel/PDF export, empty and loading states. |
| UX-16 | Forms | Visible labels, required marks, helper text; errors next to the field plus a summary on save; numeric inputs right-aligned with fixed decimals; searchable dropdowns for drivers, customers, sites. |
| UX-17 | Calculation panel | Live totals (revenue, wage, margin, tax, net) and the source of each price or rate on trip, invoice and settlement forms. |
| UX-18 | Dialogs | Confirmation for void, approve, close period, maintenance; required reason where rules require; typed confirmation word for maintenance. |
| UX-19 | Feedback | Toast after save; loading indicator over 300 ms; background-job progress; banners for blocked states (mapping incomplete, period closed, read-only tenant). |
| UX-20 | Lock and live indicators | "X is editing this record" bar, read-only mode, subtle highlight when another user changes a visible row. |
| UX-21 | History panel | Side panel on every record with its history and links. |

### 13.5 Screen Inventory
| Group | Screens |
|---|---|
| Start | Sign-in, sign-up, email verification, password reset, setup checklist, dashboard |
| Operations | Trips (quick-entry row, recent trips, Created Trips list), trip form, trip import, shortage view |
| Billing & Cash | Invoices (list, trip picker, manual invoice, draft/approved view), credit notes, e-invoicing status, receipts, driver accounts (advances, settlements), partner accounts (payments) |
| Accounting | Chart of accounts, journal (list, entry form, import), automatic entries and pending postings, general ledger, trial balance, profit & loss, balance sheet, account statement, accounts payable, accounts receivable, banks and cash boxes, cost centres, periods, maintenance zone |
| Reports | Operational reports, financial statements and ageing, report builder, saved reports |
| Master Data | Drivers (with truck details), customers, partners, materials, loading sites, unloading sites, ownership types, tariffs, monthly site rates |
| Settings | Company settings, account mapping, numbering, e-invoicing, invoice layout designer, field configuration, users and roles, audit log |
| Tenant | Subscription and billing, companies, data export |
| Provider console | Tenants, plans, support access, announcements, platform health |

### 13.6 Printed Documents
| ID | Requirement |
|---|---|
| UX-22 | All printed documents on A4, RTL Arabic layout, company header and logo, page X of Y, amount in words, QR where required, signature blocks. |
| UX-23 | Print preview before printing; PDF identical to the printout. |

### 13.7 Accessibility
| ID | Requirement |
|---|---|
| UX-24 | WCAG 2.1 AA: contrast, full keyboard access, visible focus, labelled controls, errors announced to screen readers, reduced-motion respected. |
| UX-25 | Click targets ≥ 32 px on desktop, ≥ 44 px on touch devices. |

## Section 14 — Security, Privacy & Compliance

### 14.1 Authentication & Sessions
| ID | Requirement |
|---|---|
| SEC-01 | Email and password sign-in; passwords stored only as a strong salted hash (ASP.NET Core Identity standard), never shown or emailed. |
| SEC-02 | Password policy and lockout per FR-175; mandatory two-factor authentication for every user per FR-177. |
| SEC-03 | Secure HTTP-only session cookies with idle timeout; sign-out ends the server session; password reset and role changes end the user's other sessions. |
| SEC-04 | Verification, invitation and password-reset links are single-use and expire. |

### 14.2 Authorisation & Isolation
| ID | Requirement |
|---|---|
| SEC-05 | Every request checked server-side for tenant, company and permission; the interface never decides access alone. |
| SEC-06 | Every query filtered by tenant and company at the data-access layer; automated cross-tenant access tests on every release. |
| SEC-07 | Provider staff access only through an approved, time-limited support window (BR-014). |

### 14.3 Data Protection
| ID | Requirement |
|---|---|
| SEC-08 | TLS 1.2+ for all traffic; SQL Server data, backups and stored files encrypted at rest. |
| SEC-09 | E-invoicing credentials and other secrets in a secure secret store or encrypted columns; never shown after saving or logged. |
| SEC-10 | Card payments handled entirely by the payment gateway; the platform never stores or sees card numbers. |
| SEC-11 | Driver personal and payment details (national ID, phones, bank details) are shown in full to every user who has permission to view drivers (drv.view or masters.manage); no field-level masking. Access is controlled by permission and recorded in the audit log for exports. |

### 14.4 Application Security
| ID | Requirement |
|---|---|
| SEC-12 | OWASP Top 10 protection: server-side validation, parameterised queries, output encoding, anti-forgery tokens, security headers. |
| SEC-13 | Uploaded files checked for type and size and processed safely; spreadsheet formulas not executed. |
| SEC-14 | Rate limiting on sign-in, password reset and the API. |
| SEC-15 | Dependency vulnerability scanning on every build; independent penetration test before launch. |

### 14.5 Audit & Integrity
| ID | Requirement |
|---|---|
| SEC-16 | Append-only, protected audit log (FR-188, FR-189), including sign-ins, failed sign-ins, permission changes and support-access sessions. |
| SEC-17 | Financial integrity rules enforced server-side: balanced entries, closed periods, gap-free numbering, reversal instead of deletion (Section 8). |

### 14.6 Privacy & Compliance
| ID | Requirement |
|---|---|
| SEC-18 | The tenant owns its data; the provider processes it only to deliver the service (5.6). |
| SEC-19 | Compliance with Jordan's Personal Data Protection Law, including data export. Because no record is hard-deleted, personal data that must be erased under the law is anonymised in place (record kept, personal fields cleared) — the product owner handles the legal confirmation. |
| SEC-20 | Launch-market tax compliance: 16 % sales tax, gap-free invoice numbering, national e-invoicing, printed QR; records kept at least 7 years (NFR-10). |
| SEC-21 | Owner accepts Terms of Service and Privacy Policy at sign-up; acceptance recorded with version and time. |

### 14.7 Soft Deletion & URL Protection
| ID | Requirement |
|---|---|
| SEC-22 | Hashed URLs: no internal database identifier (record, tenant or company ID) appears in any URL, link or API route. Every identifier in a URL is a hashed, non-sequential opaque token that cannot be guessed or enumerated; a tampered or unknown token returns "not found" without revealing whether the record exists. Server-side tenant, company and permission checks (SEC-05) still apply to every request. |
| SEC-23 | Soft deletion: no record in any entity is ever hard-deleted from the database (BR-041). Deleted records are flagged, hidden from all screens, lists, searches, reports and balances, and kept with who deleted them and when. |

## Section 15 — Testing & Acceptance Strategy

### 15.1 Test Levels
| ID | Level | Scope | When |
|---|---|---|---|
| TST-01 | Unit tests | Every Section 8 rule and calculation (rounding, tax, credit-note shares, tariff matching, rate resolution, settlement net, numbering) | Every build |
| TST-02 | Integration tests | Each operation with its transaction and journal entry, period locks, referential protection, tenant/company isolation | Every build |
| TST-03 | End-to-end UI tests | Main Section 9 flows through the Blazor interface in Arabic RTL and English | Every release candidate |
| TST-04 | E-invoicing tests | Build, validation, submission, rejection, credit-note referencing against the national system's test environment | Before launch and on format changes |
| TST-05 | Performance tests | NFR-01 and NFR-02 with 500,000 trips and 25 concurrent users | Before launch, then quarterly |
| TST-06 | Security tests | Cross-tenant attempts, permission checks on every API call, dependency scanning, penetration test | Every build (automated); penetration test before launch |
| TST-07 | Backup & restore drill | Single-company and full-platform restore within RTO/RPO | Before launch, then quarterly |

### 15.2 Test Data & Reference Cases
| ID | Requirement |
|---|---|
| TST-08 | Reference data set: demo tenant with two companies, drivers in all fleet types, partners, tariffs at every specificity, monthly rates, a full month of trips. |
| TST-09 | Section 8 worked examples automated with exact expected results to 3 decimals. |
| TST-10 | Concurrency: simultaneous approvals give consecutive numbers with no gaps or duplicates; simultaneous edits respect locks. |
| TST-11 | Reconciliation: after the reference month every sub-ledger matches its control account at 0.000; trial balance balances. |

### 15.3 Quality Gates
| ID | Gate |
|---|---|
| TST-12 | No release with any failing unit, integration or isolation test. |
| TST-13 | No open critical or high defects at launch; medium defects need a documented workaround and product-owner approval. |
| TST-14 | Code coverage of at least 80 % for domain and application layers. |

### 15.4 User Acceptance Testing
| ID | Requirement |
|---|---|
| TST-15 | UAT by launch-customer business users (dispatcher, accountant, administrator) on staging with a copy of their real master data. |
| TST-16 | UAT script covers every Section 9 flow and Section 6 acceptance criterion; each case Pass/Fail with evidence. |
| TST-17 | Written product-owner acceptance after UAT before go-live. No parallel run is required. |

## Section 16 — Release Plan
All scope is delivered in one release; this section defines go-live conditions only. Dates are set once the technical plan is agreed.

### 16.1 Release Milestones
| ID | Milestone | Exit criteria |
|---|---|---|
| REL-01 | PRD sign-off | All sections approved; Section 19 open items resolved or accepted |
| REL-02 | Design complete | Brand applied (name, logo, colours); screen designs for 13.5 approved |
| REL-03 | Build complete | All Must requirements implemented; quality gates TST-12 to TST-14 met |
| REL-04 | E-invoicing certified | Invoices and credit notes accepted by the national system's test environment (TST-04) |
| REL-05 | Security & performance passed | Penetration test with no open critical/high findings; performance targets met (TST-05, TST-06) |
| REL-06 | UAT accepted | UAT script passed with product-owner sign-off (TST-15 to TST-17) |
| REL-07 | Go-live | Production ready, backups verified (TST-07), monitoring active, support in place |

### 16.2 Go-live Readiness
| ID | Requirement |
|---|---|
| REL-08 | Production environment, payment gateway, email service and e-invoicing production credentials configured and tested. |
| REL-09 | Built-in transport chart template, demo company data, Arabic/English email templates, Terms of Service and Privacy Policy final. |
| REL-10 | Short user guide and help pages for each main flow in Arabic and English. |
| REL-11 | Tenant support channel and internal incident process with a named contact. |
| REL-12 | Documented rollback plan for the launch deployment. |

### 16.3 Launch
| ID | Requirement |
|---|---|
| REL-13 | At go-live the platform opens directly to public self-service sign-up. |
| REL-14 | Weekly review of usage, defects and support requests during the first month after go-live. |

## Section 17 — Risks & Mitigation
| ID | Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|---|
| RSK-01 | E-invoicing format or rules change, or test environment not available in time | Invoices cannot be submitted; compliance at risk | Medium | Separate connector layer (FR-066); early testing (TST-04); follow official notices; manual resubmission |
| RSK-02 | Cross-tenant data exposure | Severe: trust and legal exposure | Low | Data-access-layer isolation (SEC-06); automated cross-tenant tests; penetration test |
| RSK-03 | Calculation or rounding errors | Wrong invoices and payments; disputes | Medium | Automated Section 8 tests with exact results (TST-09); reconciliation tests (TST-11); UAT with real data |
| RSK-04 | Numbering gaps or duplicates under concurrency | Tax-audit findings | Low | Server-side sequence assignment in the transaction (BR-038); concurrency tests (TST-10) |
| RSK-05 | Performance degrades as data grows | Slow entry and reports | Medium | Server-side paging, indexing, background exports; performance tests at 500,000 trips (TST-05) |
| RSK-06 | Poor-quality historical data at onboarding | Wrong openings and history | High | Import preview, tolerant mode with Needs review, setup-only opening-balance difference (FR-110) |
| RSK-07 | Missing monthly driver rates at month start | Trips cannot be saved | Medium | Dashboard warning (FR-037); clear blocking message; authorised manual rate |
| RSK-08 | Low user adoption | Slow onboarding, churn | Medium | Keyboard-first entry, guided setup, Arabic-first UI, demo company, user guide (REL-10) |
| RSK-09 | Scope growth during build | Delays and cost overrun | Medium | Single-release scope fixed by this PRD; changes only via versioned revision (1.2) |
| RSK-10 | Open technical decisions delayed (hosting, residency, tenancy model) | Architecture rework | Medium | Section 10 kept open; decisions required before build (REL-01) |
| RSK-11 | Payment-gateway or email-provider outage | Failed payments or lost notifications | Low | Retries, grace period (5.2), delivery-failure logging |
| RSK-12 | Data loss | Severe | Low | Encrypted backups, point-in-time recovery, quarterly restore drills (NFR-05, TST-07) |

## Section 18 — Glossary
| Term (English) | Arabic | Definition |
|---|---|---|
| Tenant | المشترك | One subscribing organisation; contains one or more companies and its users. |
| Company | الشركة | A legal entity inside a tenant, with its own data, numbering, tax settings and ledger. |
| Trip / Shipment | رحلة / إرسالية | One truck load from a loading site to an unloading site, identified by its receipt number. |
| Receipt number | رقم الوصل | The loading receipt number of a trip; unique within the company. |
| Loading site / Unloading site | موقع التحميل / موقع التفريغ | Where the load is picked up and where it is delivered. |
| Loaded / Unloaded weight | الوزن المحمّل / المفرّغ | Weight in tons at loading and at delivery. |
| Billing quantity / Wage quantity | كمية الفوترة / كمية الأجرة | The weight used to bill the customer / to pay the driver, per company settings. |
| Shortage | العجز | Loaded minus unloaded weight; flagged above the tolerance %. |
| Own fleet / External fleet | أسطول الشركة / أسطول خارجي | Company-owned trucks / trucks of owner-drivers or other owners; each has its own monthly driver rate. |
| Ownership type | نوع الملكية | Classification of a driver's truck, mapped to own or external fleet. |
| Partner | شركة نقل شريكة | A haulage company whose trucks carry the company's loads, paid at the partner tariff instead of a driver wage. |
| Tariff | التعرفة | A customer or partner price rule keyed by party × material × loading site × unloading site, with validity dates. |
| Tariff breach | مخالفة التعرفة | A trip priced differently from its tariff price. |
| Monthly site rate | سعر الموقع الشهري | Driver wage rate per ton for a loading site in a month, separately for own and external fleet. |
| Frozen rate | السعر المثبّت | The driver rate saved into a trip at save time; later changes do not alter it. |
| Claim statement | المطالبة المالية | Invoice layout grouping trips by site or route, with a per-shipment annex. |
| Shipment annex | كشف تفاصيل الإرساليات | The claim-statement page listing every trip on the invoice. |
| Credit note | إشعار دائن / مرتجع | Full or partial return of an invoice; reverses its share and frees returned trips. |
| Draft | مسودة | An invoice before approval; no number, no entry. |
| Advance | سلفة | Cash paid to a driver before settlement. |
| Settlement | تسوية | Numbered statement paying a driver for open trips, net of advances and deductions. |
| Deduction | خصم | An amount subtracted at settlement (e.g. shortage, fine). |
| Receipt (cash) | سند قبض | Money collected from a customer, against an invoice or on account. |
| On account / Unapplied | على الحساب / غير مخصص | A receipt not linked to a specific invoice. |
| Voucher | سند | Printed receipt, payment or journal document with the amount in words. |
| Amount in words | كتابة المبلغ بالكلمات | The amount written out in Arabic words on vouchers and invoices. |
| Posting key | مفتاح الترحيل | A named role (e.g. Transport revenue) mapped to a ledger account for automatic entries. |
| Account mapping | ربط الحسابات | Linking every posting key to an account in the chart of accounts. |
| Journal entry | قيد يومية | A balanced set of debit and credit lines recording a financial event. |
| Reversal entry | قيد عكسي | A mirror entry that cancels a previous entry; the original is kept. |
| Control account | حساب رقابي | A ledger account summarising a sub-ledger (receivables, driver payable, partner payable, advances). |
| Period | الفترة المحاسبية | A calendar month; a closed period accepts no activity. |
| Opening balance difference | فرق الأرصدة الافتتاحية | Equity account used only during initial setup to hold an unbalanced opening difference. |
| Fils | فلس | 1/1000 of a Jordanian dinar. |
| National e-invoicing | الفوترة الوطنية | The government system receiving electronic invoices (launch market: JoFotara). |
| Record lock | قفل السجل | Prevents two users from editing the same record at once. |
| Audit log | سجل التدقيق | Read-only record of every action with user and time. |
| Maintenance zone | منطقة الصيانة | Protected area for bulk accounting clean-up actions. |

## Section 19 — Assumptions & Open Questions

### 19.1 Confirmed Values (accepted by the product owner)
| ID | Ref | Value |
|---|---|---|
| ASM-01 | 1.6 | Arabic-first RTL interface with English as a second language. |
| ASM-02 | 3.1 | Provider support staff access tenant data only through a tenant-approved, time-limited, audited window. |
| ASM-03 | 5.2 | Trial period of 14 days. |
| ASM-04 | 5.2 | Grace period of 7 days after a failed payment. |
| ASM-05 | 5.2 | Cancellation after 30 days suspended; data retained 90 days after cancellation, then soft-deleted. |
| ASM-06 | 5.3 | All plans have the same features and differ only in limits. |
| ASM-07 | 5.3 | Monthly or annual billing in advance, by card or bank transfer through a payment gateway. |
| ASM-08 | 7.9.2 | Invitation links expire after 7 days. |
| ASM-09 | 10.1 | Screens and saves under 1 second; monthly reports under 3 seconds. |
| ASM-10 | 10.1 | 25 concurrent users and 500,000 trips per company. |
| ASM-11 | 10.1 | 99.5 % monthly availability. |
| ASM-12 | 10.1 | RPO ≤ 15 minutes; RTO ≤ 4 hours. |
| ASM-13 | 10.1 | Audit log and financial records kept at least 7 years. |
| ASM-14 | 14.6 | Compliance with Jordan's Personal Data Protection Law. |

### 19.2 Deferred Decisions (decided by the product owner after the build)
| ID | Ref | Item | Build approach |
|---|---|---|---|
| DEF-01 | 10.2 / NFR-22 | Hosting provider and data residency | Build provider-neutral and container-ready; decide at deployment |
| DEF-02 | 12.1 | Payment gateway and email service provider | Build against provider-neutral interfaces; plug in the chosen providers by configuration |

Resolved during review: the product owner handles the legal confirmation for anonymising personal data (SEC-19); the tax-authority rule is confirmed — an accepted e-invoice can only be cancelled with a credit note (FR-046).

### 19.3 Decisions Recorded During Review
- Product name: Direct Service Transportation; product owner: Direct Service Company.
- One release covering all flows; no future releases described.
- Exactly one truck per driver, stored in and edited from the driver record; fleet type from the driver's ownership type.
- Master data (customers, materials, sites) cannot be created from the trip form.
- Invoice approval on by default; an e-invoiced invoice is cancelled only by a credit note.
- Partner payments cannot exceed the partner's balance due.
- Expired driver documents give a warning only.
- Driver details shown in full to every user with permission to view drivers.
- Opening balance difference allowed only during initial setup / data migration.
- Internal API only; no public API in this release.
- Light theme only, using the Direct Service brand colours; typefaces IBM Plex Sans Arabic and Inter.
- Technology: latest stable .NET (.NET 10), Blazor, latest SQL Server (SQL Server 2025).
- One shared Master Database for this system and its sister projects; tenant isolation by TenantId and CompanyId.
- Soft deletion for all entities; no hard deletes anywhere.
- Two-factor authentication mandatory for every user.
- Hashed, non-sequential identifiers in all URLs.
- No parallel run before go-live; go-live opens directly to public sign-up.

<!-- Notes for later sections: launch market is Jordan (JOD, 16% sales tax, national e-invoicing); currency, tax and e-invoicing must be configurable per company to allow other markets later — cover in Sections 4, 5, 10, 14, 19. -->
