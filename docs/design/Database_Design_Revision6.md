# Database Design — Changes in Revision 6

_Owner change requests of 15 Oct 2026, part A (edits). Database changes were allowed for these items only. Applies on top of revision 5 (`docs/design/Database_Design_Revision5.md`) and revision 4 (`docs/inputs/Database_Design.md`, unchanged). Each section names its EF Core migration; `database/MasterDatabase_Idempotent.sql` is regenerated after each one._

## 1. Users created with a username and password (item 1) — migration `Rev6_UserNameSignIn`
`core.Users`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Email | NVARCHAR(150) | **NULL** (rev. 5: NOT NULL) | — | NULL only for a user created by an administrator with a username and no email |
| NormalizedEmail | NVARCHAR(150) | **NULL** (rev. 5: NOT NULL) | — | upper-cased Email (Identity) |
| UserName | NVARCHAR(50) | NULL | — | new; set by an administrator: 3–50 characters, ASCII letters, digits, `.` `_` `-` (application rule; never contains `@`, so it never equals an email) |
| NormalizedUserName | NVARCHAR(50) | NULL | — | new; upper-cased UserName (Identity look-up, case-insensitive uniqueness) |
| MustChangePassword | BIT | NOT NULL | 0 (`DF_Users_MustChangePassword`) | new; the user must choose a new password at the next sign-in |

Constraints and indexes:
- `CK_Users_Email_UserName` CHECK (`[Email] IS NOT NULL OR [UserName] IS NOT NULL`) — every user has a sign-in name.
- `UX_Users_Email` and `UX_Users_NormalizedEmail` stay unique platform-wide; their filters become `[IsDeleted] = 0 AND [Email] IS NOT NULL` / `… AND [NormalizedEmail] IS NOT NULL` (several users without email).
- `UX_Users_NormalizedUserName` UNIQUE, filter `[IsDeleted] = 0 AND [NormalizedUserName] IS NOT NULL` — usernames unique platform-wide.

Audit: the new columns are audited like the others (`NormalizedUserName` hidden from readers); new audit actions `CreatedWithPassword`, `PasswordSetByAdmin`, `PasswordChanged`. Personal-data erasure (SEC-19) and tenant deletion clear `UserName` / `NormalizedUserName`.

## 2. "Require 2FA for all users" (item 2) — migration `Rev6_RequireTwoFactor`
`core.Tenants`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| RequireTwoFactor | BIT | NOT NULL | 0 (`DF_Tenants_RequireTwoFactor`) | new; changed by the Owner (core.tenant.manage) with the other sign-in security settings; when 1, every user of the tenant without two-step verification must set it up at the next sign-in. Platform staff (no tenant) always need it |

Audited with the tenant's other settings (action `SettingChange`).

## 3. Journal entry Reference and drafts (item 3) — migration `Rev6_JournalReferenceAndDrafts`
`acc.JournalEntries`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Reference | NVARCHAR(100) | NULL | — | new; free-text reference (المرجع) next to the description; copied to a reversal entry |

New table `acc.JournalDrafts` (company-scoped, audited, soft delete): a manual journal entry saved as a draft — no JV number, not posted, never in a balance or report; posting creates the entry through the posting engine and removes the draft in the same transaction.

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL | — | PK `PK_JournalDrafts` |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | `UX_JournalDrafts_PublicId` |
| TenantId / CompanyId | BIGINT | NOT NULL | — | FK to core.Tenants / core.Companies; `IX_JournalDrafts_CompanyId` (filter `[IsDeleted] = 0`) |
| EntryDate | DATE | NULL | — | intended entry date (required at posting) |
| Description | NVARCHAR(500) | NULL | — | required at posting |
| Reference | NVARCHAR(100) | NULL | — | |
| Lines | NVARCHAR(MAX) | NOT NULL | — | JSON array (account id, debit, credit, cost centre id, party type + id, note); `CK_JournalDrafts_Lines` ISJSON |
| TotalDebit / TotalCredit | DECIMAL(18,3) | NOT NULL | — | as entered (may differ while drafting) |
| CreatedAt … RowVersion | standard audit, soft-delete and row-version columns | | | `CK_JournalDrafts_DeletedAt_DeletedBy_DeleteReason` |

