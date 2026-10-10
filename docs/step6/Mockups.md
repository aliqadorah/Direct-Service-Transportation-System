# Step 6 — Full mockups (checkpoint 3)

_10 Oct 2026. **Approved by the owner on 10 Oct 2026 as the basis for steps 7–8**, with the fixes below (applied to the board; carried into the design system in step 7)._

## Owner fixes applied (10 Oct 2026)
1. **Bidi isolation everywhere:** every left-to-right run inside Arabic text (codes such as TRP-10481 / INV-02210, dates, amounts, IBANs, Latin words) is isolated as one unit; joined Latin tokens stay together. The board applies it to every rendered text node; the Blazor components will use `<bdi>`/isolated spans the same way (step 7).
2. **Invoice prefix INV-** (D-16 updated; the default sequence in code changed from TRP- to INV-); sample data consistent.
3. **SCR-11 trip form:** three columns, calculation and history stacked in one side column, shorter price-source notes on one line; no clipped field.
4. **SCR-10 quick entry:** a focused selection widens to show the full name, the dropdown lists full names (new variant "Quick entry: customer dropdown"), and clipped values show in full on hover.

**Mockup board:** `docs/step6/index.html` (serve the `docs` folder, e.g. `py -m http.server 8765 --directory docs`, then open `/step6/`; the page loads its screens from the `.js` files next to it). The same board is published as a private artifact (link in the session message and in `BUILD_STATUS.md`).

## Direction applied
**A · Ledger** (owner's choice, 10 Oct 2026), with four parts of C:
- **Saved views** as tabs above every list (built-in views plus "+ Save view").
- **Filter row** under the table headers.
- **Ctrl+K command bar** in the top bar for global search and actions (FR-187).
- **Compact/comfortable density switch** in the user menu (32 px / 40 px rows); the review bar of the board switches it too.

Ledger kept as chosen: dark side menu on the right with the PRD groups (UX-11), dense tables, 3 px corners, hairline borders, no shadows, square status tags with icon + text (UX-05), quick-entry strip on the trips page.

## Coverage
69 screens: SCR-00 (shell) and every screen of PRD 13.5 numbered in the traceability matrix (SCR-01 … SCR-94, including the five G-21 placements). Each screen has five states — **default, loading, empty, error, success** — and 32 screens have variants (SCR-10 now has three) for their modes (for example sign-in password / authenticator code / recovery code; invoice draft / approved / void / e-invoice rejected; trips quick entry / Created Trips list; settlement; pre-close check; typed confirmation). 580 screen × state × variant combinations, all rendered and checked for missing text and overflow.

| Group | Screens |
|---|---|
| Shell and start | SCR-00 shell (command bar, notifications, user menu with density, banners and lock bar), 01 sign-in, 02 sign-up, 03 email verification, 04 password reset, 05 two-factor set-up, 06 setup checklist, 07 dashboard, 08 my profile, 09 print preview |
| Operations | 10 trips (quick entry, recent, Created Trips list), 11 trip form with history, 12 trip import, 13 shortage view |
| Billing & cash | 20 invoices, 21 trip picker, 22 manual invoice, 23 invoice view, 24 credit notes, 25 e-invoicing status, 26 receipts, 27 driver accounts (advance, settlement), 28 partner accounts |
| Accounting | 30 chart of accounts, 31 journal list, 32 journal entry, 33 journal import, 34 automatic entries / pending postings / export, 35 general ledger, 36 trial balance, 37 profit & loss, 38 balance sheet, 39 account statement, 40 payables, 41 receivables, 42 banks and cash boxes, 43 cost centres, 44 periods with pre-close check, 45 maintenance zone |
| Reports | 50 operational reports, 51 financial statements and ageing, 52 report builder, 53 saved reports |
| Master data | 60 drivers (list, profile, form), 61 customers, 62 partners, 63 materials, 64 loading sites, 65 unloading sites, 66 ownership types, 67 tariffs, 68 monthly site rates |
| Settings | 70 company settings, 71 account mapping, 72 numbering, 73 e-invoicing, 74 invoice layout designer, 75 field configuration, 76 users and roles, 77 audit log |
| Tenant | 80 subscription and billing, 81 companies, 82 data export, 83 support access approvals |
| Provider console | 90 tenants, 91 plans, 92 support access, 93 announcements, 94 platform health (separate shell) |

## State conventions
- **Loading:** skeleton shaped like the screen (table rows, form fields, report lines) with a "loading" status for screen readers; forms on sign-in show "verifying" over the form.
- **Empty:** a first-time or filtered-empty message saying what will appear and the next action (e.g. "Clear filters", "Use the transport template"). Screens that always hold data (settings, plans) show their defaults with a first-visit note.
- **Error:** field errors next to the field plus an error summary (UX-16), business refusals with the PRD reason (closed period, mapping incomplete, above remaining balance, plan limit, last Owner…), or a load failure with retry and a reference number.
- **Success:** a toast naming what happened (UX-19), or the next step / confirmation dialog where the flow continues (e.g. weight-basis confirmation, invoice void with reason).

## Decisions taken without asking (recorded in BUILD_STATUS as DT-65 … DT-69)
- DT-65 Digits and dates: Western digits in the Arabic interface, each number isolated as a left-to-right run; default date format YYYY-MM-DD (users can change it, FR-178).
- DT-66 Menu: "Setup checklist" appears as a top-level item while setup is incomplete; the "Tenant" group appears only for core.tenant.manage; the provider console has its own shell.
- DT-67 Default density: compact (32 px rows); comfortable 40 px.
- DT-68 Saved views storage (no schema change): stored as rows of `cfg.TableColumnViews` under namespaced keys (`<list>#view:<name>`), with the Columns JSON holding columns + filters + name; personal (UserId) or company-shared (UserId NULL, settings.manage). Built-in views ("Unbilled", "Needs review"…) are defined in code.
- DT-69 Density preference: stored per device in the browser (no column in `core.Users`); shown in the profile as "saved on this device".

