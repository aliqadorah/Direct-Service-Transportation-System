# Direct Service Transportation — Traceability Matrix (skeleton, step 1)

_Prepared 8 Oct 2026. Skeleton: database objects and screens are mapped now. Code components are **planned names**, to be fixed in step 2 (architecture). Test columns give the planned test levels; test names are added as tests are written. Status is updated in steps 3–9._

**Legend**
- **Mod** — module from `01_Requirements_Inventory.md` §2 (X, M01…M14).
- **DB** — tables (schema prefix omitted: `TRN_*` = `trn`; `Users`, `Roles`… = `core`; `Accounts`, `JournalEntries`… = `acc`; `CompanySettings`, `NumberSequences`… = `cfg`). Setting keys are written `TRN.xxx`.
- **Code** — planned application component (service / handler / job). Final layering in step 2.
- **Screens** — `SCR-xx` from the screen catalogue (§A). `—` = none (with reason).
- **Tests** — U = unit · I = integration (database + transaction) · S = security/isolation · E = end-to-end UI (Playwright, Arabic RTL + English) · P = performance · O = operational/manual procedure (not automatable in code).
- **N/A** is always given with a reason.
- **St** — status: ☐ not started · ◐ backend implemented and tested (UI / E2E pending, steps 8–9) · ☑ done (no UI part, or fully done). Updated per block; see `docs/step4/B*_*.md` for the test names.

---

## A. Screen catalogue (from PRD 13.5, plus the placements proposed in G-21)

| ID | Screen | Group |
|---|---|---|
| SCR-00 | App shell: side navigation, company switcher, global search, notifications panel, user menu, banners | Shell |
| SCR-01 | Sign-in (incl. 2FA challenge, recovery code) | Start |
| SCR-02 | Sign-up (incl. terms acceptance) | Start |
| SCR-03 | Email verification | Start |
| SCR-04 | Password reset | Start |
| SCR-05 | Two-factor set-up (first sign-in, recovery codes) — G-21 | Start |
| SCR-06 | Setup checklist | Start |
| SCR-07 | Dashboard | Start |
| SCR-08 | My profile: preferences and active sessions — G-21 | User menu |
| SCR-09 | Print preview (shared, all printed documents) — G-21 | Shared |
| SCR-10 | Trips: quick-entry row, recent trips, Created Trips list | Operations |
| SCR-11 | Trip form (full) with history panel | Operations |
| SCR-12 | Trip import | Operations |
| SCR-13 | Shortage view | Operations |
| SCR-20 | Invoices list | Billing & Cash |
| SCR-21 | New trip invoice (trip picker) | Billing & Cash |
| SCR-22 | Manual invoice form | Billing & Cash |
| SCR-23 | Invoice view (draft / approved / void) | Billing & Cash |
| SCR-24 | Credit notes (list, create from invoice, view) | Billing & Cash |
| SCR-25 | E-invoicing status | Billing & Cash |
| SCR-26 | Receipts (list, form, view) | Billing & Cash |
| SCR-27 | Driver accounts: account view, advances, settlements | Billing & Cash |
| SCR-28 | Partner accounts: account view, payments | Billing & Cash |
| SCR-30 | Chart of accounts (tree, import/export) | Accounting |
| SCR-31 | Journal list | Accounting |
| SCR-32 | Journal entry form | Accounting |
| SCR-33 | Journal import | Accounting |
| SCR-34 | Automatic entries & pending postings (incl. journal export) | Accounting |
| SCR-35 | General ledger | Accounting |
| SCR-36 | Trial balance | Accounting |
| SCR-37 | Profit & loss | Accounting |
| SCR-38 | Balance sheet | Accounting |
| SCR-39 | Account statement | Accounting |
| SCR-40 | Accounts payable (parties, supplier bills, payments, returns) | Accounting |
| SCR-41 | Accounts receivable (parties, receivable documents, collections, returns) | Accounting |
| SCR-42 | Banks and cash boxes | Accounting |
| SCR-43 | Cost centres | Accounting |
| SCR-44 | Periods (incl. pre-close check) | Accounting |
| SCR-45 | Maintenance zone | Accounting |
| SCR-50 | Operational reports | Reports |
| SCR-51 | Financial statements and ageing | Reports |
| SCR-52 | Report builder | Reports |
| SCR-53 | Saved reports | Reports |
| SCR-60 | Drivers (list, profile with truck, account summary, history) | Master Data |
| SCR-61 | Customers | Master Data |
| SCR-62 | Partners | Master Data |
| SCR-63 | Materials | Master Data |
| SCR-64 | Loading sites | Master Data |
| SCR-65 | Unloading sites | Master Data |
| SCR-66 | Ownership types | Master Data |
| SCR-67 | Tariffs | Master Data |
| SCR-68 | Monthly site rates | Master Data |
| SCR-70 | Company settings | Settings |
| SCR-71 | Account mapping | Settings |
| SCR-72 | Numbering | Settings |
| SCR-73 | E-invoicing settings | Settings |
| SCR-74 | Invoice layout designer | Settings |
| SCR-75 | Field configuration | Settings |
| SCR-76 | Users and roles (incl. invitations) | Settings |
| SCR-77 | Audit log | Settings |
| SCR-80 | Subscription and billing | Tenant |
| SCR-81 | Companies | Tenant |
| SCR-82 | Data export | Tenant |
| SCR-83 | Support access approvals — G-21 | Tenant |
| SCR-90 | Provider: tenants | Provider console |
| SCR-91 | Provider: plans | Provider console |
| SCR-92 | Provider: support access | Provider console |
| SCR-93 | Provider: announcements | Provider console |
| SCR-94 | Provider: platform health | Provider console |

69 screens: 64 named in 13.5 (the Trips screen with quick entry, recent trips and Created Trips counts as one) plus 5 placements from G-21 (SCR-00 shell, SCR-05, SCR-08, SCR-09, SCR-83). The sign-in 2FA challenge and the notifications panel sit inside SCR-01 / SCR-00.

## B. Standard reports catalogue (7.7.2 / 7.7.3)

| ID | Report | Source | Screen |
|---|---|---|---|
| RPT-01 | Trips report | 7.7.2 | SCR-50 |
| RPT-02 | Unbilled trips | 7.7.2 | SCR-50 |
| RPT-03 | Shortage report | 7.7.2 / FR-012 | SCR-50, SCR-13 |
| RPT-04 | Tariff breach report | FR-024 | SCR-50 |
| RPT-05 | Driver productivity | 7.7.2 | SCR-50 |
| RPT-06 | Customer revenue | 7.7.2 | SCR-50 |
| RPT-07 | Route & site analysis | 7.7.2 | SCR-50 |
| RPT-08 | Material volume | 7.7.2 | SCR-50 |
| RPT-09 | Partner shipments | 7.7.2 | SCR-50 |
| RPT-10 | Margin analysis | 7.7.2 | SCR-50 |
| RPT-11 | Monthly summary | 7.7.2 | SCR-50 |
| RPT-12 | Customer statement | 7.7.3 | SCR-51 |
| RPT-13 | Driver statement | FR-083 | SCR-51, SCR-27 |
| RPT-14 | Partner statement | FR-088 | SCR-51, SCR-28 |
| RPT-15 | Ledger account statement | FR-120 | SCR-39 |
| RPT-16 | Cash / bank statement | FR-130 | SCR-51, SCR-42 |
| RPT-17 | Receivables ageing | 7.7.3 / US-22 | SCR-51 |
| RPT-18 | Payables ageing | FR-128 | SCR-51 |
| RPT-19 | Driver balances | 7.7.3 | SCR-51 |
| RPT-20 | Partner balances | 7.7.3 | SCR-51 |
| RPT-21 | Registers (invoices, credit notes, receipts, advances, settlements, partner payments) | 7.7.3 | SCR-51 |
| RPT-22 | E-invoicing report | FR-065 | SCR-51, SCR-25 |
| RPT-23 | Sub-ledger reconciliation | FR-105 | SCR-51 |

---

## C. Functional requirements

