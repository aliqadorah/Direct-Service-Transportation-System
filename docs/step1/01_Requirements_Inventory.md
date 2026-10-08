# Direct Service Transportation — Step 1: Requirements Inventory

_Prepared 8 Oct 2026 · Inputs: `docs/inputs/PRD_v1.2.md` (v1.2, approved), `docs/inputs/Database_Design.md` (revision 4), `docs/inputs/Build_Brief.md`._
_Status: **awaiting owner approval**. Nothing in this document changes the PRD or the database design; every open point is listed in sections 6–8 with a recommendation for you to decide._

Companion file: `02_Traceability_Matrix.md` (every requirement → database objects → code module → screens → tests).

---

## 1. Environment check (done before step 1)

| Check | Result |
|---|---|
| .NET SDK | 10.0.401 (also 10.0.400); runtimes ASP.NET Core 10.0.12 |
| `dotnet ef` | Not installed at first; installed global tool `dotnet-ef` 10.0.12 from NuGet — works |
| SQL Server | `localhost` (default instance MSSQLSERVER), SQL Server 2025 RTM 17.0.1000.7 Standard Developer, Windows authentication, current login is sysadmin, collation `SQL_Latin1_General_CP1_CI_AS` |
| Code folder | `D:\Direct Service Transportation System` (not a git repository yet) |

Note on collation: the server default is `SQL_Latin1_General_CP1_CI_AS`. All text a person types is `NVARCHAR`, so Arabic is stored correctly. The database collation is a step 2 decision (see A-19).

---

## 2. Modules

The PRD's functional areas are grouped into the modules below. Step 2 sets the final module boundaries and build order. These IDs are used throughout the matrix.

| ID | Module | PRD sections | Main tables |
|---|---|---|---|
| X | Cross-cutting foundation | 1.8, 8.1, 8.8, 11.1, 14.7, NFR-04/06/18/21 | Standard columns on every table; `core.AuditLogs` |
| M01 | Platform, tenancy & subscription | 5.1–5.7, 5.5 | `core.Plans`, `core.Tenants`, `core.SubscriptionInvoices`, `core.Companies`, `core.SupportAccessGrants`, `core.Announcements`, `core.TermsAcceptances` |
| M02 | Identity & access | 3.3–3.6, 7.9.2, 14.1–14.2 | `core.Users`, `core.UserRecoveryCodes`, `core.UserSessions`, `core.Permissions`, `core.Roles`, `core.RolePermissions`, `core.UserCompanyRoles`, `core.Invitations` |
| M03 | Company configuration | 7.9.1, 7.9.3 | `core.Companies`, `cfg.CompanySettings`, `cfg.NumberSequences`, `cfg.CustomFieldDefinitions`, `cfg.StandardFieldConfigs`, `cfg.TableColumnViews` |
| M04 | Master data | 7.8 | `trn.TRN_OwnershipTypes`, `TRN_Partners`, `TRN_Drivers`, `TRN_Customers`, `TRN_Materials`, `TRN_LoadingSites`, `TRN_UnloadingSites` |
| M05 | Pricing | 7.2, 8.4 | `trn.TRN_Tariffs`, `TRN_MonthlySiteRates`, `TRN_RateHistory` |
| M06 | Trips | 7.1, 8.2 | `trn.TRN_Trips`, `cfg.ImportBatches` |
| M07 | Ledger integration (posting engine) | 7.5, 8.6 | `acc.PostingKeyMappings`, `acc.JournalEntries`, `acc.EntryLines`, `acc.PendingPostings`, `acc.JournalExportBatches` |
| M08 | Accounting | 7.6, 8.7 | `acc.Accounts`, `acc.CostCentres`, `acc.CashBoxes`, `acc.BankAccounts`, `acc.Periods`, `acc.ApArParties`, `acc.ApArDocuments`, `acc.ApArDocumentLines`, `acc.ApArPayments`, `acc.ApArReturns`, `core.MaintenanceLogs` |
| M09 | Invoicing, credit notes & printing | 7.3.1–7.3.5, 8.3 | `trn.TRN_Invoices`, `TRN_InvoiceLines`, `TRN_CreditNotes`, `TRN_CreditNoteLines`, `TRN_InvoiceLayouts` |
| M10 | National e-invoicing | 7.3.6, INT-01 | `cfg.EInvoiceSettings`, `trn.TRN_EInvoiceSubmissions`, `acc.ApArEInvoiceSubmissions` |
| M11 | Cash, drivers & partners | 7.4, 8.5 | `trn.TRN_Receipts`, `TRN_Advances`, `TRN_Settlements`, `TRN_SettlementDeductions`, `TRN_PartnerPayments` |
| M12 | Reports & dashboard | 7.7 | `cfg.SavedReports`, `cfg.DashboardPins` (plus read-only use of every business table) |
| M13 | Collaboration, search, audit & notifications | 7.9.4–7.9.5, 12.4 | `cfg.RecordLocks`, `core.Notifications`, `core.AuditLogs` |
| M14 | Data exchange (import/export, company export) | 12.3, 5.6 | `cfg.ImportBatches` (plus file output) |