## 4. Receipts against several invoices (item 4) — migration `Rev6_ReceiptAllocations`
New table `trn.TRN_ReceiptAllocations` (company-scoped, audited, soft delete): the part of a receipt applied to one open invoice of the customer. The rest of a receipt is on account. `TRN_Receipts.InvoiceId` is kept and filled only when exactly one invoice is allocated.

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL | — | PK `PK_TRN_ReceiptAllocations` |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | `UX_TRN_ReceiptAllocations_PublicId` |
| TenantId / CompanyId | BIGINT | NOT NULL | — | FK to core.Tenants / core.Companies |
| ReceiptId | BIGINT | NOT NULL | — | FK `FK_TRN_ReceiptAllocations_TRN_Receipts_ReceiptId` |
| InvoiceId | BIGINT | NOT NULL | — | FK `FK_TRN_ReceiptAllocations_TRN_Invoices_InvoiceId`; `IX_TRN_ReceiptAllocations_InvoiceId` |
| Amount | DECIMAL(18,3) | NOT NULL | — | `CK_TRN_ReceiptAllocations_Amount` > 0; ≤ the invoice's open balance and Σ ≤ the receipt amount (application rules) |
| CreatedAt … RowVersion | standard audit, soft-delete and row-version columns | | | `CK_TRN_ReceiptAllocations_DeletedAt_DeletedBy_DeleteReason` |

`UX_TRN_ReceiptAllocations_ReceiptId_InvoiceId` UNIQUE (filter `[IsDeleted] = 0`): an invoice once per receipt.

Data: every existing receipt with an `InvoiceId` was copied as one allocation of its full amount (raw SQL in the migration).

## 5. Supplier bill line deductions (item 7) — migration `Rev6_SupplierBillDeductions`
`acc.ApArDocumentLines`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Deduction | DECIMAL(18,3) | NOT NULL | 0 (`DF_ApArDocumentLines_Deduction`) | new; `CK_ApArDocumentLines_Deduction` `[Deduction] >= 0 AND [Deduction] <= [Amount]`; supplier bills only (application rule); tax stays on the full amount |

`acc.ApArDocuments`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Deduction | DECIMAL(18,3) | NOT NULL | 0 (`DF_ApArDocuments_Deduction`) | new; Σ line deductions; `CK_ApArDocuments_Deduction` ≥ 0; Total = Subtotal + Tax − Deduction (application rule) |

Posting: the bill's entry credits the new posting key `ACC.SupplierDeductions` with Σ deductions (reference data only — `acc.PostingKeyMappings` rows; the chart template adds account 4105 "Supplier deductions income" and maps it).

## 6. Returns give back the deduction (owner decision of 17 Oct 2026) — migration `Rev6_ReturnDeductions`
`acc.ApArReturns`:

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Deduction | DECIMAL(18,3) | NOT NULL | 0 (`DF_ApArReturns_Deduction`) | new; the returned share of the bill's deductions (bill deduction × amount ÷ subtotal, the last return takes the rest); `CK_ApArReturns_Deduction` `[Deduction] >= 0 AND [Deduction] <= [Amount]`; Total = Amount + Tax − Deduction (application rule) |

## Items without database changes
Item 5 (trial balance tree), item 6 (account statement of several accounts, general ledger screen removed) and item 8 (OpenAPI) change no table.

# Part B — owner change requests of 15 Oct 2026, new accounting features

## B1. Budgets — migration `Rev6_Budgets`
New table `acc.Budgets` (company-scoped, audited, soft delete): a budget (الموازنة) of one fiscal year. The fiscal year is the calendar year (the application has no fiscal-year setting; periods are calendar months).

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL | — | PK `PK_Budgets` |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | `UX_Budgets_PublicId` |
| TenantId / CompanyId | BIGINT | NOT NULL | — | FK to core.Tenants / core.Companies |
| FiscalYear | INT | NOT NULL | — | `CK_Budgets_FiscalYear` 2000–2100 |
| Name | NVARCHAR(200) | NOT NULL | — | `UX_Budgets_CompanyId_FiscalYear_Name` (filter `[IsDeleted] = 0`) |
| Status | NVARCHAR(20) | NOT NULL | N'Draft' | `CK_Budgets_Status` Draft / Approved; **`UX_Budgets_CompanyId_FiscalYear_Approved`** unique on (CompanyId, FiscalYear) filtered `[Status] = N'Approved' AND [IsDeleted] = 0` — one approved budget per company and year |
| ApprovedAt | DATETIME2(3) | NULL | — | `CK_Budgets_Status_ApprovedAt_ApprovedBy`: required when Approved |
| ApprovedBy | BIGINT | NULL | — | FK core.Users |
| CreatedAt … RowVersion | standard audit, soft-delete and row-version columns | | | `CK_Budgets_DeletedAt_DeletedBy_DeleteReason` |