### C.1 Trips (7.1)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-001 | Create trip from full form or quick-entry row | M | M06 | TRN_Trips | TripService.Create | SCR-10, SCR-11 | U I E | ☑ |
| FR-002 | Validate on save, block naming the field; warnings don't block | M | M06 | TRN_Trips, StandardFieldConfigs, CustomFieldDefinitions | TripValidator | SCR-10, SCR-11 | U I E | ☑ |
| FR-003 | Selecting driver shows truck; capacity warning | M | M06/M04 | TRN_Drivers | TripService, DriverLookup | SCR-10, SCR-11 | U E | ☑ |
| FR-004 | Fleet type from ownership type; partner from driver link | M | M06 | TRN_Trips.FleetClass/PartnerId, TRN_OwnershipTypes, TRN_Drivers.PartnerId | TripCalculator | SCR-10, SCR-11 | U | ☑ |
| FR-005 | Live calculations (billing/wage qty, revenue, wage, partner cost, margin) | M | M06 | TRN_Trips (BillingQty, WageQty, Revenue, DriverWage, PartnerCost), CompanySettings TRN.BillOnUnloadedWeight / TRN.PayDriverOnLoadedWeight | TripCalculator | SCR-10, SCR-11 | U (EX-1) | ☑ |
| FR-006 | Automatic pricing with source shown | M | M05/M06 | TRN_Tariffs, TRN_MonthlySiteRates | TariffResolver, DriverRateResolver | SCR-10, SCR-11 | U I | ☑ |
| FR-007 | Quick-entry keyboard behaviour; remember last values per user | M | M06 | TRN_Trips.CreatedBy (G-12) | QuickEntryDefaults | SCR-10 | E | ☑ |
| FR-008 | Last ten trips of the user with History/Edit | M | M06 | TRN_Trips, AuditLogs | TripQueries.Recent | SCR-10 | I E | ☑ |
| FR-009 | Created Trips list: filters, search, paging, sort, columns, export | M | M06/M14 | TRN_Trips, TableColumnViews | TripQueries.List, ExportService | SCR-10 | I E P | ☑ |
| FR-010 | Edit rules; reverse/re-post wage; rate kept unless site/month/driver change | M | M06/M07 | TRN_Trips, JournalEntries, EntryLines, Periods | TripService.Edit | SCR-11 | U I | ☑ |
| FR-011 | Void with reason; reversal; blocked if invoiced/settled/closed | M | M06/M07 | TRN_Trips (Void*), JournalEntries | TripService.Void | SCR-11 | I E | ☑ |
| FR-012 | Shortage and shortage %; flag above tolerance | M | M06 | TRN_Trips (ShortageWeight, ShortagePct, IsShortageFlagged), CompanySettings TRN.ShortageTolerancePct | TripCalculator | SCR-10, SCR-13 | U | ☑ |
| FR-013 | Import xlsx/CSV/TSV/paste, mapping, preview, tolerant mode, import log | M | M06/M14 | TRN_Trips, ImportBatches | TripImportService | SCR-12 | U I E | ☑ |
| FR-014 | Clear Needs review; recorded in history | M | M06 | TRN_Trips.NeedsReview, AuditLogs | TripService.ClearReview | SCR-10, SCR-11 | I | ☑ |
| FR-015 | Trip history incl. invoice/credit note/settlement events | M | M06/M13 | AuditLogs | HistoryQueries | SCR-11 (history panel) | I | ☑ |
| FR-016 | Wage accrual / partner cost entry on save | M | M07 | JournalEntries, EntryLines, PostingKeyMappings, TRN_Trips.JournalEntryId | PostingEngine (Trip) | — (no own screen; seen in SCR-34) | U I | ☑ |
| FR-017 | Custom trip fields everywhere | M | M03/M06 | CustomFieldDefinitions, TRN_Trips.CustomValues | CustomFieldService | SCR-10, SCR-11, SCR-12, SCR-52 | I E | ☑ |
| FR-018 | Trip statement for driver/customer/partner (print/export) | S | M12 | TRN_Trips | TripStatementReport | SCR-50, SCR-09 | I | ◐ |

### C.2 Pricing (7.2)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-019 | Tariff lines: add, edit, archive, import, export | M | M05 | TRN_Tariffs | TariffService | SCR-67 | I E | ☑ |
| FR-020 | Line matching incl. validity dates | M | M05 | TRN_Tariffs | TariffResolver | — (logic) | U | ☑ |
| FR-021 | Specificity scoring; tie → latest Valid from | M | M05 | TRN_Tariffs.Specificity, IX_TRN_Tariffs_Lookup | TariffResolver | — (logic) | U | ☑ |
| FR-022 | Winning line fills price/partner rate; trip stores tariff price | M | M05/M06 | TRN_Trips.TariffPrice/CustomerPrice/PartnerRate | TariffResolver | SCR-10, SCR-11, SCR-12 | U I | ☑ |
| FR-023 | Breach flag and amount | M | M06 | TRN_Trips.IsTariffBreach | TripCalculator | SCR-10, SCR-11 | U | ☑ |
| FR-024 | Breach report | M | M12 | TRN_Trips | Report RPT-04 | SCR-50 | I | ◐ |
| FR-025 | Tariff lock; deviations audited | M | M06 | CompanySettings TRN.LockTariffs, AuditLogs | TripValidator | SCR-10, SCR-11 | U I S | ☑ |
| FR-026 | Tariff history; changes never alter trips | M | M05 | TRN_RateHistory | TariffService | SCR-67 | I | ☑ |
| FR-027 | Monthly rates per month + site, own/external | M | M05 | TRN_MonthlySiteRates | MonthlyRateService | SCR-68 | I | ☑ |
| FR-028 | Month strip with status; enter for all active sites; save, print | M | M05 | TRN_MonthlySiteRates, TRN_LoadingSites | MonthlyRateService | SCR-68, SCR-09 | I E | ◐ |
| FR-029 | No inheritance from previous month | M | M05 | TRN_MonthlySiteRates | MonthlyRateService | SCR-68 | U | ☑ |
| FR-030 | New-trip rate resolution table | M | M05 | TRN_MonthlySiteRates, TRN_Trips.RateSource/IsManualRate | DriverRateResolver | SCR-10, SCR-11 | U | ☑ |
| FR-031 | Trip stores rate, source and month; frozen | M | M06 | TRN_Trips.DriverRate/RateSource/RateMonth | TripService | — (logic) | I | ☑ |
| FR-032 | Rate on edit kept unless site/month/driver change | M | M06 | TRN_Trips | TripService.Edit | SCR-11 | U | ☑ |
| FR-033 | Import rate rules | M | M06 | TRN_Trips, TRN_MonthlySiteRates | TripImportService | SCR-12 | U | ☑ |
| FR-034 | Warn when editing a month with trips | M | M05 | TRN_MonthlySiteRates, TRN_Trips | MonthlyRateService | SCR-68 | I | ☑ |
| FR-035 | Clearing both rates of a used site refused; rename keeps link | M | M05 | TRN_MonthlySiteRates (CHECK one rate), TRN_LoadingSites | MonthlyRateService | SCR-68 | I | ☑ |
| FR-036 | Rate history | M | M05 | TRN_RateHistory | MonthlyRateService | SCR-68 | I | ☑ |
| FR-037 | Dashboard warning: sites without current-month rates | S | M12 | TRN_MonthlySiteRates, TRN_LoadingSites | DashboardQueries | SCR-07 | I | ◐ |