All 66 tables are assigned to a module (core 18, trn 22, acc 16, cfg 10).

---

## 3. Inventory counts

| Kind | IDs | Count |
|---|---|---|
| Product goals | G-01 … G-09 | 9 |
| User stories | US-01 … US-34 | 34 |
| Functional requirements | FR-001 … FR-190 | 190 (Must 183 · Should 7) |
| Business rules | BR-001…004, BR-010…014, BR-020…041, BR-031a, BR-038a | 33 |
| User flows | 9.1 … 9.14 | 14 |
| Non-functional | NFR-01 … NFR-22 | 22 |
| Security | SEC-01 … SEC-23 | 23 |
| UI/UX | UX-01 … UX-25 | 25 |
| Integrations / API / data exchange / real-time | INT-01…03, API-01…07, DX-01…04, RT-01…02 | 16 |
| Testing | TST-01 … TST-17 | 17 |
| Release | REL-01 … REL-14 | 14 |
| Confirmed values / deferred | ASM-01…14, DEF-01…02 | 16 |
| Standard reports (7.7.2 / 7.7.3) | RPT-01 … RPT-23 (numbered in the matrix) | 23 |
| Screens | SCR-xx (numbered in the matrix): 64 from 13.5 + 5 placements (G-21) | 69 |
| Tenancy/platform items without PRD IDs (Section 5) | P-01 … P-17 (numbered in the matrix) | 17 |

Exact Should items: FR-018, FR-037, FR-066, FR-134, FR-151, FR-184, FR-190 (7). Every other FR is Must (183).

---

## 4. Roles, permissions and users

**User levels:** Platform (provider staff, `core.Users.IsPlatformStaff = 1`, `TenantId NULL`), Tenant users, External parties (not users).

**Preset roles (3.5), created per tenant at sign-up** (`core.Roles.TenantId` is NOT NULL, so presets are rows per tenant, not global seed):

| Role | Keys |
|---|---|
| Owner | All 22 keys incl. tenant.manage |
| Administrator | All except tenant.manage (incl. acct.maint) |
| Accountant | trips.view, inv.view, inv.issue, inv.void, rcpt.manage, drv.view, drv.advance, drv.settle, je.view, je.export, acct.view, acct.post, acct.apar, reports.view |
| Dispatcher | trips.view, trips.edit, trips.void, drv.view, masters.manage, reports.view |
| Viewer | trips.view, inv.view, drv.view, je.view, acct.view, reports.view |
| Custom | Any combination |

**Permission keys (22):** trips.view, trips.edit, trips.void, inv.view, inv.issue, inv.void, rcpt.manage, drv.view, drv.advance, drv.settle, je.view, je.export, acct.view, acct.post, acct.apar, acct.setup, acct.maint, reports.view, masters.manage, settings.manage, users.manage, tenant.manage. The Owner preset holds all 22; the Administrator preset holds 21.

The database requires keys to be namespaced (`trn.trips.view`). The mapping is decision D-01.

**Personas → default roles:** Tenant Owner → Owner · System Administrator → Administrator · Accountant → Accountant · Dispatcher / Data-entry clerk → Dispatcher · Manager / Viewer → Viewer · Provider Support Agent → platform role (see D-14).

---

## 5. Calculations and worked examples (become unit tests, TST-09)

