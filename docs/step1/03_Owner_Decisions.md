# Direct Service Transportation — Step 1 Owner Decisions

_Decided by the owner on 8 Oct 2026. Answers to D-01 … D-18 in `docs/step1/01_Requirements_Inventory.md` §10. Step 1 is approved; continue with step 2._

## Decisions

| # | Topic | Decision |
|---|---|---|
| D-01 | Permission key namespacing | **Agree.** Module prefix + PRD key verbatim (`trn.trips.view`, `acc.acct.post`, `core.users.manage`, …). UI and documents show the PRD names. |
| D-02 | C-01 default invoice layout | **Change the database default of `TRN_Invoices.LayoutType` to 'Claim'** (design revision 5). |
| D-03 | C-02, C-03 lower bounds | **Agree:** follow the database (`> 0`) for tariff price and settlement deduction amount. |
| D-04 | C-04, C-05 rejected/failed e-invoices | **Agree:** a rejected (never accepted) document can be corrected and resubmitted; "Failed" = status 'Rejected' + `LastError`. Accepted documents are cancelled only by credit note (FR-046). |
| D-05 | C-06 editing posted manual journal entries | **Agree:** edit = reverse the original and post a corrected, linked entry; shown in the UI as an edit with history. |
| D-06 | C-07 tariff lock | **Agree:** PRD wording — under the lock, users with masters.manage may deviate (audited). |
| D-07 | C-08 … C-11 | **Agree** with the recommendations in §6 (built-in Company type: no delete, reclassify or rename; English account name required by the application; partner payment details optional with IBAN validated when given; suspended tenant = view, print and export). |
| D-08 | G-01 tenant-level settings | **Option (a): add a tenant settings table/columns** for the password policy (FR-175) and session idle timeout (FR-176) (design revision 5). |
| D-09 | G-02 archive a company | **Option (c): add a `Status` column to `core.Companies`** (Active/Archived), matching the other archivable tables (design revision 5). |
| D-10 | G-03 chart-of-accounts template | **Agree:** draft the built-in transport chart template and default posting-key mapping for owner approval before seeding. |
| D-11 | G-05 subscription credits | **Option (a): a credit only reduces the subscription invoice it is applied to; nothing carries forward.** No schema change. |
| D-12 | G-06, G-07 trial plan and trial end | **Agree:** seed one TRIAL plan (owner edits limits in the provider console); trial ends unpaid → Suspended → Cancelled after 30 days. |
| D-13 | G-08, G-09 provider staff | **Agree:** every platform-staff user can use the provider console; tenant data only inside an approved support window, read-only, every request audited. |
| D-14 | G-04, G-10 … G-18, G-20, G-21 | **Agree** with the recommendations in §7 (incl. G-04 `ACC.OpeningBalanceDifference` posting key; G-16 option (c)). |
| D-15 | E-invoicing of AR receivable documents | **Agree: include.** |
| D-16 | G-19 number prefixes | **Approved:** TRP-, CRN-, RCV-, ADV-, STL-, PPY-, JV-, BIL-, ARD-, PAY-, RTN-, JEX-; 5 digits each. |
| D-17 | Assumptions A-01 … A-22 | **Agree.** A-19 collation is proposed in step 2. |
| D-18 | REL-10 user guide and help pages (Arabic + English) | **In scope:** a short help page per main flow, written at the end of the build. |

## Database design revision 5 (approved changes)
Make only these changes, document each in `BUILD_STATUS.md` and in a "Changes in revision 5" note, and follow the design's naming and standard-column conventions:
1. `trn.TRN_Invoices.LayoutType` default `'TaxInvoice'` → `'Claim'` (D-02).
2. Tenant-level settings for the password policy and session idle timeout (D-08). Propose the exact table/columns in step 2 for owner review before implementing.
3. `core.Companies.Status` (Active/Archived, CHECK, default Active) (D-09). Propose the exact column definition in step 2.