### C.3 Invoicing, returns, layouts, e-invoicing (7.3)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-038 | Trip picker: customer's active unbilled trips, filters, select-all, live totals | M | M09 | TRN_Trips (IX_TRN_Trips_Unbilled) | InvoiceQueries.Unbilled | SCR-21 | I E | ☑ |
| FR-039 | Manual invoice lines | M | M09 | TRN_InvoiceLines | InvoiceService | SCR-22 | U I | ☑ |
| FR-040 | Totals live and stored at approval (8.3) | M | M09 | TRN_Invoices | InvoiceCalculator | SCR-21, SCR-22 | U (EX-2) | ☑ |
| FR-041 | Approval setting: Draft vs direct approve; Save & approve | M | M09 | CompanySettings TRN.InvoiceApproval | InvoiceService | SCR-21, SCR-22 | I E | ☑ |
| FR-042 | Draft reserves trips; edit, DRAFT print, approve, delete (soft) | M | M09 | TRN_Invoices, TRN_Trips.InvoiceId | InvoiceService | SCR-23, SCR-09 | I E | ☑ |
| FR-043 | Approval: gap-free number, post entry, trips invoiced, queue e-invoice | M | M09/M07/M10 | NumberSequences, JournalEntries, TRN_EInvoiceSubmissions | InvoiceService.Approve, NumberingService | SCR-23 | I (TST-10) | ☑ |
| FR-044 | Approval blocks (mapping, closed period, trip state, total ≤ 0) | M | M09 | PostingKeyMappings, Periods | InvoiceService.Approve | SCR-23 | U I | ☑ |
| FR-045 | Void invoice with reason; reversal; trips released; VOID stamp | M | M09/M07 | TRN_Invoices (Void*), JournalEntries | InvoiceService.Void | SCR-23 | I E | ☑ |
| FR-046 | Accepted e-invoice cannot be voided — full credit note | M | M09/M10 | TRN_EInvoiceSubmissions | InvoiceService.Void | SCR-23 | I | ☑ |
| FR-047 | Invoice list filters, totals, export | M | M09 | TRN_Invoices | InvoiceQueries.List | SCR-20 | I E | ☑ |
| FR-048 | Invoice view incl. receipts, credit notes, e-invoice status, history | M | M09 | TRN_Invoices, TRN_Receipts, TRN_CreditNotes, TRN_EInvoiceSubmissions, AuditLogs | InvoiceQueries.Detail | SCR-23 | I E | ☑ |
| FR-049 | Credit note from approved invoice; reason | M | M09 | TRN_CreditNotes, TRN_CreditNoteLines | CreditNoteService | SCR-24 | I E | ☑ |
| FR-050 | Proportional shares; ≤ remaining | M | M09 | TRN_CreditNotes | CreditNoteCalculator | SCR-24 | U (EX-3) | ☑ |
| FR-051 | Own sequence; mirror entry; trips unbilled | M | M09/M07 | NumberSequences, JournalEntries, TRN_Trips.InvoiceId | CreditNoteService | SCR-24 | I | ☑ |
| FR-052 | Void credit note; blocked if trip re-invoiced or closed | M | M09 | TRN_CreditNotes | CreditNoteService.Void | SCR-24 | I | ☑ |
| FR-053 | Credit note printout | M | M09 | TRN_CreditNotes | PrintService | SCR-09 | I | ☑ |
| FR-054 | Two layouts: claim statement (with annex) and tax invoice | M | M09 | TRN_InvoiceLayouts | InvoicePrintService | SCR-09 | I E | ☑ |
| FR-055 | Layout designer with live preview | M | M09 | TRN_InvoiceLayouts | InvoiceLayoutService | SCR-74 | I E | ☑ |
| FR-056 | Annex columns incl. custom trip fields | M | M09 | TRN_InvoiceLayouts.AnnexColumns, CustomFieldDefinitions | InvoicePrintService | SCR-74, SCR-09 | I | ☑ |
| FR-057 | Printed invoice content (parties, tax numbers, words, page X/Y, QR, signatures) | M | M09 | TRN_Invoices, Companies, TRN_Customers, TRN_EInvoiceSubmissions.QrCode | InvoicePrintService | SCR-09 | I E | ☑ |
| FR-058 | Print/PDF for draft/approved/void; every print logged | M | M09/M13 | AuditLogs (A-04) | PrintService | SCR-09 | I | ☑ |
| FR-059 | E-invoicing company settings; credentials encrypted | M | M10 | EInvoiceSettings | EInvoiceSettingsService | SCR-73 | I S | ☑ |
| FR-060 | Build national format and validate before submission | M | M10 | TRN_EInvoiceSubmissions.RequestPayload | JoFotaraConnector (UBL 2.1) | SCR-25 | U | ☑ |
| FR-061 | Automatic and manual submission; retries; Rejected/Failed list | M | M10 | TRN_EInvoiceSubmissions | EInvoiceSubmissionJob | SCR-25 | I | ☑ |
| FR-062 | Store unique ID, system number, QR, time; QR printed | M | M10 | TRN_EInvoiceSubmissions | EInvoiceSubmissionJob | SCR-25, SCR-09 | I | ☑ |
| FR-063 | Status values; reason; correct and resubmit | M | M10 | TRN_EInvoiceSubmissions.Status | EInvoiceService (C-04, C-05) | SCR-25, SCR-23 | I E | ☑ |
| FR-064 | Credit notes reference original; only after acceptance | M | M10 | TRN_EInvoiceSubmissions | EInvoiceService | SCR-24, SCR-25 | U I | ☑ |
| FR-065 | E-invoicing report | M | M12 | TRN_EInvoiceSubmissions, ApArEInvoiceSubmissions | Report RPT-22 | SCR-25, SCR-51 | I | ☑ |
| FR-066 | Connector per company | S | M10 | EInvoiceSettings.Connector | IEInvoiceConnector | SCR-73 | U | ☑ |

### C.4 Cash, drivers & partners (7.4)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-067 | Record receipt; numbered; posts Dr cash/bank Cr receivables | M | M11/M07 | TRN_Receipts, NumberSequences, JournalEntries | ReceiptService | SCR-26 | I E | ☑ |
| FR-068 | Against invoice / on account | M | M11 | TRN_Receipts.InvoiceId, TRN_Invoices.Paid/Remaining | ReceiptService | SCR-26 | U I | ☑ |
| FR-069 | Void receipt; balance restored | M | M11 | TRN_Receipts (Void*) | ReceiptService.Void | SCR-26 | I | ☑ |
| FR-070 | Receipt voucher | M | M11 | TRN_Receipts | PrintService, AmountInWords | SCR-09 | U I | ☑ |
| FR-071 | Receipts list | M | M11 | TRN_Receipts | ReceiptQueries | SCR-26 | I | ☑ |
| FR-072 | Record advance; numbered; posts | M | M11/M07 | TRN_Advances, NumberSequences, JournalEntries | AdvanceService | SCR-27 | I E | ☑ |
| FR-073 | Edit advance while unsettled; reverse + re-post | M | M11 | TRN_Advances | AdvanceService.Edit | SCR-27 | I | ☑ |
| FR-074 | Void advance | M | M11 | TRN_Advances | AdvanceService.Void | SCR-27 | I | ☑ |
| FR-075 | Payment voucher | M | M11 | TRN_Advances | PrintService | SCR-09 | I | ☑ |
| FR-076 | Driver account view | M | M11 | TRN_Trips, TRN_Advances, TRN_Settlements | DriverAccountQueries | SCR-27 | I | ☑ |
| FR-077 | Create settlement; pre-select open items up to date | M | M11 | TRN_Settlements, TRN_Trips (IX_TRN_Trips_Unsettled), TRN_Advances | SettlementService | SCR-27 | I E | ☑ |
| FR-078 | Deduction lines; shortage suggestion (G-16) | M | M11 | TRN_SettlementDeductions | SettlementService | SCR-27 | U I | ☑ |
| FR-079 | Net ≥ 0; method, cash box/bank | M | M11 | TRN_Settlements (CHECK net) | SettlementCalculator | SCR-27 | U (EX-4) | ☑ |
| FR-080 | Confirm: number, link/lock items, post | M | M11/M07 | TRN_Settlements, NumberSequences, JournalEntries | SettlementService.Confirm | SCR-27 | I | ☑ |
| FR-081 | Void settlement; release items | M | M11 | TRN_Settlements, TRN_Trips.SettlementId, TRN_Advances.SettlementId | SettlementService.Void | SCR-27 | I | ☑ |
| FR-082 | Settlement statement + payment voucher | M | M11 | TRN_Settlements | PrintService | SCR-09 | I | ☑ |
| FR-083 | Driver statement | M | M12 | TRN_Trips, TRN_Advances, TRN_Settlements, TRN_SettlementDeductions | Report RPT-13 | SCR-27, SCR-51 | I | ☑ |
| FR-084 | Partner account view | M | M11 | TRN_Trips, TRN_PartnerPayments | PartnerAccountQueries | SCR-28 | I | ☑ |
| FR-085 | Partner payment; ≤ balance due | M | M11/M07 | TRN_PartnerPayments, JournalEntries | PartnerPaymentService | SCR-28 | U I E | ☑ |
| FR-086 | Void partner payment | M | M11 | TRN_PartnerPayments | PartnerPaymentService.Void | SCR-28 | I | ☑ |
| FR-087 | Partner payment voucher | M | M11 | TRN_PartnerPayments | PrintService | SCR-09 | I | ☑ |
| FR-088 | Partner statement | M | M12 | TRN_Trips, TRN_PartnerPayments | Report RPT-14 | SCR-28, SCR-51 | I | ☑ |
| FR-089 | Cash/bank documents dated, own gap-free sequence, closed-period block | M | M11 | NumberSequences, Periods | NumberingService, PeriodGuard | SCR-26, SCR-27, SCR-28 | I | ☑ |
| FR-090 | Reprint marked Copy and logged | M | M11/M13 | AuditLogs | PrintService | SCR-09 | I | ☑ |
| FR-091 | Cash and bank balances on dashboard and statement | M | M12 | EntryLines, CashBoxes, BankAccounts | DashboardQueries, Report RPT-16 | SCR-07, SCR-51 | I | ◐ |

