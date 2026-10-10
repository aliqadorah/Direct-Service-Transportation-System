# Step 4 · Block B6 — Invoicing & credit notes: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied._

## What was built (backend; screens follow in step 8)

| Area | Implementation | Requirements |
|---|---|---|
| Trip picker | The customer's active, unbilled trips with period, loading site, unloading site and material filters, totals (count, quantity, revenue); select-all = every listed trip (max 5,000) | FR-038 |
| Drafts | Trip-based (one line per trip: billing quantity × frozen price = trip revenue) or manual lines (description, quantity, unit price each > 0); invoice date not in the future nor in a closed period; due date = invoice date + payment terms unless given; discount 0 … subtotal; extra fees with a required description; layout Claim by default (rev. 5); a draft reserves its trips (`Trip.InvoiceId`, trip edit/void refused) and can be edited (row version required), printed with a DRAFT stamp, approved or deleted (soft, with reason, trips released) | FR-039, FR-040, FR-042, 7.3.1 |
| Approval setting | On (default): Save creates a draft; off: Save approves directly; "Save & approve" always available to inv.issue | FR-041 |
| Approval | Refused while account mapping is incomplete, the date is in a closed period, a trip is no longer active and on this invoice, or the total is not above 0; next gap-free INV- number inside the transaction; revenue entry (Dr receivables total with the customer, Dr discount; Cr transport revenue, other revenue, sales tax output); trips marked invoiced in their history; pending-posting handler for retries | FR-043, FR-044, FR-094, 8.6 |
| Totals | `InvoiceCalculator`: base = subtotal − discount + fees, tax = round(base × rate) once, total; tax-inclusive mode derives net amounts so the design's checks hold (DT-79); half-up at each amount | 8.3, BR-020…023 |
| Void | inv.void with reason; refused for drafts, with active receipts or credit notes, when the national e-invoicing system accepted the invoice (cancel by full credit note), or in a closed period; reversal dated like the original, number kept, remaining 0, trips released | FR-045, FR-046 |
| Credit notes | From an approved invoice: trip lines returned whole, manual lines by quantity up to what is not yet returned; reason required; date not before the invoice nor in the future/closed period; returned share carries the same share of discount and fees, tax at the invoice's rate (PRD example 180 → 204.450); never above the remaining balance (BR-026); returning everything still open takes exactly what is left of each component, so the remaining balance is exactly 0.000 (BR-027); own CRN- sequence (a raced idempotent create rolls back, so no number gap); mirror entry; returned trips unbilled again; invoice returned amount, remaining and status (Closed at 0) updated | FR-049…051, BR-025…027 |
| Credit-note void | inv.void with reason; refused in a closed period or when a returned trip was invoiced again or voided; reversal; trips back on the invoice; balance restored | FR-052 |
| Lists and views | Invoice list with status, customer, period, paid/unpaid/partial, overdue, e-invoice status, number search, sort, paging and totals; view with lines (trip annex fields, returned quantities), credit notes, amount in words, history (approve, prints, credit notes, void); credit-note list and view | FR-047, FR-048 |
| Layouts | One per document type and company (provisioned with the company): title, header band, grouping of the claim's first page (loading site, route, material), annex columns in order (13 built-in trip columns + the trip custom fields), labels, footer, signature boxes, logo, QR; live preview with sample data | FR-054…056 |
| Printing | `InvoicePrintService` builds A4 HTML (RTL Arabic or English): company header with logo and tax/CR numbers, customer with tax number, number and dates, claim page 1 grouped totals + page 2 shipment annex, or tax-invoice lines, totals with tax, amount in words (BR-024), e-invoice QR (when accepted, B7), signatures, footer, DRAFT/VOID stamps; `IPdfRenderer` (Infrastructure `ChromiumPdfRenderer`, PuppeteerSharp, headless Chrome/Edge — Q-08) renders the same HTML to PDF with page X of Y; every print logged in the document's history (`AuditTrail`) | FR-053, FR-057, FR-058, UX-22, UX-23 |

18 new endpoints under `/invoices`, `/credit-notes`, `/invoice-layouts` (inv.view: lists, views, history, print; inv.issue: picker, create, edit, approve, delete draft, credit notes, layouts; inv.void: voids).

Deferred: e-invoicing build/submission/QR (B7 — FR-059…066; void of an accepted invoice already refused); receipts against invoices (B8 — `PaidAmount` and `Rebalance` ready); print of vouchers with "Copy" (B8, FR-090); fonts bundled locally for the PDF engine (step 7; today the HTML links Google Fonts with Segoe UI/Tahoma fallbacks).

OpenAPI exported (220 paths) — Redocly lint: valid, 0 warnings.