| ID | Calculation | Source | PRD worked example → expected result |
|---|---|---|---|
| CALC-01 | Billing qty = unloaded if "Bill on unloaded weight" on (default) and recorded, else loaded | 8.2, FR-005 | EX-1 |
| CALC-02 | Wage qty = loaded if "Pay driver on loaded weight" on (default off), else unloaded, fallback loaded | 8.2, FR-005 | EX-1 |
| CALC-03 | Revenue = billing qty × customer price, half-up 3 dp | 8.2, BR-021 | EX-1: 31.850 × 6.000 = **191.100** |
| CALC-04 | Driver wage = wage qty × driver rate (0 for partner trips) | 8.2 | EX-1: 31.850 × 3.500 = **111.475** |
| CALC-05 | Partner cost = wage qty × partner rate | 8.2 | — |
| CALC-06 | Margin = revenue − wage − partner cost (not stored) | 8.2 | EX-1: **79.625** |
| CALC-07 | Shortage = loaded − unloaded; shortage % = shortage ÷ loaded × 100; flag above tolerance (default 0.5 %) | 8.2, FR-012 | EX-1: 0.150 t = **0.47 %**, not flagged |
| CALC-08 | Tariff breach = (price − tariff price) × billing qty; − loss, + excess | 8.2, FR-023 | — |
| CALC-09 | Tariff specificity = party 8 + material 4 + loading 2 + unloading 1; ties → latest Valid from | FR-021, BR-028 | All-All = 0; party = 8; party+material = 12; all four = 15 |
| CALC-10 | Driver rate resolution (Monthly / Manual / 0 for partner / blocked) | FR-030, BR-029 | Decision table in FR-030 |
| CALC-11 | Invoice: subtotal; 0 ≤ discount ≤ subtotal; fees ≥ 0; base = subtotal − discount + fees; tax = round(base × rate); total = base + tax; remaining = total − active receipts − active credit notes | 8.3, BR-023 | EX-2: 720.000, −20.000, +5.000 → base **705.000**, tax **112.800**, total **817.800** |
| CALC-12 | Credit note shares proportional to revenue; tax at invoice rate; ≤ remaining; full return absorbs rounding | BR-025…027 | EX-3: 180.000 of 720.000 → 25 %; discount **5.000**; fees **1.250**; base **176.250**; tax **28.200**; total **204.450** |
| CALC-13 | Settlement: gross − advances − deductions = net ≥ 0 | 8.5 | EX-4: 1,250.000 − 400.000 − 35.000 = **815.000** |
| CALC-14 | Amount in Arabic words using currency main and fractional units | BR-024 | e.g. 815.000 JOD |
| CALC-15 | Ageing buckets not due, 1–30, 31–60, 61–90, > 90 as of a date; unapplied separate | US-22, FR-128 | — |
| CALC-16 | Rounding half-up at each calculated amount, never only at the total | BR-021 | — |
| CALC-17 | Gap-free numbering under concurrency | BR-038, TST-10 | — |

---

## 6. Conflicts between the PRD and the database design (need your decision)

Under the build prompt I must not choose between the two documents myself. Each item gives the options and my recommendation.