### C.5 Ledger integration (7.5)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-092 | Account mapping screen with status | M | M07 | PostingKeyMappings | PostingKeyService | SCR-71 | I E | ◐ |
| FR-093 | Only active postable accounts; mapped account cannot be deactivated | M | M07/M08 | PostingKeyMappings, Accounts | PostingKeyService, AccountService | SCR-71, SCR-30 | I | ◐ |
| FR-094 | Unmapped → invoicing/posting disabled; banner | M | M07 | PostingKeyMappings | PostingGuard | SCR-00 (banner), SCR-71 | I E | ◐ |
| FR-095 | Mapping changes for new entries only; audited | M | M07 | PostingKeyMappings, AuditLogs | PostingKeyService | SCR-71 | I | ◐ |
| FR-096 | Every financial event posts in the same operation | M | M07 | JournalEntries, EntryLines | PostingEngine | — (engine) | I (NFR-04) | ◐ |
| FR-097 | Entry fields incl. source and party | M | M07 | JournalEntries, EntryLines | PostingEngine | SCR-34 | I | ◐ |
| FR-098 | Balanced and open period, else refused | M | M07 | JournalEntries (CHECK debit = credit), Periods | PostingEngine | — | U I | ◐ |
| FR-099 | Linked reversal; originals unchanged | M | M07 | JournalEntries.ReversesEntryId/ReversedByEntryId | PostingEngine.Reverse | SCR-34 | I | ◐ |
| FR-100 | Accrue driver wage setting | M | M07 | CompanySettings TRN.AccrueDriverWage | PostingEngine | SCR-70 | U I | ☐ |
| FR-101 | Pending postings with retry | M | M07 | PendingPostings | PendingPostingJob | SCR-34, SCR-07 | I | ◐ |
| FR-102 | Automatic entries list | M | M07 | JournalEntries, EntryLines | JournalQueries | SCR-34 | I | ◐ |
| FR-103 | Journal voucher | M | M07 | JournalEntries, EntryLines | PrintService | SCR-09 | I | ◐ |
| FR-104 | Journal export batches JSON/CSV | M | M07/M14 | JournalExportBatches, JournalEntries.ExportBatchId | JournalExportService | SCR-34 | I | ◐ |
| FR-105 | Sub-ledger reconciliation | M | M12 | EntryLines (IX_EntryLines_Party), Accounts | Report RPT-23 | SCR-51 | I (TST-11) | ☑ |
| FR-106 | Control-account lines need a party | M | M07/M08 | Accounts.IsControlAccount/ControlPartyType, EntryLines.Party* | JournalValidator (G-17) | SCR-32 | U | ◐ |

### C.6 Accounting (7.6)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-107 | Chart as tree; template for new companies | M | M08 | Accounts | AccountService, ChartTemplateSeeder (G-03) | SCR-30 | I E | ◐ |
| FR-108 | Referenced account only deactivated; parent with children not postable | M | M08 | Accounts | AccountService | SCR-30 | I | ◐ |
| FR-109 | Chart import/export with opening columns; preview | M | M08/M14 | Accounts, ImportBatches | ChartImportService | SCR-30 | I | ◐ |
| FR-110 | Opening entry; difference account during setup only | M | M08 | Accounts.Opening*, Companies.MigrationCompletedAt, JournalEntries (SourceType Opening) | OpeningBalanceService (G-04) | SCR-30, SCR-07, SCR-36, SCR-38 | U I | ◐ |
| FR-111 | Manual entries | M | M08 | JournalEntries (IsManual), EntryLines | ManualJournalService | SCR-32 | I E | ◐ |
| FR-112 | Edit posted manual entries with history | M | M08 | JournalEntries, AuditLogs | ManualJournalService.Edit (C-06) | SCR-32 | I | ◐ |
| FR-113 | Void manual entries by reversal | M | M08 | JournalEntries (Void*) | ManualJournalService.Void | SCR-32 | I | ◐ |
| FR-114 | Import entries with preview checks | M | M08/M14 | JournalEntries, ImportBatches | JournalImportService | SCR-33 | U I | ◐ |
| FR-115 | Journal list, export, voucher | M | M08 | JournalEntries, EntryLines | JournalQueries | SCR-31, SCR-09 | I | ◐ |
| FR-116 | General ledger | M | M08 | EntryLines, Accounts | GeneralLedgerReport | SCR-35 | I | ☑ |
| FR-117 | Trial balance at tree level | M | M08 | EntryLines, Accounts | TrialBalanceReport | SCR-36 | I | ☑ |
| FR-118 | Profit & loss; cost-centre filter | M | M08 | EntryLines, Accounts, CostCentres | ProfitLossReport | SCR-37 | I | ☑ |
| FR-119 | Balance sheet with current-period result | M | M08 | EntryLines, Accounts | BalanceSheetReport | SCR-38 | I | ☑ |
| FR-120 | Account statement, optionally by party | M | M08 | EntryLines | Report RPT-15 | SCR-39 | I | ☑ |
| FR-121 | Drill-down; Excel/PDF | M | M08/M12 | — (navigation) | ReportDrillDown | SCR-35…39 | E | ☑ |
| FR-122 | Accounting parties; transport customers as receivable parties | M | M08 | ApArParties, TRN_Customers.ApArPartyId | ApArPartyService | SCR-40, SCR-41 | I | ☑ |
| FR-123 | Supplier bills; post | M | M08/M07 | ApArDocuments (SupplierBill), ApArDocumentLines | ApArDocumentService | SCR-40 | I | ☑ |
| FR-124 | Receivable documents; post | M | M08/M07 | ApArDocuments (Receivable), ApArDocumentLines, ApArEInvoiceSubmissions (D-15) | ApArDocumentService | SCR-41 | I | ☑ |
| FR-125 | Payments and collections | M | M08 | ApArPayments | ApArPaymentService | SCR-40, SCR-41 | I | ☑ |
| FR-126 | Returns on AP/AR documents | M | M08 | ApArReturns | ApArReturnService | SCR-40, SCR-41 | I | ☑ |
| FR-127 | Printed AP/AR documents; void rules | M | M08 | ApArDocuments, ApArPayments, ApArReturns | ApArDocumentService.Void, PrintService | SCR-40, SCR-41, SCR-09 | I | ☑ |
| FR-128 | Payables and receivables ageing | M | M12 | ApArDocuments | Report RPT-17/18 | SCR-51 | U I | ☑ |
| FR-129 | Cash boxes and bank accounts | M | M08 | CashBoxes, BankAccounts | CashBankService | SCR-42 | I | ☑ |
| FR-130 | Cash/bank statement | M | M12 | EntryLines | Report RPT-16 | SCR-42, SCR-51 | I | ☑ |
| FR-131 | Cost centres and report | M | M08 | CostCentres, EntryLines | CostCentreService | SCR-43 | I | ☑ |
| FR-132 | Period list; close/reopen with reason | M | M08 | Periods | PeriodService | SCR-44 | I E | ◐ |
| FR-133 | Closed period blocks everything everywhere | M | M08/X | Periods | PeriodGuard (used by every document service) | — (rule) | I | ◐ |
| FR-134 | Pre-close check | S | M08 | PendingPostings, TRN_Invoices (Draft), EntryLines | PeriodService.PreCloseCheck | SCR-44 | I | ◐ |
| FR-135 | Maintenance actions (soft deletions) | M | M08 | JournalEntries, EntryLines, Accounts, MaintenanceLogs | MaintenanceService | SCR-45 | I S | ☑ |
| FR-136 | Maintenance protections and log | M | M08 | MaintenanceLogs | MaintenanceService | SCR-45 | I E | ☑ |

### C.7 Reports & dashboard (7.7)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-137 | Today / month-to-date figures | M | M12 | TRN_Trips, TRN_Invoices, TRN_Receipts | DashboardQueries | SCR-07 | I P | ☑ |
| FR-138 | Alert cards opening filtered lists | M | M12 | TRN_Drivers (IX_TRN_Drivers_Expiry), TRN_Trips, TRN_Invoices, TRN_EInvoiceSubmissions, PendingPostings, TRN_MonthlySiteRates | DashboardQueries | SCR-07 | I E | ☑ |
| FR-139 | Cash/bank balance cards | M | M12 | EntryLines, CashBoxes, BankAccounts | DashboardQueries | SCR-07 | I | ☑ |
| FR-140 | Pins with thresholds | M | M12 | DashboardPins, SavedReports | DashboardPinService | SCR-07 | I E | ☑ |
| FR-141 | Cards by permission; live refresh | M | M12/M13 | — | DashboardQueries, RealtimeHub | SCR-07 | S E | ☑ |
| FR-142 | All standard reports RPT-01…23 | M | M12 | (per report) | ReportCatalog | SCR-50, SCR-51 | I | ☑ |
| FR-143 | Report builder data sources incl. custom fields | M | M12 | SavedReports, CustomFieldDefinitions | ReportBuilderEngine | SCR-52 | I | ☑ |
| FR-144 | Grouping, measures, filters, sort, top-N, detail | M | M12 | SavedReports.Definition | ReportBuilderEngine | SCR-52 | U I | ☑ |
| FR-145 | Private/shared; edit rights | M | M12 | SavedReports.IsShared/OwnerUserId | SavedReportService | SCR-53 | I S | ☑ |
| FR-146 | Run, export, pin | M | M12 | SavedReports, DashboardPins | SavedReportService | SCR-53 | E | ☑ |
| FR-147 | Data source needs its view permission | M | M12 | — | ReportBuilderEngine | SCR-52 | S | ☑ |
| FR-148 | Period/as-of, filters, selected company, totals | M | M12 | — | ReportBase | SCR-50, SCR-51 | I | ☑ |
| FR-149 | Drill-down from every line | M | M12 | — | ReportDrillDown | SCR-50, SCR-51 | E | ☑ |
| FR-150 | Excel/PDF of every screen and report; print header/footer, RTL | M | M14 | — | ExportService, PdfRenderer | All list/report screens, SCR-09 | I E | ☑ |
| FR-151 | Large exports in background with notification | S | M14 | Notifications | ExportJob | SCR-00 (notifications) | I | ☑ |

