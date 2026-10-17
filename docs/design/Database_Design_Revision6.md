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
