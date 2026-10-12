# Visual layout check — 12 Oct 2026

_Owner feedback of 12 Oct 2026 (`docs/feedback/`): overlapping elements in the role editor (SCR-76) and on the two-step set-up page (SCR-05). Follow-up: a check of every screen and dialog, and an automated test so that overlapping text cannot come back._

## How the check works

`tests/e2e/specs/visual.spec.ts` opens, in Arabic (RTL) and English:

- the account pages (sign-in, sign-up, forgot password, two-step set-up with the QR code, recovery codes, two-step sign-in);
- every company and tenant screen (the routes of the accessibility check, plus driver edit, new advance and the 15 help pages), with real documents so detail screens have content;
- 36 dialogs, side panels and menus (role editor — new, preset, custom, delete —, invite, user panel, new supplier/customer/cash box/bank/cost centre/material/partner/site/type/tariff/custom field, numbering, period close, opening-entry post, data-migration confirm, voids of receipt/advance/invoice/trip, driver and customer delete, customer panel, column chooser, driver pick list, Ctrl+K bar, notifications, user menu, new company, cancel subscription);
- the provider console screens and their dialogs (tenant panel, extend trial, new plan, new announcement, support request).

Each one is checked at **1280 and 1366 px wide**, in **compact and comfortable** density, first at a laptop-like height (680 px: dialogs, sticky parts) and then at the height of the whole content (so text below the fold is checked too). `tests/e2e/support/layout.ts` reports, from the rendered boxes:

| Problem | Meaning |
|---|---|
| overlap | two pieces of visible text painted over each other, or a picture/QR code/field over text — unless something opaque (dialog, panel, dropdown, sticky row) covers one of them |
| cut-off | text cut off by a box that clips it without an ellipsis; a one-line control (button, tab, badge) whose label wraps onto a second line |
| page-overflow | the page scrolls sideways |

The test fails on any of them and saves a screenshot of every combination (`TestResults/visual/<lang>/<width>-<density>/`). `tests/e2e/specs/visual-checker.spec.ts` rebuilds the reported layouts in plain HTML and proves the checker catches them, and that it stays quiet on dialogs over the page, scrolled lists, ellipses, unit adornments inside fields and sticky total rows.

Misalignment that is not an overlap (e.g. fields at different heights) is not detectable this way; it was reviewed by eye on contact sheets of all screenshots.

**Result after the fixes (12 Oct 2026):** 12/12 green — account pages, every screen (including the 15 help pages), dialogs/panels/menus and the console, in both languages, at both widths and densities, plus the 4 checker self-tests.

## What was fixed

| Screen | Problem | Fix |
|---|---|---|
| SCR-76 role editor (Settings → Users & roles → role) | Permission groups and rows overlapped, checkboxes detached from their descriptions, group headings in English on the Arabic screen | Cause: the dialog body is a scrolling flex column and each group (`.stack`, min-height 0) was squeezed to fit. Dialog body blocks no longer shrink. The list is rebuilt: group heading, then rows of checkbox + description + key in small text, scrolling inside the dialog; headings translated (AR/EN) |
| SCR-05 two-step set-up | QR code over the instructions and the 6-digit code field | The QR SVG kept its fixed pixel size inside a smaller box. Now stacked: steps, QR scaled into its box, manual key, step 3, code field |
| SCR-74 invoice layout designer | "Annex columns and order" collapsed to a sliver, rows over each other and over the footer field (found by the check, Arabic and English, all sizes) | Same cause as SCR-76 (scrolling form). Groups of fields (`fieldset`) never shrink below their content, everywhere |
| SCR-10 trips quick entry | At 1280 and 1366 px the five pick lists were ~50 px wide, labels wrapped ("Unloading" over "Material", `*` on its own line), fields at different heights, the date cut off | The row wraps onto two lines when the window is narrower (one line on wide screens); pick lists at least 150 px, labels on one line |
| Top bar (every screen) | The search text ("Search by trip, invoice number or driver name…") wrapped onto two lines in English | One line with an ellipsis |

## Found, not changed here

- The built-in ownership type is stored as "Company" (DT-15) and shown in English on Arabic screens (drivers list, driver profile, ownership types). Translating it touches every place driver data is shown (lists, pick lists, reports, exports) — proposed as a follow-up.
- Placeholder text and the value inside a field are not part of the page's text, so the checker cannot see them being cut off; the quick-entry date was found by eye.