### C.8 Master data (7.8)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-152 | Drivers with truck fields | M | M04 | TRN_Drivers | DriverService | SCR-60 | U I E | ☑ |
| FR-153 | Truck changes in history; trips keep fleet and partner | M | M04 | AuditLogs, TRN_Trips.FleetClass/PartnerId/TruckPlateAtSave | DriverService | SCR-60 | I | ◐ |
| FR-154 | Document expiry alerts (30 days) | M | M04/M12 | TRN_Drivers (IX_TRN_Drivers_Expiry) | DriverQueries, DashboardQueries | SCR-60, SCR-07 | I | ◐ |
| FR-155 | Expired document warning on trip; save allowed | M | M06 | TRN_Drivers | TripValidator | SCR-10, SCR-11 | U | ◐ |
| FR-156 | Driver profile with account summary | M | M04/M11 | TRN_Drivers, TRN_Trips, TRN_Advances, TRN_Settlements | DriverQueries.Profile | SCR-60 | I | ◐ |
| FR-157 | Customers and profile | M | M04 | TRN_Customers | CustomerService | SCR-61 | I E | ◐ |
| FR-158 | Partners and profile | M | M04 | TRN_Partners | PartnerService | SCR-62 | I | ◐ |
| FR-159 | Materials | M | M04 | TRN_Materials | MaterialService | SCR-63 | I | ☑ |
| FR-160 | Loading and unloading sites | M | M04 | TRN_LoadingSites, TRN_UnloadingSites | SiteService | SCR-64, SCR-65 | I | ☑ |
| FR-161 | Ownership types; built-in Company protected | M | M04 | TRN_OwnershipTypes.IsBuiltIn | OwnershipTypeService (C-08) | SCR-66 | I | ☑ |
| FR-162 | Referenced records not deleted; counts; archive; restore | M | M04/X | All master tables (FKs NO ACTION), Status | ReferenceGuard | SCR-60…66 | I E | ☑ |
| FR-163 | Rename cascade; issued documents keep names | M | M04 | (FK by Id; printed snapshots) | PrintService | SCR-63…65 | I | ◐ |
| FR-164 | Master-data import/export with matching and duplicates | M | M04/M14 | ImportBatches | MasterDataImportService | SCR-60…68 | I | ☑ |
| FR-165 | Lists with search, filters, sort, columns, export | M | M04 | TableColumnViews | MasterDataQueries | SCR-60…68 | I E | ◐ |
| FR-166 | History of every master record | M | M04/M13 | AuditLogs | HistoryQueries | SCR-60…68 | I | ☑ |
| FR-167 | Trip form selects only existing active master data | M | M06 | — | TripValidator | SCR-10, SCR-11 | U E | ☐ |

### C.9 Administration & configuration (7.9)

| ID | Requirement (short) | Pri | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|---|
| FR-168 | Settings screen; changes audited | M | M03 | Companies, CompanySettings, AuditLogs | CompanySettingsService | SCR-70 | I E | ◐ |
| FR-169 | Next number never at/below issued; prefix for new docs | M | M03 | NumberSequences | NumberingService | SCR-72 | U I | ◐ |
| FR-170 | Weight-basis change applies to new trips after confirmation | M | M03 | CompanySettings | CompanySettingsService | SCR-70 | I | ◐ |
| FR-171 | Mark data migration complete (irreversible) | M | M03/M08 | Companies.MigrationCompletedAt | CompanySettingsService | SCR-70 | I | ◐ |
| FR-172 | Invitations; 7-day expiry; resend/cancel | M | M02 | Invitations | InvitationService, IEmailSender | SCR-76 | I E | ◐ |
| FR-173 | Preset/custom roles; delete rules | M | M02 | Roles, RolePermissions, UserCompanyRoles | RoleService | SCR-76 | I | ◐ |
| FR-174 | Edit, disable, unlock, reset, archive users; BR-001…003 | M | M02 | Users, UserCompanyRoles | UserAdminService | SCR-76 | I S | ◐ |
| FR-175 | Password policy; 5 failures → 15-min lock | M | M02 | Users.FailedSignInCount/LockedUntil (G-01) | Identity options, UserStore | SCR-01 | I S | ◐ |
| FR-176 | Session expiry; active sessions; sign out all | M | M02 | UserSessions (G-01) | SessionService | SCR-08, SCR-76 | I S | ◐ |
| FR-177 | Mandatory 2FA; recovery codes; reset audited | M | M02 | Users.TwoFactorSecret/TwoFactorEnabledAt, UserRecoveryCodes | TwoFactorService | SCR-01, SCR-05, SCR-76 | I S E | ◐ |
| FR-178 | User preferences | M | M02 | Users.LanguageCode/DefaultCompanyId/DateFormat | UserProfileService | SCR-08 | I | ◐ |
| FR-179 | Custom fields definition | M | M03 | CustomFieldDefinitions | CustomFieldService | SCR-75 | I | ☑ |
| FR-180 | Custom fields appear everywhere | M | M03 | CustomFieldDefinitions, *.CustomValues | CustomFieldService | SCR-10…12, SCR-60, SCR-61, SCR-52, SCR-09 | I E | ◐ |
| FR-181 | Field with data: hide only, type fixed | M | M03 | CustomFieldDefinitions | CustomFieldService | SCR-75 | I | ☑ |
| FR-182 | Standard fields rename/hide/require/reorder; core fields never hidden | M | M03 | StandardFieldConfigs | FieldConfigService | SCR-75 | U I | ☑ |
| FR-183 | Default and personal column views | M | M03 | TableColumnViews | ColumnViewService | SCR-10, SCR-75 | I | ☑ |
| FR-184 | Preview configured form | S | M03 | — | — (UI) | SCR-75 | E | ◐ |
| FR-185 | Live changes within seconds | M | M13 | — | RealtimeHub (SignalR) | SCR-00, all lists | I E | ☑ |
| FR-186 | Record locking; read-only for others | M | M13 | RecordLocks | RecordLockService | All edit forms | I E | ☑ |
| FR-187 | Global search with Arabic variants; respects permissions | M | M13 | TRN_Trips, TRN_Drivers, TRN_Customers, TRN_Partners, document tables | GlobalSearchService (A-12) | SCR-00 | U I S | ☑ |
| FR-188 | Company audit log of all listed actions | M | M13/X | AuditLogs | AuditInterceptor, AuditService | SCR-77 | I | ☑ |
| FR-189 | Audit log read-only; filters; export | M | M13 | AuditLogs | AuditQueries | SCR-77 | I S | ☑ |
| FR-190 | In-app notifications | S | M13 | Notifications | NotificationService | SCR-00 | I | ☑ |

---

## D. User stories (each is covered by the FRs listed; acceptance tested E2E in step 9)