New table `acc.BudgetLines` (company-scoped, audited, soft delete): one revenue or expense account (posting or parent account), optionally one cost centre, twelve monthly amounts in the natural direction of the account (revenue = credit − debit, expense = debit − credit; may be negative).

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL | — | PK `PK_BudgetLines` |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | `UX_BudgetLines_PublicId` |
| TenantId / CompanyId | BIGINT | NOT NULL | — | FK to core.Tenants / core.Companies |
| BudgetId | BIGINT | NOT NULL | — | FK acc.Budgets |
| AccountId | BIGINT | NOT NULL | — | FK acc.Accounts; revenue or expense type (application rule); no line on an account whose ancestor also has a line for the same cost centre (application rule, no double counting) |
| CostCentreId | BIGINT | NULL | — | FK acc.CostCentres; **`UX_BudgetLines_BudgetId_AccountId_CostCentreId`** (filter `[IsDeleted] = 0`): one line per account and cost centre |
| Month01 … Month12 | DECIMAL(18,3) | NOT NULL | 0 | January … December |
| Total | DECIMAL(18,3) | NOT NULL | 0 | `CK_BudgetLines_Total` = Month01 + … + Month12 |
| CreatedAt … RowVersion | standard audit, soft-delete and row-version columns | | | `CK_BudgetLines_DeletedAt_DeletedBy_DeleteReason` |

Reference data: three permission keys in `core.Permissions` (group Accounting) — 23 `acc.budget.view`, 24 `acc.budget.manage`, 25 `acc.budget.approve`. Data step: existing preset roles receive the keys of their preset (Owner and Administrator all three, Accountant view + manage, Viewer view); custom roles are unchanged.

## B2. Journal entry templates — migration `Rev6_JournalTemplates`
New table `acc.JournalTemplates` (company-scoped, audited, soft delete): a journal entry template (نموذج قيد). "Use template" fills a new entry on the screen; a template never posts (no recurring posting in this version).

| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL | — | PK `PK_JournalTemplates` |
| PublicId | UNIQUEIDENTIFIER | NOT NULL | NEWID() | `UX_JournalTemplates_PublicId` |
| TenantId / CompanyId | BIGINT | NOT NULL | — | FK to core.Tenants / core.Companies |
| Name | NVARCHAR(100) | NOT NULL | — | `UX_JournalTemplates_CompanyId_Name` (filter `[IsDeleted] = 0`) |
| Description | NVARCHAR(500) | NULL | — | the entry description it fills in |
| Reference | NVARCHAR(100) | NULL | — | the entry Reference it fills in |
| Lines | NVARCHAR(MAX) | NOT NULL | — | JSON array (account id, cost centre id, line description, debit, credit — amounts optional, 0 = none); `CK_JournalTemplates_Lines` ISJSON |
| CreatedAt … RowVersion | standard audit, soft-delete and row-version columns | | | `CK_JournalTemplates_DeletedAt_DeletedBy_DeleteReason` |

## B3. Accounting dashboard — no new table
The dashboard reads the existing tables. One new `cfg.CompanySettings` key (no schema change): `ACC.SupplierDeductionAttempted` = the last date (company time, yyyy-MM-dd) a supplier bill with a deduction was refused because "Supplier deductions" is not mapped; the reminder "Supplier deductions not mapped" shows while bills with deductions exist or such a refusal happened in the last 30 days.

## B4. Separate "Post journal entries" permission (owner request 18 Oct 2026, DT-201) — migration `Rev6_PostJournalEntries`
Reference data only, no schema change. New permission key 26 `acc.acct.postentry` (group Accounting): post manual entries and drafts, edit and void posted entries, import entries. Key 14 `acc.acct.post` is re-described as "Prepare manual journal entries: save and edit drafts, use and manage entry templates". Data step: every role (preset or custom) that holds key 14 receives key 26, so no existing user loses posting; owners remove it from roles that should only prepare entries. Presets: Owner, Administrator and Accountant hold both.

