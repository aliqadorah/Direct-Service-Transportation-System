# Database Design — Changes in Revision 5

_Approved by the owner on 8 Oct 2026 (`docs/step1/03_Owner_Decisions.md` D-02, D-08, D-09; `docs/step2/02_Owner_Decisions_Step2.md` Q-02 … Q-05). Applies on top of revision 4 (`docs/inputs/Database_Design.md`, unchanged). Implemented in migration `InitialMasterDatabase` and verified by `docs/step3/02_Design_vs_Database_Verification.md`._

## 1. `trn.TRN_Invoices.LayoutType` default (D-02)
| Column | Type | Null | Default | Rules |
|---|---|---|---|---|
| LayoutType | VARCHAR(10) | NOT NULL | **'Claim'** (rev. 4: 'TaxInvoice') | `CK_TRN_Invoices_LayoutType` CHECK IN ('Claim','TaxInvoice') — unchanged |

Default constraint: `DF_TRN_Invoices_LayoutType`. Reason: PRD 7.3.1 / US-12 — the claim statement is the default layout.

## 2. Tenant-level password policy and idle timeout on `core.Tenants` (D-08, option R5-A)
Inserted after `RetentionEndsAt`, before the standard audit columns.

| Column | Type | Null | Default | Description | Rules |
|---|---|---|---|---|---|
| PasswordMinLength | TINYINT | NOT NULL | 8 | Minimum password length for the tenant's users (FR-175). | `CK_Tenants_PasswordMinLength` BETWEEN 8 AND 64 (Q-05) |
| PasswordRequireLetter | BIT | NOT NULL | 1 | Password must contain at least one letter (FR-175). | — |
| PasswordRequireDigit | BIT | NOT NULL | 1 | Password must contain at least one digit (FR-175). | — |
| SessionIdleTimeoutMinutes | SMALLINT | NOT NULL | 30 (Q-03) | Inactive sessions expire after this many minutes (FR-176). | `CK_Tenants_SessionIdleTimeoutMinutes` BETWEEN 5 AND 1440 |

Defaults: `DF_Tenants_PasswordMinLength`, `DF_Tenants_PasswordRequireLetter`, `DF_Tenants_PasswordRequireDigit`, `DF_Tenants_SessionIdleTimeoutMinutes`.
Not configurable (fixed by the PRD): 5 failed sign-ins → 15-minute lock. Absolute session lifetime 12 hours is a platform setting (Q-04), not a column.

## 3. `core.Companies.Status` (D-09)
Inserted after `MigrationCompletedAt`, before the standard audit columns.

| Column | Type | Null | Default | Description | Rules |
|---|---|---|---|---|---|
| Status | VARCHAR(10) | NOT NULL | 'Active' | Record status. An archived company is hidden from the company switcher and read-only; its data is kept. | `CK_Companies_Status` CHECK IN ('Active','Archived'); default `DF_Companies_Status` |

Application rules: archive/restore by the Owner only, with a reason, audited; the last Active company cannot be archived; only Active companies count toward `Plans.MaxCompanies` (Q-06); `UX_Companies_TenantId_Name` unchanged.

## Totals after revision 5
66 tables (unchanged), 1,410 columns (+5), 118 business foreign keys (unchanged), 246 check constraints, 302 named default constraints.