| ID | Story | Covered by | Screens | Tests | St |
|---|---|---|---|---|---|
| US-01 | Quick trip entry | FR-001, 005–008, 022, 030 | SCR-10 | E | ☑ |
| US-02 | Bulk trip import | FR-013, 033 | SCR-12 | E | ☑ |
| US-03 | Void a trip | FR-011 | SCR-11 | E | ☑ |
| US-04 | Weight shortage tracking | FR-012, RPT-03 | SCR-13, SCR-50 | E | ◐ |
| US-05 | Driver wage from monthly site rates | FR-027–036 | SCR-68, SCR-10 | E | ☑ |
| US-06 | Customer and partner tariffs | FR-019–026 | SCR-67 | E | ☑ |
| US-07 | Invoice unbilled trips | FR-038, 040, 044 | SCR-21 | E | ☐ |
| US-08 | Manual invoice | FR-039 | SCR-22 | E | ☐ |
| US-09 | Draft and approval | FR-041–043 | SCR-23 | E | ☐ |
| US-10 | Credit note | FR-049–053 | SCR-24 | E | ☐ |
| US-11 | Void an invoice | FR-045, 046 | SCR-23 | E | ☐ |
| US-12 | Claim statement with annex | FR-054–058 | SCR-74, SCR-09 | E | ☐ |
| US-13 | National e-invoicing | FR-059–066 | SCR-73, SCR-25 | E | ☐ |
| US-14 | Customer receipt | FR-067–071 | SCR-26 | E | ☐ |
| US-15 | Driver advance | FR-072–075 | SCR-27 | E | ☐ |
| US-16 | Driver settlement | FR-076–083 | SCR-27 | E | ☐ |
| US-17 | Partner shipments and payments | FR-004, 084–088 | SCR-28 | E | ☐ |
| US-18 | Automatic journal entries | FR-096–101 | SCR-34 | E | ◐ |
| US-19 | Period closing | FR-132–134 | SCR-44 | E | ◐ |
| US-20 | Accounting operations | FR-107–131 | SCR-30…43 | E | ◐ |
| US-21 | Dashboard | FR-137–141 | SCR-07 | E | ☑ |
| US-22 | Account statements and ageing | RPT-12…18 | SCR-51 | E | ☐ |
| US-23 | Report builder | FR-143–147 | SCR-52, SCR-53 | E | ☑ |
| US-24 | Driver records with truck details | FR-152–156 | SCR-60 | E | ◐ |
| US-25 | Protected deletion | FR-162, 163 | SCR-60…66 | E | ☑ |
| US-26 | Custom fields | FR-179–181 | SCR-75 | E | ☑ |
| US-27 | Configure standard fields | FR-182, 183 | SCR-75 | E | ☑ |
| US-28 | Real-time teamwork | FR-185, 186, BR-038 | SCR-00 | E | ☑ |
| US-29 | Global search | FR-187 | SCR-00 | E | ☑ |
| US-30 | Record history and audit | FR-015, 166, 188, 189 | SCR-77, history panels | E | ☐ |
| US-31 | Sign-up and guided setup | 5.4, G-10 | SCR-02, SCR-03, SCR-05, SCR-06 | E | ☐ |
| US-32 | Invite users and assign roles | FR-172–174 | SCR-76 | E | ◐ |
| US-33 | Manage subscription | 5.3, BR-012 | SCR-80 | E | ☐ |
| US-34 | Multiple companies | 5.1 | SCR-00, SCR-81 | E S | ☐ |

## E. Tenancy, subscription & platform (Section 5 — no FR numbers in the PRD; numbered here as P-xx)

| ID | Requirement | Mod | DB | Code | Screens | Tests | St |
|---|---|---|---|---|---|---|---|
| P-01 | Tenant → companies → users; tenant and company isolation; company switcher (5.1) | M01/X | Tenants, Companies, UserCompanyRoles; TenantId/CompanyId on every table | TenantContext, global query filters | SCR-00, SCR-81 | S I | ◐ |
| P-02 | Tenant lifecycle Trial 14 d / Active / PastDue 7 d / Suspended / Cancelled 30 d → retention 90 d / Deleted (5.2) | M01 | Tenants (Status, TrialEndsAt, GraceEndsAt, CancelledAt, RetentionEndsAt) | TenantLifecycleJob (G-07, G-18) | SCR-80, SCR-90 | U I | ☐ |
| P-03 | Plans with limits, defined in the console (5.3) | M01 | Plans | PlanService | SCR-91 | I | ☐ |
| P-04 | Billing monthly/annual in advance via gateway (5.3) | M01 | Tenants.BillingCycle, SubscriptionInvoices | BillingService, IPaymentGateway (dev fake) | SCR-80 | I | ☐ |
| P-05 | Subscription tax invoice per charge; history downloadable (5.3) | M01 | SubscriptionInvoices | BillingService | SCR-80 | I | ☐ |
| P-06 | Upgrade immediately pro-rated; downgrade at renewal if usage fits (5.3) | M01 | Tenants, Plans (G-05) | PlanChangeService | SCR-80 | U I | ☐ |
| P-07 | Limit reached → creation blocked with message and upgrade option (5.3) | M01 | Plans | PlanLimitGuard | SCR-00 | U I | ☐ |
| P-08 | Onboarding checklist, 8 steps, any order, resumable (5.4) | M01 | CompanySettings CORE.Setup.* (G-10) | SetupChecklistService | SCR-06 | I E | ☐ |
| P-09 | Demo company with sample data (5.4) | M01 | Companies, CompanySettings CORE.IsDemo (G-11) | DemoCompanySeeder | SCR-81 | I | ☐ |
| P-10 | Historical import and opening balances (5.4) | M14/M08 | ImportBatches | (FR-013, FR-109) | SCR-12, SCR-30 | I | ☐ |
| P-11 | Provider console: tenants with usage (5.5) | M01 | Tenants, Plans | ProviderTenantQueries | SCR-90 | I S | ☐ |
| P-12 | Account actions: extend trial, apply credit, suspend/reactivate, audited (5.5) | M01 | Tenants, SubscriptionInvoices, AuditLogs | ProviderTenantService | SCR-90 | I | ☐ |
| P-13 | Support access requests, Owner approval, time-limited, logged (5.5) | M01 | SupportAccessGrants, AuditLogs | SupportAccessService (G-08, G-09) | SCR-92, SCR-83 | I S | ☐ |
| P-14 | Service announcements (5.5) | M01 | Announcements | AnnouncementService | SCR-93, SCR-00 | I | ☐ |
| P-15 | Platform health metrics (5.5) | M01 | — (metrics, health checks) | HealthEndpoints | SCR-94 | I | ☐ |
| P-16 | Full company export: Excel + JSON package (5.6, DX-04) | M14 | all company tables | CompanyExportJob | SCR-82 | I | ☐ |
| P-17 | Sign-up creates tenant, Owner, first company; trial starts (9.1) | M01/M02 | Tenants, Users, Companies, Roles, RolePermissions, UserCompanyRoles, TermsAcceptances | SignUpService | SCR-02 | I E | ◐ |

## F. Business rules

| ID | Rule | Mod | DB enforcement | Code | Tests | St |
|---|---|---|---|---|---|---|
| BR-001 | ≥ 1 active Owner per tenant | M02 | — | UserAdminService | U I | ☑ |
| BR-002 | ≥ 1 active user with users.manage per company | M02 | — | UserAdminService | U I | ☑ |
| BR-003 | Users with actions never deleted; disable/archive | M02 | Users.Status | UserAdminService | I | ☑ |
| BR-004 | Manual rate / tariff deviation under lock need masters.manage | M06 | — | TripValidator | U S | ☐ |
| BR-010 | Suspended tenant read-only | M01 | — | TenantStatusGuard | I S | ◐ |
| BR-011 | Reactivation restores access, no data change | M01 | — | TenantLifecycleService | I | ☐ |
| BR-012 | Downgrade refused while over limits (listed) | M01 | — | PlanChangeService | U | ☐ |
| BR-013 | Limits never block view/print/export | M01 | — | PlanLimitGuard | U | ☐ |
| BR-014 | Provider access only via approved window, logged | M01 | SupportAccessGrants (CHECK EndsAt > StartsAt) | SupportAccessGuard | S | ☐ |
| BR-020 | Smallest currency unit; currency decimals | X | DECIMAL(18,3); Companies.CurrencyDecimals | Money type | U | ◐ |
| BR-021 | Half-up rounding at each amount | X | — | Money.Round | U | ◐ |
| BR-022 | Tax exclusive default 16 %; company setting | M03 | Companies.TaxRate/TaxInclusive defaults | InvoiceCalculator | U | ☑ |
| BR-023 | Tax = round(base × rate) once | M09 | TRN_Invoices CHECKs | InvoiceCalculator | U (EX-2) | ☑ |
| BR-024 | Amount in Arabic words | X | — | AmountInWords | U | ◐ |
| BR-025 | Credit-note proportional shares | M09 | — | CreditNoteCalculator | U (EX-3) | ☑ |
| BR-026 | Credit note ≤ remaining | M09 | TRN_Invoices.RemainingAmount CHECK ≥ 0 | CreditNoteService | U I | ☑ |
| BR-027 | Full return absorbs rounding → 0.000 | M09 | — | CreditNoteCalculator | U | ☑ |
| BR-028 | Tariff most-specific wins | M05 | TRN_Tariffs.Specificity, UX on keys + ValidFrom | TariffResolver | U | ☑ |
| BR-029 | Driver rate by company + site + month + fleet class | M05 | UX_TRN_MonthlySiteRates_… | DriverRateResolver | U | ☑ |
| BR-030 | Rate frozen at save | M06 | TRN_Trips stores rates | TripService | I | ☑ |
| BR-031 | Item in only one active settlement | M11 | TRN_Trips.SettlementId, TRN_Advances.SettlementId | SettlementService | I | ☑ |
| BR-031a | Accrual off → settlement debits wage expense | M07 | CompanySettings TRN.AccrueDriverWage | PostingEngine | U I | ☑ |
| BR-032 | Settled advance locked | M11 | TRN_Advances.SettlementId | AdvanceService | I | ☑ |
| BR-033 | Entries balance | M07 | JournalEntries CHECK TotalDebit = TotalCredit | PostingEngine | U I | ◐ |
| BR-034 | Entry dated with source date; closed period refused | M07 | Periods | PostingEngine, PeriodGuard | I | ◐ |
| BR-035 | Edit trip/advance = reverse + new entry | M07 | JournalEntries reversal links | PostingEngine | I | ☐ |
| BR-036 | Calendar-month periods; closed = no activity anywhere | M08 | Periods | PeriodGuard | I | ◐ |
| BR-037 | Separate sequences with prefixes | M03 | NumberSequences (G-19) | NumberingService | I | ◐ |
| BR-038 | Server numbers at approval, gap-free under concurrency | M03 | NumberSequences (UPDLOCK, ROWLOCK), UX numbers | NumberingService | I (TST-10) | ☑ |
| BR-038a | Next number never ≤ issued | M03 | — | NumberingService | U | ☑ |
| BR-039 | Lock on edit; release on close or 90 s idle | M13 | RecordLocks (UX active lock) | RecordLockService | I | ◐ |
| BR-040 | No delete while referenced; counts; archive/void | X | All FKs ON DELETE NO ACTION | ReferenceGuard | I | ◐ |
| BR-041 | Soft delete for all entities; restore; audited | X | IsDeleted/DeletedAt/DeletedBy/DeleteReason + CHECK; filtered UX | SoftDeleteInterceptor, global filters | I S | ☑ |