| # | Topic | PRD says | Database design says | Options | Recommendation |
|---|---|---|---|---|---|
| C-01 | Default invoice layout | 7.3.1 / US-12: **Claim statement is the default** | `TRN_Invoices.LayoutType` DEFAULT **'TaxInvoice'** | (a) Keep the DB default; the server always writes the layout and defaults new invoices to 'Claim'. (b) Change the DB default to 'Claim' (schema change). | **(b)** — the database default then matches the PRD and no code path can fall back to the wrong value. Needs your approval because it changes the design. |
| C-02 | Tariff price lower bound | 7.2.1: price per ton **≥ 0** | `TRN_Tariffs.Price` **CHECK > 0** | (a) Follow the DB (> 0). (b) Change the CHECK to ≥ 0. | **(a)** — a zero tariff would make every non-zero price a "breach". Record the PRD as clarified. |
| C-03 | Settlement deduction amount | FR-078 / 8.5: each deduction **≥ 0** | `TRN_SettlementDeductions.Amount` **CHECK > 0** | (a) Follow the DB (> 0); a zero line is simply not saved. (b) Change the CHECK to ≥ 0. | **(a)** |
| C-04 | Correcting a rejected e-invoice | FR-063 / US-13 / 9.7: rejected shows the reason and **can be corrected and resubmitted** | `TRN_EInvoiceSubmissions` rule: "A rejected invoice is **corrected by a credit note and a new invoice**" | (a) PRD: allow correction and resubmission of the same document. (b) DB note: never resubmit; issue a credit note and a new invoice. | **(a)** for rejections due to missing or invalid data (the document was never accepted). The credit-note route stays mandatory for accepted documents (FR-046). Please confirm. |
| C-05 | "Failed" e-invoice state | FR-061: automatic retries, then **Rejected/Failed** list | Status CHECK has no 'Failed' (Queued, Submitted, Rejected, NotRequired) | (a) After retries are used up, set status 'Rejected' and record the technical error in `LastError`; the list is labelled "Rejected / Failed". (b) Add 'Failed' to the CHECK (schema change). | **(a)** — no schema change. |
| C-06 | Editing posted manual journal entries | FR-112 / US-20: **edit posted manual entries** in an open period with full change history | `acc.JournalEntries`: "posted entries are **never edited**; corrections are reversal entries" | (a) "Edit" = reverse the original (Status Reversed) and post a corrected entry linked to it (new number); the UI shows it as an edit with history. (b) Edit lines in place and keep the history in `core.AuditLogs`. | **(a)** — same mechanism as BR-035 for trips and advances; keeps the ledger append-only. |
| C-07 | Tariff lock | FR-025 / US-06 / BR-004: under the lock, deviations are **allowed for users with masters.manage** (audited) | `TRN_Tariffs` rule: "When Lock tariffs is on, trips **must** use the tariff price" | (a) PRD wording (masters.manage may deviate). (b) DB wording (nobody may deviate). | **(a)** — the PRD is more specific and is repeated in three places. |
| C-08 | Built-in "Company" ownership type | FR-161: cannot be **deleted or reclassified** | `TRN_OwnershipTypes.IsBuiltIn`: cannot be **renamed or deleted** | (a) Apply both: no delete, no reclassify, no rename. (b) PRD only (renaming allowed). | **(a)**, though renaming is low-risk either way. |
| C-09 | English account name | 7.6.1: account name **required, Arabic and English** | `acc.Accounts.NameEn` is **NULL**able | (a) Require both in the application; the DB stays nullable. (b) Arabic required, English optional (follow the DB). | **(a)** — meets the PRD without a schema change. |
| C-10 | Partner payment details | 7.8.3: bank name, IBAN or CliQ alias, **all optional** | `TRN_Partners.Iban/CliqAlias` rules say "required when **PaymentMethod** = Bank/CliQ", but the table has **no PaymentMethod column** (same wording on `acc.BankAccounts.Iban`) | (a) Treat as copy-over text: IBAN validated when given, nothing conditionally required. | **(a)** — no schema change. |
| C-11 | Suspended tenant access | 5.2: read-only — **view, print and export** | `core.Tenants` rule: "read-only (**export only**)" | (a) PRD wording. | **(a)** |

---

## 7. Gaps — the PRD requires something the database design has no place for, or the PRD is silent (need your decision)

