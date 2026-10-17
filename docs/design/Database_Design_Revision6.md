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