Section 8.9 validation rules are covered by FR-002, FR-152, FR-157, FR-158, FR-068, FR-050, FR-079, FR-085, FR-110, FR-155 (same tests).

## G. User flows (Section 9) — E2E in step 9 (TST-03)

| ID | Flow | Main FRs | Screens | Tests | St |
|---|---|---|---|---|---|
| 9.1 | Sign-up & first-time setup | P-17, P-08, FR-107, 110, 092, 152…160, 019, 027, 172, 171 | SCR-02…06, SCR-30, SCR-71, SCR-60…68, SCR-76 | E | ☐ |
| 9.2 | Month-start monthly rates | FR-027…037 | SCR-68 | E | ☐ |
| 9.3 | Record a trip (quick entry) | FR-001…016 | SCR-10 | E | ☐ |
| 9.4 | Import trips | FR-013, 033 | SCR-12 | E | ☐ |
| 9.5 | Edit or void a trip | FR-010, 011 | SCR-11 | E | ☐ |
| 9.6 | Invoice a customer | FR-038…048 | SCR-21, SCR-23 | E | ☐ |
| 9.7 | E-invoicing submission | FR-059…065 | SCR-25 | E | ☐ |
| 9.8 | Credit note or void invoice | FR-045, 049…053 | SCR-24, SCR-23 | E | ☐ |
| 9.9 | Customer receipt | FR-067…070 | SCR-26 | E | ☐ |
| 9.10 | Driver advance & settlement | FR-072…082 | SCR-27 | E | ☐ |
| 9.11 | Partner payment | FR-085, 087 | SCR-28 | E | ☐ |
| 9.12 | Month-end close | FR-132…134 | SCR-44 | E | ☐ |
| 9.13 | Concurrent work | FR-185, 186, BR-038 | SCR-00 | E I | ☐ |
| 9.14 | Configure fields | FR-179…184 | SCR-75 | E | ☐ |

## H. Non-functional requirements

| ID | Requirement | Mod | DB / Code | Screens | Tests | St |
|---|---|---|---|---|---|---|
| NFR-01 | Screens/saves < 1 s; monthly reports < 3 s; big exports background | X | indexes per design; paging; ExportJob | N/A — applies to all | P | ☐ |
| NFR-02 | 25 users, 500,000 trips/company; scale out | X | stateless app; SignalR backplane-ready | N/A — non-visual | P | ☐ |
| NFR-03 | 99.5 %; maintenance notice 48 h; zero-downtime | M01 | Announcements; hosting deferred (DEF-01) | SCR-93, SCR-00 | O | ☐ |
| NFR-04 | Document + entry in one transaction; idempotent submits | M07/X | single DbContext transaction; G-13 | N/A — non-visual | I | ☐ |
| NFR-05 | Encrypted backups, RPO 15 min, RTO 4 h | X | hosting deferred; documented procedure | N/A — operations | O (TST-07) | ☐ |
| NFR-06 | TLS, encryption at rest, server-side isolation | X | HSTS/HTTPS; TDE at deployment; filters | N/A | S | ☐ |
| NFR-07 | Arabic-first RTL; keyboard; field-specific errors | X | UI components; problem details | all | E | ☐ |
| NFR-08 | Browsers; A4 Arabic print/PDF; Excel compatibility | X | PdfRenderer, ExcelWriter | SCR-09 | E | ☐ |
| NFR-09 | New market = configuration + connector | X | Companies market fields, EInvoiceSettings.Connector | SCR-70, SCR-73 | U | ☐ |
| NFR-10 | Audit and financial records ≥ 7 years, unalterable | X | AuditLogs append-only; soft delete only | N/A | I | ☐ |
| NFR-11 | .NET 10, latest C#/ASP.NET Core | X | solution targets net10.0 | N/A | build | ☑ |
| NFR-12 | Blazor Web App, RTL + English | X | Blazor Web App | all | E | ☐ |
| NFR-13 | SQL Server 2025 + latest EF Core, versioned migrations | X | EF Core 10 migrations | N/A | I | ☑ |
| NFR-14 | Layered architecture; rules in domain/application | X | step 2 | N/A | review | ◐ |
| NFR-15 | Versioned API with OpenAPI | X | /api/v1, OpenAPI document | N/A | I | ◐ |
| NFR-16 | SignalR live updates and locks | M13 | RealtimeHub | SCR-00 | I E | ◐ |
| NFR-17 | Background jobs never block entry | X | hosted background job runner | N/A | I | ◐ |
| NFR-18 | Structured logging, health checks, metrics; no secrets/PII in logs | X | logging setup, HealthEndpoints | SCR-94 | I | ◐ |
| NFR-19 | Every Section 8 rule tested on every build | X | test projects | N/A | U I | ☐ |
| NFR-20 | Single Master Database; shared tables reused | X | schemas core/acc/cfg/trn | N/A | I (step 3 verification) | ☑ |
| NFR-21 | Soft delete via global filter | X | SoftDeleteInterceptor, filters | N/A | I | ☑ |
| NFR-22 | Provider-neutral, container-ready, configuration-driven | X | Dockerfile, options pattern, interfaces | N/A | build | ☐ |

## I. Security