| # | Gap | Where | Options | Recommendation |
|---|---|---|---|---|
| G-01 | **Tenant-level settings** — password policy "configurable per tenant" (FR-175) and session idle timeout "tenant-configurable" (FR-176) have no table: `cfg.CompanySettings` is per company and `core.Tenants` has no such columns. | FR-175, FR-176 | (a) Add a tenant settings table or columns (schema change). (b) Store the tenant values in `cfg.CompanySettings` under `CORE.*` keys on every company of the tenant, kept identical by the server. (c) Platform-wide configuration only (does not meet "per tenant"). | **(a)** is cleanest but changes the design. If you want no schema change, **(b)**. Your choice. |
| G-02 | **Archiving a company** (Owner "create and archive companies", 3.2) — `core.Companies` has no Status column. | 3.2, 5.6 | (a) Archive = soft delete with reason (the company disappears everywhere, including the switcher; export still possible via an Owner-only path). (b) A `CORE.Archived` key in `cfg.CompanySettings`. (c) Add a Status column (schema change). | **(c)** to match the other archivable tables; **(b)** if no schema change. |
| G-03 | **Built-in transport chart-of-accounts template** — the PRD requires it (FR-107, 5.4, REL-09) but does not list its accounts, nor the default mapping of the posting keys. | FR-107, REL-09 | (a) You supply the template. (b) I propose a template (codes, Arabic/English names, types, control accounts, default posting-key mapping) for your approval before it is seeded. | **(b)** — I draft it in step 3 as a separate item for your review. |
| G-04 | **"Opening balance difference" account** (FR-110) — not among the 14 posting keys in 7.5.1 and not otherwise identified in the DB. | FR-110 | (a) Add posting key `ACC.OpeningBalanceDifference` (a data row in `acc.PostingKeyMappings`, no schema change), required while migration is not complete. | **(a)** |
| G-05 | **Subscription credit** — provider console "apply a credit" (5.5) and pro-rated upgrades (5.3) have no place to hold a credit balance; `core.SubscriptionInvoices.Amount` is CHECK ≥ 0. | 5.3, 5.5 | (a) A credit is recorded as a subscription invoice with Status 'Credited' against the charge it reduces (no balance carried forward). (b) Add a credit-balance column or table (schema change). | **(b)** if credits should carry forward; otherwise (a). Please tell me how you want credits to work. |
| G-06 | **Which plans to seed** — the PRD says plans and prices are set by the provider without code changes, but a trial tenant needs a plan (`Tenants.PlanId` NOT NULL). | 5.2, 5.3 | (a) Seed one inactive-for-sale "TRIAL" plan with placeholder limits that you adjust in the console. (b) You give me the launch plans and limits. | **(a)**, then you edit it in the provider console. |
| G-07 | **End of trial without payment** — the PRD lists the statuses but not what happens when the 14-day trial ends unpaid. | 5.2 | (a) Trial → Suspended (read-only) → Cancelled after 30 days, as for unpaid subscriptions. (b) Trial → PastDue with the 7-day grace period first. | **(a)** |
| G-08 | **Provider staff permissions** — 3.1/5.5 name provider staff and a "Provider Support Agent" platform role, but `core.Roles` are tenant roles and `UserCompanyRoles` needs a company. There is no table for platform roles. | 3.1, 5.5, SEC-07 | (a) Every platform-staff user can use the provider console; tenant data only through an approved support window. (b) Add platform roles (schema change). | **(a)** |
| G-09 | **What support staff may do inside a support window** | 5.5, BR-014 | (a) Read-only (all view permissions of the company, no create/change), every request audited. (b) Same permissions as an Administrator. | **(a)** — "diagnose" implies read-only. |
| G-10 | **Setup checklist progress** (5.4, US-31: saved, resumable, Not started / In progress / Done) — no table. | US-31 | (a) Work out each step's status from the data (profile filled, accounts exist, mapping complete, master data present, current-month rates present, users invited) and store explicit "Done" marks in `cfg.CompanySettings` (`CORE.Setup.*`). | **(a)** — no schema change. |
| G-11 | **Demo company** (5.4) — no flag on `core.Companies`; does it count toward the plan's MaxCompanies? | 5.4 | (a) Mark it with `cfg.CompanySettings` key `CORE.IsDemo = true`; it does not count toward the limit. (b) It counts. | **(a)** — please confirm the limit rule. |
| G-12 | **Quick-entry memory** — "last driver, customer, material and route remembered per user across sessions" (FR-007) — no per-user preference store. | FR-007 | (a) Take them from the user's most recent saved trip (`TRN_Trips.CreatedBy`), server-side, works on any device. (b) Browser storage (per device only). | **(a)** — no schema change. |
| G-13 | **Idempotency keys** (API-05 / NFR-04) — no table. | API-05 | (a) Create calls carry a client-generated `PublicId`; a repeat hits `UX_*_PublicId` and returns the existing record. Approve/void are naturally idempotent (status + RowVersion). (b) Add an idempotency table (schema change). | **(a)** |
| G-14 | **Partner tariff price and breach** — `TRN_Trips` stores only the customer `TariffPrice`; nothing records the partner tariff rate. Do the breach flag (FR-023) and the tariff lock (FR-025) apply to the partner rate too? | FR-022…025 | (a) Breach flag, breach report and lock apply to the **customer price only**; the partner rate is pre-filled from the partner tariff, and a typed different rate is audited. (b) Also flag partner-rate deviations (needs a column for the partner tariff rate — schema change). | **(a)** |
| G-15 | **Driver-rate fields on partner trips** — `RateSource` (Monthly/Manual) and `RateMonth` are NOT NULL, but FR-030 gives partner trips a rate of 0 with no source. | FR-030 | (a) Partner trips store DriverRate 0, RateSource 'Monthly', RateMonth = month of the loading date, IsManualRate 0. | **(a)** |
| G-16 | **Suggested shortage deduction amount** (FR-078) — no formula. | FR-078 | (a) Shortage weight × customer price of each flagged trip, editable. (b) Only the weight above the tolerance × customer price. (c) Show the flagged trips and their shortage weights; the user types the amount. | **(c)**, unless you have a formula. |
| G-17 | **Control-account party types** — `acc.Accounts.ControlPartyType` allows Customer/Driver/Partner/Supplier, while `acc.EntryLines.PartyType` allows Customer/Driver/Partner (TRN) or ApArParty (ACC). The matching rule for AP/AR parties is not stated. | FR-106, FR-122 | (a) 'Customer' accepts TRN Customer **or** ACC ApArParty with role Customer; 'Supplier' accepts ACC ApArParty with role Supplier; 'Driver'/'Partner' accept the TRN party. | **(a)** |
| G-18 | **Tenant deletion** (5.2 Deleted: "all tenant data is soft-deleted") | 5.2, BR-041 | (a) A background job sets IsDeleted on every row of the tenant in batches (reason "Retention period ended"), then marks the tenant Deleted. (b) Only the tenant row is soft-deleted; access is blocked through it. | **(a)** — matches the PRD wording literally. |
| G-19 | **Default number prefixes** — BR-037 gives only examples (TRP-00001, STL-00001; flow 9.5 shows an invoice numbered TRP-00045). | BR-037 | Proposed defaults, 5 digits each: Invoice **TRP-**, Credit note **CRN-**, Receipt **RCV-**, Advance **ADV-**, Settlement **STL-**, Partner payment **PPY-**, Journal entry **JV-**, Supplier bill **BIL-**, Receivable document **ARD-**, AP/AR payment **PAY-**, AP/AR return **RTN-**, Journal export batch **JEX-**. | Approve or change. |
| G-20 | **Terms of Service / Privacy Policy text and version** (SEC-21, REL-09) — not supplied. | SEC-21 | Placeholder documents, version "draft-1", until you provide the final texts. | Placeholders. |
| G-21 | **Screens not named in 13.5** — two-factor set-up/challenge, the user's own preferences and active sessions (FR-176/178), notifications panel, print preview, and the tenant-side approval of support access. | 13.5 | Place them in: sign-in flow (2FA), user menu (preferences, sessions), top bar (notifications), shared print preview, Tenant group (support-access approvals). | Approve placement. These are not new features; the PRD requires each function. |

