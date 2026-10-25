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

## B8. Email change waiting for confirmation (owner testing 21 Oct 2026, DT-229) — migration `Rev6_PendingEmail`
- `core.Users.PendingEmail` NVARCHAR(150) NULL — the new address a user asked for (profile, with the current password and, with two-step verification on, a code) or an Owner / Administrator started (Users & roles). Sign-in and password reset keep using `Email` until the account's user, signed in, confirms the link sent to the new address; `Email` / `NormalizedEmail` / `EmailConfirmedAt` are then set and the pending columns cleared. Not unique (the free check is done at the request and again at confirmation; `UX_Users_Email` / `UX_Users_NormalizedEmail` stay the guarantee).
- `core.Users.PendingEmailAt` DATETIME2 NULL — when it was asked; part of the link's token purpose, so a newer request or a cancel makes earlier links useless.
- `CK_Users_PendingEmail_PendingEmailAt`: both set or both NULL.
- Name and username changes (Owner / Administrator only, DT-229) use the existing `FullName`, `UserName`, `NormalizedUserName` columns and `UX_Users_NormalizedUserName`; no schema change.

## B9. Partner trips voided or made cheaper after payment (owner decision 21 Oct 2026 on DT-228, DT-230) — migration `Rev6_PartnerOverpayments`
- New table `trn.TRN_PartnerOverpayments` (company-scoped, standard audit columns): `PartnerId` (FK `TRN_Partners`), `TripId` (FK `TRN_Trips`), `PartnerPaymentId` NULL (FK `TRN_PartnerPayments`), `Amount` DECIMAL(18,3) > 0, `Handling` VARCHAR(10) IN ('Return','Deduct','NotPaid'), `Reason` NVARCHAR(500); check: `PartnerPaymentId` set exactly for 'NotPaid'; indexes on PartnerId, TripId, PartnerPaymentId (filtered), CompanyId (filtered), PublicId (unique). One row each time a trip void or edit leaves the partner paid more than its trips cost (the user's choice and reason), or a payment is voided because it was not actually made for a trip.
- `trn.TRN_PartnerPayments.DeductedAmount` DECIMAL(18,3) NOT NULL DEFAULT 0, check ≥ 0 and 0 for repayments — of the trips a payment settles, the amount paid earlier for voided / cheaper trips with "Deduct" that it sets off (shown on the voucher and the statement; no money, no journal line). Amount still to deduct = Σ Deduct rows − Σ `DeductedAmount` of active payments, never more than the partner owes.

## Rollback of B7 and B9 (GitHub CI run of 21 Oct 2026, DT-231) — no schema change
`CK_TRN_Settlements_NetPayable` is unchanged and correct: `[NetPayable] >= 0 AND [NetPayable] = [GrossWages] - [AdvancesTotal] - [DeductionsTotal] + [RepaidAmount] + [CarriedForward]`. **Upgrading** is safe for existing data: settlements made before `Rev6_SettlementRepayment` already satisfy `NetPayable = Gross − Advances − Deductions ≥ 0` (the old constraint), and the new columns are added with 0 before the new constraint is created, so every old row satisfies it with no data step; partner payments get `Kind` 'Payment' and `DeductedAmount` 0, advances no `CarriedFromSettlementId` — the accounting meaning of the old rows. Only **rolling back** could fail: the old constraint cannot hold a settlement with a driver repayment (net shown as 0, the shortfall repaid / carried). The Down methods of `Rev6_SettlementRepayment` and `Rev6_PartnerOverpayments` now refuse first, in the same transaction, with the reason (`THROW 50231` / `50232`) while such rows exist (repayments, carried advances, partner repayments; choices for paid partner trips, payment deductions), instead of failing half-way or silently dropping recorded data.

## B10. System email via Microsoft 365 (owner decision 22 Oct 2026, option A; DT-250…DT-253) — migration `Rev6_SystemEmail`
Two new **platform-wide** tables (schema `core`, no `TenantId` filter: only platform staff read them, through the provider console). The migration only creates them: **no existing row or column changes**, so the upgrade is safe for existing data. Rolling it back drops both tables (the settings can be entered again; the delivery log is lost).

**`core.SystemEmailSettings`** — how the platform sends email (standard audit columns: `Id`, `PublicId`, `CreatedAt/By`, `UpdatedAt/By`, soft delete, `RowVersion`; every change is in `core.AuditLogs`, the secret written as `"***"`).

| Column | Type | Rule |
|---|---|---|
| `State` | VARCHAR(10) NOT NULL | `CK_SystemEmailSettings_State`: Active / Pending / Retired. **At most one Active** (`UX_SystemEmailSettings_Active`, filtered `State='Active' AND IsDeleted=0`) **and one Pending** (`UX_SystemEmailSettings_Pending`). No row = the development folder |
| `Mode` | VARCHAR(20) NOT NULL | `CK_SystemEmailSettings_Mode`: Development / Microsoft365 (another provider is a new value) |
| `EntraTenantId`, `EntraClientId` | NVARCHAR(100) NULL | the Entra app's directory (tenant) ID and application (client) ID |
| `ClientSecretEncrypted` | VARBINARY(MAX) NULL | the client secret, ASP.NET Core Data Protection, purpose `DST.SystemEmail.ClientSecret.v1`; never returned by the API |
| `SecretSavedAt` | DATETIME2(3) NULL | when the secret was saved |
| `SecretExpiresOn` | DATE NULL | the expiry date staff entered (as Entra shows it) |
| `SenderMailbox` | NVARCHAR(254) NULL | the mailbox the emails are sent as |
| `DisplayName` | NVARCHAR(100) NOT NULL | sender name |
| `ReplyTo` | NVARCHAR(254) NULL | reply-to address |
| `ActivatedAt` | DATETIME2(3) NULL | when the row became Active |
| `LastTestAt`, `LastTestOk`, `LastTestError` | DATETIME2(3) / BIT / NVARCHAR(400) NULL | the last test email with these settings and, when it failed, "kind: detail" (no secret) |
| `ExpiryWarnedDays` | INT NULL | the expiry warning already sent to platform staff (30, then 7); `CK_SystemEmailSettings_ExpiryWarnedDays` > 0 |

`CK_SystemEmailSettings_Microsoft365`: a Microsoft365 row has tenant ID, client ID, secret and mailbox. Index `UX_SystemEmailSettings_PublicId`. FKs to `core.Users` for CreatedBy / UpdatedBy / DeletedBy.

**`core.EmailOutbox`** — the delivery queue and log of every email. Not an `AuditableEntity` and not written to the audit log (a technical log of its own; resends by staff are audited as `CORE.EmailOutbox` / Resend).

| Column | Type | Rule |
|---|---|---|
| `Id`, `PublicId` | BIGINT identity, UNIQUEIDENTIFIER | `UX_EmailOutbox_PublicId` (the console's reference) |
| `TenantId` | BIGINT NULL | FK `core.Tenants`; the tenant of the user whose action caused the email (masked with the tenant's data on deletion) |
| `Template` | VARCHAR(40) NOT NULL | VerifyEmail, PasswordReset, Invitation, StaffInvitation, ConfirmEmailChange, EmailChangeAsked, EmailChanged, TwoFactorChanged, SupportDecision, SupportEnded, TenantDeleted, TestEmail, SystemEmailFailures, SecretExpiring |
| `LanguageCode` | VARCHAR(5) NOT NULL | `CK_EmailOutbox_LanguageCode`: ar / en (the recipient's) |
| `Recipient` | NVARCHAR(254) NOT NULL | replaced by "(removed)" after `Email:LogRetentionDays` (180) and when the tenant is deleted |
| `Status` | VARCHAR(10) NOT NULL | `CK_EmailOutbox_Status`: Pending / Sending / Sent / Failed / Suppressed |
| `Attempts` | INT NOT NULL DEFAULT 0 | ≥ 0 |
| `NextAttemptAt`, `ClaimedUntil`, `CreatedAt`, `LastAttemptAt`, `SentAt`, `FailedAt` | DATETIME2(3) | `CK_EmailOutbox_SentAt` (Sent ⇒ SentAt), `CK_EmailOutbox_FailedAt` (Failed ⇒ FailedAt) |
| `ErrorKind`, `LastError` | VARCHAR(40) / NVARCHAR(400) NULL | wrongSecret, missingPermission, throttled, … and Microsoft's message — never a secret or a link |
| `Mode` | VARCHAR(20) NULL | the mode it was (last) tried with |
| `HasSecretLink` | BIT NOT NULL DEFAULT 0 | the email holds a single-use link |
| `PayloadEncrypted` | VARBINARY(MAX) NULL | recipient, subject, text and HTML, Data Protection purpose `DST.SystemEmail.Outbox.v1`. `CK_EmailOutbox_PayloadEncrypted`: only while Pending / Sending, or Failed **without** a single-use link (so it can be resent as it was) — wiped once sent |
| `ResentFromId`, `ResentAt` | BIGINT NULL (FK to itself), DATETIME2(3) NULL | a resend's original; when a failed row was resent |
| `RowVersion` | ROWVERSION | |

Indexes: `IX_EmailOutbox_Status_NextAttemptAt` (the worker's queue), `IX_EmailOutbox_SentAt` filtered `SentAt IS NOT NULL` (the 30-a-minute throttle, last success), `IX_EmailOutbox_CreatedAt` (log, retention), `IX_EmailOutbox_Recipient_Template_CreatedAt` (5 of a kind per address an hour). The worker claims due rows with `UPDLOCK, READPAST, ROWLOCK` (raw SQL, listed in BUILD_STATUS.md). The application user `dst_app` needs no new right (insert / update only; `DENY DELETE` on `core` stays).

## B11. Journal line reference and tax (owner request 25 Oct 2026, DT-254…DT-259) — migration `Rev6_JournalLineTax`
Manual journal entries get a reference on every line and a tax code per line; the amount on a line is **before tax** and posting adds the tax as an automatic line. Sales invoices, credit notes, supplier bills and e-invoicing (JoFotara) keep their own tax calculation (no change).

**New table `acc.TaxCodes`** (company-scoped, standard audit / soft-delete / row-version columns, audited as `ACC.TaxCode`): the tax codes of Settings → Tax codes.

| Column | Type | Rule |
|---|---|---|
| `Code` | VARCHAR(30) NOT NULL | `UX_TaxCodes_CompanyId_Code` unique per company (filter `[IsDeleted] = 0`); letters, digits, `.` `_` `-` (application rule, as account codes) |
| `Rate` | DECIMAL(5,2) NOT NULL | `CK_TaxCodes_Rate` 0–100 (percent) |
| `NameAr` / `NameEn` | NVARCHAR(100) NOT NULL / NULL | Arabic and English description |
| `Status` | VARCHAR(10) NOT NULL DEFAULT 'Active' | `CK_TaxCodes_Status` Active / Archived (deactivated: not offered, not posted; posted lines keep it) |
| `SortOrder` | INT NOT NULL DEFAULT 0 | `CK_TaxCodes_SortOrder` ≥ 0; list order (then the code) |

Index `UX_TaxCodes_PublicId`; FKs to `core.Tenants`, `core.Companies`, `core.Users` (Created/Updated/DeletedBy). Application rules: a code used on a journal line (any status of the entry) cannot be deleted and its code and rate cannot change — deactivate it and create a new code. Seed (editable): `VAT_JO_0`, `_1`, `_2`, `_4`, `_5`, `_7`, `_8`, `_10`, `_16` with "ضريبة المبيعات n%" / "VAT n%", order 10…90 — for every existing company by the migration, for new companies by `CompanyProvisioningService`.

**`acc.EntryLines`** — six new columns (all NULL):

| Column | Type | Rule |
|---|---|---|
| `Reference` | NVARCHAR(100) | line reference (مرجع السطر); a line without one takes the entry's `Reference` when posted; index `IX_EntryLines_CompanyId_Reference` (filter `IS NOT NULL`) |
| `TaxCodeId` | BIGINT | FK `FK_EntryLines_TaxCodes_TaxCodeId`; the line's tax code (on an automatic tax line, the code it was calculated with); filtered index |
| `TaxRate` | DECIMAL(5,2) | the code's rate when posted (`CK_EntryLines_TaxRate` 0–100) — later edits of the code never change posted lines |
| `TaxAmount` | DECIMAL(18,3) | tax = amount × rate ÷ 100, half-up to the currency's decimals (`CK_EntryLines_TaxAmount` ≥ 0); gross = amount + tax (not stored) |
| `TaxAccountId` | BIGINT | FK `FK_EntryLines_Accounts_TaxAccountId`; the tax account the user chose (required for a rate above 0 %, never defaulted); filtered index |
| `TaxForLineId` | BIGINT | FK `FK_EntryLines_EntryLines_TaxForLineId` (self); set on the **automatic tax line** = the line whose tax it carries; filtered index |

Checks: `CK_EntryLines_TaxCodeId_TaxRate` — without a tax code every tax column is NULL; with one the rate is set. `CK_EntryLines_TaxForLineId` — an automatic tax line has no tax amount and no tax account of its own (its `AccountId` is the tax account). Posting: for each taxed line with tax > 0 the engine adds a line to the tax account on the **same side** (debit line → tax debit, credit line → tax credit), same cost centre and reference, note "ضريبة المبيعات 16% على السطر 2 / VAT 16% on line 2", numbered after the entered lines; `TotalDebit` / `TotalCredit` include the tax lines and must be equal (BR-033). Reversal (edit, void) mirrors every column and keeps the link between the mirror lines.

**`acc.Accounts.IsTaxAccount`** BIT NOT NULL DEFAULT 0 (`DF_Accounts_IsTaxAccount`) — offered as the tax account of journal lines; `CK_Accounts_IsTaxAccount`: never a control account. The chart template (new companies) marks 1105 (input VAT) and 2104 (output VAT) and adds 5207 "مصروف ضريبة المبيعات غير القابلة للاسترداد / VAT expenses (non-recoverable)" under 52, marked. Existing companies: nothing is added or marked by the migration; "Add the default tax accounts" (chart of accounts, acct.setup) marks the template's own 1105 / 2104 when they are there (same code, type and name) and adds the missing ones with the next free code under their group (11, 21, 52).

**JSON lines** of `acc.JournalDrafts` and `acc.JournalTemplates` (no schema change): each line may also hold `reference`, `taxCodeId`, `taxAccountId` (internal ids); lines saved before have none. Draft totals are as they would post (with tax).

**Existing data (upgrade).** Choice: the lines of existing entries **take the entry's Reference** (one `UPDATE … FROM acc.JournalEntries`), so statements, the entry view and the search show old and new entries alike — exactly what a new line starts with; entries without a Reference (all automatic ones) keep empty line references. No existing line gets a tax code; nothing else changes, so balances and reports are unchanged. Alternative not taken: leave line references empty and fall back to the entry's at reading time — two places to read one value. (The statement still falls back to the entry's Reference for a line without one.)

**Rollback.** `Down` refuses first, in the same transaction, while any line has a tax code or is an automatic tax line (`THROW 50259`, same style as DT-231); otherwise it drops the tax codes, the tax-account flag and the line references (the entry Reference stays).

Raw SQL in the migration: the reference copy, the seed of the tax codes and the rollback guard (listed in BUILD_STATUS.md).