## B5. Receipt-number search column and index (owner decision 18 Oct 2026, DT-206) — migration `Rev6_ReceiptNoSearch`
`trn.TRN_Trips.ReceiptNoSearch` NVARCHAR(30) NULL — persisted computed column `[ReceiptNoKey] COLLATE Latin1_General_100_BIN2` (the key is already upper-case, spaces removed, digits ASCII). Index `IX_TRN_Trips_ReceiptNoSearch` on (CompanyId, ReceiptNoSearch) INCLUDE (Status) WHERE IsDeleted = 0. The trips list's "any part of the receipt number" search (`LIKE '%…%'`, which no index can seek) scans this narrow index with a byte-by-byte comparison instead of the Arabic linguistic collation: measured on 500,000 trips, 1.5–1.9 s → about 0.4 s for the comparison alone. Matching is exact on the stored key (letter case and spaces were already folded; Arabic diacritics in a receipt number are no longer ignored by this search). Uniqueness still uses `UX_TRN_Trips_CompanyId_ReceiptNoKey`.

## B6. Authenticator codes work once (owner decision 19 Oct 2026, DT-212) — migration `Rev6_TwoFactorLastStep`
`core.Users.TwoFactorLastStep` BIGINT NULL — the 30-second time step (Unix seconds / 30) of the last authenticator code accepted. A code is accepted only for a later step; the step is claimed with one conditional `UPDATE … WHERE TwoFactorLastStep IS NULL OR TwoFactorLastStep < @step` (with `OUTPUT` of the row version before/after), so one code works once, also against simultaneous requests. Reset to NULL when a new authenticator key is set (protection is per key). Recovery codes (`core.UserRecoveryCodes`) are spent with a conditional `UPDATE … WHERE UsedAt IS NULL`; no schema change.

## B7. Repayment by the driver or the partner (owner testing 21 Oct 2026, DT-228) — migration `Rev6_SettlementRepayment`
- `trn.TRN_Settlements.RepaidAmount` DECIMAL(18,3) NOT NULL DEFAULT 0 — what the driver paid back with the settlement when advances and deductions exceed the wages; the money columns (`PaymentMethod`, `CashBoxId` / `BankAccountId`, `Reference`) then describe the money received (reference required for Bank and CliQ, as before).
- `trn.TRN_Settlements.CarriedForward` DECIMAL(18,3) NOT NULL DEFAULT 0 — the unpaid rest after a partial repayment.
- `CK_TRN_Settlements_NetPayable` becomes `[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal] + [RepaidAmount] + [CarriedForward]`; new `CK_TRN_Settlements_RepaidAmount_CarriedForward`: both ≥ 0, and non-zero only when `NetPayable` = 0.
- `trn.TRN_Advances.CarriedFromSettlementId` BIGINT NULL, FK `FK_TRN_Advances_TRN_Settlements_CarriedFromSettlementId` → `trn.TRN_Settlements`, filtered index `IX_TRN_Advances_CarriedFromSettlementId` — the new open advance that carries a settlement's unpaid rest to the next settlement (numbered ADV- like any advance, dated with the settlement). `PaymentMethod` may now be `'Carried'` (no cash box, no bank account, no journal entry of its own: the settlement's entry keeps the amount on driver advances); `CK_TRN_Advances_PaymentMethod` and `CK_TRN_Advances_CashBoxId_BankAccountId` extended; new `CK_TRN_Advances_CarriedFromSettlementId`: `'Carried'` exactly when the column is set. Voiding the settlement voids the carried advance (refused while a later settlement has settled it).
- `trn.TRN_PartnerPayments.Kind` VARCHAR(10) NOT NULL DEFAULT 'Payment', `CK_TRN_PartnerPayments_Kind` IN ('Payment','Repayment') — a repayment is money received from a partner who owes the company (Dr cash / bank, Cr partner payable), at most what it owes; same PPY- numbering. Partner balance = partner cost of active trips − payments + repayments (may now be below zero: a paid trip voided or made cheaper is no longer refused).
- Postings (no schema change): the settlement entry debits the cash box / bank with `RepaidAmount` and credits driver advances with `AdvancesTotal − CarriedForward` (a debit when deductions above the wages are carried).