---

## 8. Interpretations and assumptions to confirm (common ground)

These are my readings where the documents are consistent but leave room for interpretation. I will build on them unless you object.

| # | Assumption | Basis | Confidence |
|---|---|---|---|
| A-01 | Invoice statuses "Approved – Unpaid / Partially paid / Partially returned" are **derived** from Status = 'Approved' plus Paid/Returned/Remaining amounts; only Draft/Approved/Closed/Void are stored. | 7.3.2 vs DB CHECK | High |
| A-02 | Trip statuses "Active – Unbilled / Invoiced / Settled" are derived from Status + `InvoiceId` + `SettlementId`. A draft invoice already sets `InvoiceId` (reserves the trip). | 7.1.2, FR-042 | High |
| A-03 | When e-invoicing is disabled, approval writes a submission row with Status 'NotRequired', so the e-invoicing report (FR-065) counts every document. | FR-063, FR-065 | Medium |
| A-04 | Print and reprint logging (FR-058, FR-090, "Copy" mark) uses `core.AuditLogs` with Action 'Print'; a document already printed once prints with "Copy". | FR-058, FR-090 | Medium |
| A-05 | Monthly periods are created on demand; a month without a row in `acc.Periods` is Open. | FR-132 | Medium |
| A-06 | Account "Active Yes/No" (7.6.1) = `Status` Active/Archived. "Deactivate" = Archived. | 7.6.1 vs DB | High |
| A-07 | `TRN_Tariffs.Specificity` is a **persisted computed column** in SQL Server (it cannot drift from the keys), plus the CHECK 0–15. | DB "computed … (persisted computed column)" | Medium |
| A-08 | Journal export "re-export allowed" (FR-104): a re-export creates a new batch and `JournalEntries.ExportBatchId` points to the latest batch; earlier batches stay in the log. | FR-104 | Medium |
| A-09 | Cash boxes use the company's base currency (only bank accounts have `CurrencyCode`). | FR-129, 4.2 (single currency) | High |
| A-10 | All journal entries (manual and automatic) share one company sequence `ACC.JournalEntry`. | BR-037 | High |
| A-11 | AP/AR payments use one sequence for supplier payments and collections; AP/AR returns one sequence (numbers are unique per company in the DB). Supplier bills and receivable documents have separate sequences (DB unique per kind). | BR-037, DB uniques | High |
| A-12 | Arabic spelling-variant search (US-29) is done by normalising both the search text and the searched columns at query time (alef/hamza forms, ta marbuta/ha, alef maqsura/ya); no extra columns. | FR-187 | Medium |
| A-13 | Files produced by background exports and the company export package are stored through a provider-neutral file-storage interface (local folder in development), because hosting is deferred (DEF-01). | FR-151, 5.6, NFR-22 | High |
| A-14 | Payment gateway (INT-02) and email (INT-03) get provider-neutral interfaces with development implementations (fake gateway; email written to a local folder/log). | DEF-02 | High |
| A-15 | JoFotara (INT-01) is built behind the connector interface (FR-066). Without test credentials, development uses a simulated connector; real submission is verified against the JoFotara test environment when you provide credentials (TST-04/REL-04). | FR-066, TST-04 | High |
| A-16 | ASP.NET Core Identity maps onto `core.Users` without missing columns: UserName = Email; EmailConfirmed = `EmailConfirmedAt IS NOT NULL`; lockout = `FailedSignInCount`/`LockedUntil` (always enabled); authenticator key = `TwoFactorSecret` (encrypted); TwoFactorEnabled = `TwoFactorEnabledAt IS NOT NULL`; recovery codes → `core.UserRecoveryCodes`; concurrency → `RowVersion`. Phone, external logins and Identity role tables are not used. **No Identity feature lacks a column, so this is not a stop condition.** | Build prompt rule | High |
| A-17 | Password-reset and email-verification links are Identity data-protection tokens bound to the security stamp. They become invalid when the stamp changes, which makes them single-use, and they expire (SEC-04). No token table is needed. | SEC-04 | High |
| A-18 | An invitation for an email that already has an account in another tenant is refused with a clear message (one email = one account in one tenant). | Confirmed decision | High |
| A-19 | The database is created with an Arabic-aware, case- and accent-insensitive collation (e.g. `Arabic_100_CI_AI` or `Latin1_General_100_CI_AS_SC_UTF8`); to be proposed in step 2. | Arabic-first | Open |
| A-20 | `core.AuditLogs` and `core.MaintenanceLogs` have no standard columns (no PublicId, soft delete or RowVersion). They are excluded from the soft-delete filter and are insert-only. | DB section 5 | High |
| A-21 | Preset roles and their permissions are created for each tenant at sign-up. Only `core.Permissions` (the 22 keys) is seeded globally. | `core.Roles.TenantId` NOT NULL | High |
| A-22 | Reference data seeded per company at creation: the built-in "Company" ownership type, the 6 `TRN.*` settings with PRD defaults, number sequences (G-19), both invoice layouts, the chart template (G-03) when chosen. Global seed: the 22 permissions and the trial plan (G-06). | Build prompt step 3 | High |