## Tests — 240 passing (221 → 240)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 11 | PRD 8.3 invoice example (720 − 20 + 5 → 705.000, tax 112.800, total 817.800) and credit-note example (180 → 5.000 / 1.250 / 28.200 / 204.450); BR-027 absorption over three partial credit notes; tax-inclusive mode: each part converted separately, the design's checks hold and the total equals the quoted amount (4 cases incl. discount = lines and fees above lines); credit on an inclusive invoice; cap to the remaining balance within rounding; half-up line amounts and tax once on the base |
| Api.Tests | 8 | Picker, draft reservation (trip edit/void refused), approval with INV- number, revenue entry and trip history, list filters and totals; approval setting off, refusal without mapping (FR-094), future date, fee description, another customer's trip; manual invoice and draft deletion; credit notes (shares, mirror entry, trips unbilled, line returned twice refused, invoice void refused, full return to 0.000 and Closed, credit-note void restores, refused once the trip is re-invoiced); void with reversal, number kept, VOID stamp, print logged; layouts (amount column required, labels, preview), claim with annex, tax invoice, real PDF, credit-note print; review fixes (customer text HTML-encoded in prints, layout kept on edit, draft delete needs its version, unknown layout type 400, layout save needs its version, credit-note void refused after the returned trip was edited); permissions (Viewer, Dispatcher, Accountant) and cross-tenant 404 for views, prints and trips |

The PDF test needs a local Chrome or Edge (or `Printing:ChromiumPath`); the build server must provide one (step 9).

## Code review — summary (independent reviewer + author)
- **Fixed:**
  - H1: tax-inclusive totals converted the gross as one amount and derived the subtotal from it, so the stored discount could exceed the subtotal and fees could be posted gross (breaking the design's CHECKs and the revenue split); each part (lines, discount, fees) is now converted to net separately, base = subtotal − discount + fees and tax = gross − base, so the total is what the customer was quoted (DT-79, tests added).
  - M1: credit notes used the company's current tax mode instead of the invoice's; the mode is now taken from the invoice (net subtotal ≠ sum of line amounts).
  - M2: with receipts on the invoice, a proportional credit note over the remaining balance by rounding was refused (BR-026); a difference up to 0.010 now takes exactly the remaining balance, absorbed in the tax.
  - M3: custom trip fields chosen as annex columns printed empty; the lines carry the trip's custom values and the annex prints them (FR-056).
  - M4: the PDF engine loaded any URL referenced by a document; JavaScript is disabled and only `data:` URIs and the brand web fonts may load (everything else aborted, so no server-side request forgery), and an unreachable font server no longer stalls rendering (4 s, then fallback fonts).
  - M5: voiding a credit note put back a trip that had been edited since (its revenue no longer the returned amount), leaving the invoice inconsistent with its trips; the void is refused (`credit.tripChanged`).
  - L1 an edit without a layout kept resetting to Claim; L2 the e-invoice check on void moved inside the transaction; L3 an unknown layout type returns 400, not 404; L4 the "unpaid" filter excludes partly returned invoices; L5 the logo's media type comes from its bytes; L6 a render is limited to 60 s; L7 deleting a draft requires its row version; L8 saving over a stored layout requires its row version.
- **Accepted / noted:**
  - Fonts are linked from Google Fonts (allowed by the PDF engine); bundling them locally is part of step 7 (design system), after which the allow-list shrinks to `data:` only.
  - E-invoice queueing on approval, QR content and credit notes after acceptance come with B7.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B6-01 | Medium | PDF engine could fetch arbitrary URLs from document content (M4) | **Fixed** (JS off, request allow-list) |
| SR-B6-02 | Medium | Credit-note void restored a changed trip (M5) | **Fixed** |
| SR-B6-03 | Low | Draft delete and layout save without row version (L7, L8) | **Fixed** |
| SR-B6-04 | Low | Unbounded render time could hold the print gate (L6) | **Fixed** (60 s per render, one render at a time) |
| SR-B6-05 | Info | User text in printouts | Checked: every value is HTML-encoded (test with a markup customer name) |

Isolation (invoices, lines, trips, credit notes and layouts resolve through the company's filtered sets; a token from another tenant gives 404 or a validation error), permissions per endpoint (inv.view / inv.issue / inv.void), money arithmetic (half-up at each amount, CHECK-compatible totals, gap-free numbers inside the transaction), posting and reversal through the engine, and mass assignment were checked and found sound. New packages: PuppeteerSharp 25.11.0 (uses the installed Chrome/Edge; no browser download at run time) and QRCoder 1.7.0, both MIT, no known vulnerabilities at the pinned versions.