| ID | Requirement | Mod | DB / Code | Screens | Tests | St |
|---|---|---|---|---|---|---|
| SEC-01 | Email + password; Identity salted hash | M02 | Users.PasswordHash; custom UserStore | SCR-01 | I S | ◐ |
| SEC-02 | Policy, lockout, mandatory 2FA | M02 | FR-175, FR-177 | SCR-01, SCR-05 | S E | ◐ |
| SEC-03 | HTTP-only cookies, idle timeout, server session end, other sessions ended on reset/role change | M02 | UserSessions, SecurityStamp | SCR-08 | S | ◐ |
| SEC-04 | Single-use, expiring links | M02 | Invitations.TokenHash/ExpiresAt; A-17 | SCR-03, SCR-04 | S | ◐ |
| SEC-05 | Server-side tenant/company/permission check on every request | X | permission policies; TenantContext | N/A | S | ◐ |
| SEC-06 | Data-layer filtering; cross-tenant tests every release | X | global query filters | N/A | S (TST-06) | ◐ |
| SEC-07 | Provider access only via window | M01 | SupportAccessGrants | SCR-92, SCR-83 | S | ☐ |
| SEC-08 | TLS 1.2+; encryption at rest | X | HTTPS config; TDE/backup encryption at deployment (DEF-01) | N/A | O | ☐ |
| SEC-09 | Secrets in secret store / encrypted columns; never shown or logged | M10/X | EInvoiceSettings.*Encrypted, Users.TwoFactorSecret; Data Protection; user-secrets | SCR-73 | S | ☐ |
| SEC-10 | Card data only at gateway | M01 | IPaymentGateway (hosted page) | SCR-80 | review | ☐ |
| SEC-11 | Driver details in full to drv.view/masters.manage; exports audited | M04 | AuditLogs (export) | SCR-60 | S | ☐ |
| SEC-12 | OWASP Top 10 protections | X | validation, EF parameters, encoding, antiforgery, headers | N/A | S | ◐ |
| SEC-13 | Upload type/size checks; formulas not executed | M14 | ImportService | SCR-12, SCR-30, SCR-33 | U S | ☐ |
| SEC-14 | Rate limiting on sign-in, reset, API | X | ASP.NET Core rate limiter | N/A | I | ◐ |
| SEC-15 | Dependency scanning every build; pen test before launch | X | `dotnet list package --vulnerable` in build | N/A | O | ☐ |
| SEC-16 | Append-only audit log incl. sign-ins and support sessions | M13 | AuditLogs | SCR-77 | I S | ◐ |
| SEC-17 | Financial integrity server-side | M07 | CHECKs + services | N/A | U I | ☐ |
| SEC-18 | Tenant owns data | M01 | P-16 export | SCR-82 | O | ☐ |
| SEC-19 | Jordan PDPL; anonymise in place | M02/M04 | AnonymisationService (legal check deferred to owner) | SCR-60, SCR-76 | I | ☐ |
| SEC-20 | Tax compliance (16 %, gap-free, e-invoicing, QR, 7 years) | M09/M10 | FR-043, FR-059…066 | — | I | ☐ |
| SEC-21 | Terms and Privacy acceptance recorded | M01 | TermsAcceptances (G-20) | SCR-02 | I | ◐ |
| SEC-22 | Hashed opaque ids in URLs; unknown → not found | X | PublicId + URL token encoder | all | S | ◐ |
| SEC-23 | No hard deletes | X | SoftDeleteInterceptor | N/A | I S | ☑ |

## J. UI/UX

| ID | Requirement | Applies to | Built in step | Tests | St |
|---|---|---|---|---|---|
| UX-01 | Dense, calm business UI; no decorative effects | all | 5–8 | review | ☐ |
| UX-02 | Arabic RTL first, mirrored; English LTR | all | 5–8 | E | ☐ |
| UX-03 | Keyboard first; shortcuts; visible focus | entry screens | 7–8 | E | ☐ |
| UX-04 | Shared components behave the same | all | 7 | review | ☐ |
| UX-05 | Status = text + icon, never colour alone | all badges | 7 | E (a11y) | ☐ |
| UX-06 | Brand colours; 4.5:1; logo in shell, sign-in, print | all, SCR-00/01/09 | 5–7 | E (a11y) | ☐ |
| UX-07 | IBM Plex Sans Arabic / Inter; tabular figures; 14–16 px | all | 7 | review | ☐ |
| UX-08 | Compact spacing 8–32 px | all | 7 | review | ☐ |
| UX-09 | One SVG icon set; labelled icon buttons | all | 7 | E (a11y) | ☐ |
| UX-10 | Light theme only | all | 7 | review | ☐ |
| UX-11 | App shell navigation groups and top bar | SCR-00 | 8 | E | ☐ |
| UX-12 | Breadcrumbs; URL per record/filtered list with hashed ids | all | 8 | E S | ☐ |
| UX-13 | Hidden menu items without permission; no-access message | SCR-00 | 8 | E S | ☐ |
| UX-14 | ≥ 1280 px; tablet; phone viewing/approvals; no page h-scroll | all | 8 | E | ☐ |
| UX-15 | Data table component | all lists | 7 | E | ☐ |
| UX-16 | Form component conventions | all forms | 7 | E | ☐ |
| UX-17 | Calculation panel | SCR-10/11, SCR-21/22, SCR-27 | 7–8 | E | ☐ |
| UX-18 | Confirmation dialogs; reasons; typed word | void/approve/close/maintenance | 7 | E | ☐ |
| UX-19 | Toasts, loading > 300 ms, job progress, blocked-state banners | all | 7 | E | ☐ |
| UX-20 | Lock bar, read-only mode, live row highlight | edit forms, lists | 7–8 | E | ☐ |
| UX-21 | History side panel on every record | all record views | 7–8 | E | ☐ |
| UX-22 | A4 RTL print with header, logo, page X of Y, words, QR, signatures | SCR-09 | 8 | E | ◐ |
| UX-23 | Print preview; PDF identical | SCR-09 | 8 | E | ◐ |
| UX-24 | WCAG 2.1 AA | all | 5–9 | E (a11y) | ☐ |
| UX-25 | Click targets ≥ 32 / 44 px | all | 7 | E (a11y) | ☐ |

## K. Integrations, API, data exchange, real-time

| ID | Requirement | Mod | Code | Tests | St |
|---|---|---|---|---|---|
| INT-01 | National e-invoicing (JoFotara, UBL 2.1) | M10 | IEInvoiceConnector, JoFotaraConnector, simulated dev connector (A-15) | U I, TST-04 | ◐ |
| INT-02 | Payment gateway (provider deferred) | M01 | IPaymentGateway + webhook endpoint; dev fake (A-14) | I | ☐ |
| INT-03 | Email service (provider deferred), AR/EN templates | M02/X | IEmailSender; dev file sender (A-14) | I | ◐ |
| API-01 | Every Section 7 feature via REST API for own UI | X | Minimal API / controllers per module | I | ◐ |
| API-02 | /api/v1 + OpenAPI | X | API versioning, OpenAPI | I | ◐ |
| API-03 | Every request authenticated, one tenant + company, permission checked | X | auth + policies | S | ◐ |
| API-04 | Paging, sorting, filtering; no foreign ids | X | list contract | I S | ◐ |
| API-05 | Idempotency on create/approve | X | G-13 | I | ◐ |
| API-06 | Problem details in user language with field and code | X | error middleware | I | ◐ |
| API-07 | Rate limiting per tenant | X | rate limiter partitioned by tenant | I | ◐ |
| DX-01 | Import xlsx/CSV (trips, master data, chart, entries) | M14 | ImportService | I | ☐ |
| DX-02 | Export lists/reports Excel, PDF | M14 | ExportService | I | ☐ |
| DX-03 | Journal export JSON/CSV | M07 | JournalExportService | I | ☐ |
| DX-04 | Company export Excel + JSON | M14 | CompanyExportJob | I | ☐ |
| RT-01 | SignalR pushes to same-company users only | M13 | RealtimeHub (company groups) | S I | ◐ |
| RT-02 | Auto-reconnect and refresh | M13 | client reconnect handler | E | ☐ |

## L. Testing, release, goals, confirmed values — N/A for code mapping (process items)

| ID | Item | Handling | St |
|---|---|---|---|
| TST-01…02 | Unit / integration tests every build | Test projects from step 3/4 | ☐ |
| TST-03 | E2E Section 9 flows, RTL + English | Playwright in step 9 | ☐ |
| TST-04 | E-invoicing tests against national test environment | Needs your JoFotara test credentials | ☐ |
| TST-05 | Performance 500,000 trips / 25 users | Load-test script in step 9 | ☐ |
| TST-06 | Security tests | Isolation tests every build; pen test is yours before launch | ☐ |
| TST-07 | Backup and restore drill | Operational (DEF-01) — runbook only | ☐ |
| TST-08 | Reference data set (demo tenant, 2 companies, …) | Test-data seeder (test projects only) | ☐ |
| TST-09 | Section 8 worked examples exact | EX-1…EX-4 unit tests | ☐ |
| TST-10 | Concurrency numbering and locks | Integration test | ☐ |
| TST-11 | Reconciliation after reference month | Integration test | ☐ |
| TST-12…14 | Quality gates (no failing tests, no critical defects, ≥ 80 % coverage domain/application) | Coverage report in step 9 | ☐ |
| TST-15…17 | UAT | Yours (N/A for build) | — |
| REL-01…14 | Release milestones and readiness | Yours; build supplies REL-09 items (template, demo data, email templates) and REL-10 help pages are **not** in the build prompt deliverables — **see note** | — |
| G-01…G-09 | Product goals | Covered through the FRs above | — |
| ASM-01…14 | Confirmed values | Used as constants/settings (trial 14 d, grace 7 d, 30 d, 90 d, invitation 7 d, targets) | — |
| DEF-01…02 | Deferred decisions | Provider-neutral interfaces (A-13, A-14) | — |

> Note on REL-10 (user guide and help pages, Arabic and English): the PRD lists it as a go-live readiness item, not a functional requirement, and the build prompt's deliverables do not include it. Please tell me whether help pages are in the build scope.