---

## 9. Database cross-check summary

- Every PRD logical entity in Section 11 has a physical table. Section 11 names map to the design's names: Tariff → `TRN_Tariffs`, MonthlySiteRate → `TRN_MonthlySiteRates`, Settlement deduction lines → `TRN_SettlementDeductions`, AP/AR Party → `acc.ApArParties`, APDocument/ARDocument → `acc.ApArDocuments` (DocumentKind), APARPayment/APARReturn → `acc.ApArPayments`/`ApArReturns`, CompanySetting → `cfg.CompanySettings`, StandardFieldConfig → `cfg.StandardFieldConfigs`, ImportBatch → `cfg.ImportBatches`, RecordLock → `cfg.RecordLocks`.
- Tables in the design that Section 11 does not name, all backed by PRD requirements: `core.SubscriptionInvoices` (5.3), `core.UserRecoveryCodes` / `core.UserSessions` (FR-176/177), `core.Permissions` / `core.RolePermissions` (3.4), `core.Invitations` (FR-172), `core.TermsAcceptances` (SEC-21), `core.Announcements` (5.5), `cfg.EInvoiceSettings` (FR-059), `cfg.TableColumnViews` (FR-183), `acc.JournalExportBatches` (FR-104), `acc.ApArEInvoiceSubmissions` (FR-124 "queued for e-invoicing", see note).
- Section 11.9 uniqueness rules are all present as filtered unique indexes: receipt number (`ReceiptNoKey`), national ID, plate, active customer name, active partner name, material name, site names, account code, document numbers, monthly rate per month + site.
- "Shared tables never reference TRN_ tables": verified. The only shared → TRN links are polymorphic (`SourceModule/SourceType/SourceId`, `PartyModule/PartyType/PartyId`, `RecordLocks.EntityId`, `AuditLogs.EntityId`), with no foreign keys.
- Note (not a conflict): `acc.ApArDocuments` receivable documents are "queued for e-invoicing when enabled" in the DB. The PRD's e-invoicing section (7.3.6) covers transport invoices and credit notes; FR-124 only says "numbered and posted". I will also submit AR receivable documents and returns, because they are tax documents under SEC-20 and the DB provides `acc.ApArEInvoiceSubmissions`. **Please confirm (D-15).**
- Counts of foreign keys (118) and the full column/constraint comparison are verified against the created database in step 3, as the build prompt requires.

---

## 10. Decision list for you (short form)

Please answer by number. "Agree" accepts my recommendation.

| # | Decision | My recommendation |
|---|---|---|
| D-01 | Permission key namespacing in `core.Permissions`: module prefix + PRD key verbatim — `trn.` for trips/inv/rcpt/drv/masters/reports, `acc.` for acct.* and je.*, `core.` for settings/users/tenant (e.g. `trn.trips.view`, `acc.acct.post`, `core.users.manage`). The UI and documents keep showing the PRD names. | Agree? |
| D-02 | C-01 default invoice layout | Change DB default to 'Claim' |
| D-03 | C-02, C-03 lower bounds | Follow DB (> 0) |
| D-04 | C-04, C-05 rejected/failed e-invoices | Resubmit after correction; "Failed" = Rejected + LastError |
| D-05 | C-06 editing manual journal entries | Reverse + re-post |
| D-06 | C-07 tariff lock | PRD (masters.manage may deviate) |
| D-07 | C-08 … C-11 | Recommendations as in section 6 |
| D-08 | G-01 tenant settings | Your choice: new table/columns (a) or per-company copies (b) |
| D-09 | G-02 archive company | Status column (c), or settings key (b) |
| D-10 | G-03 chart template | I draft it for your approval |
| D-11 | G-05 subscription credits | Tell me the rule |
| D-12 | G-06, G-07 trial plan and trial end | Seed TRIAL plan; trial end → Suspended |
| D-13 | G-08, G-09 provider staff | Console for all staff; support window read-only |
| D-14 | G-10 … G-18, G-20, G-21 | Recommendations as in section 7 |
| D-15 | E-invoicing of AR receivable documents (section 9 note) | Include |
| D-16 | G-19 number prefixes | Approve list |
| D-17 | Assumptions A-01 … A-22 | Agree |
| D-18 | REL-10 user guide / help pages (Arabic + English): a go-live item in the PRD but not a build-prompt deliverable — in build scope or not? | Your call |
