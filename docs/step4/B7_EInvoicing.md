# Step 4 · Block B7 — National e-invoicing: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied._

## What was built (backend; screens follow in step 8)

| Area | Implementation | Requirements |
|---|---|---|
| Settings | Per company (provisioned row): on/off, connector, seller tax number, registration type (income / general sales / special), income-source sequence, environment (Test/Production), API client id and secret encrypted with Data Protection (own purpose), write-only (an empty value keeps the stored one; responses only say whether they are stored; audit writes `***`). Switching on requires every value a submission needs; row version required | FR-059, SEC-09 |
| Connectors | Provider-neutral `IEInvoiceConnector` (market rules, national format, submission) chosen per company; **JoFotara** (UBL 2.1, POST `{"invoice": base64}` with Client-Id/Secret-Key headers; endpoints configurable per environment) and a **simulator** (same UBL, checks the sums, answers like JoFotara; `[reject]` / `[fail]` in the customer name exercise the other paths) offered only where `EInvoicing:AllowSimulator` is set and never in Production | FR-066, INT-01, NFR-09 |
| Document | `EInvoiceDocumentBuilder` builds the market-neutral document from current data: stored net subtotal, discount and fees split over the lines (exact sums), fees as their own line, tax per line with the rounding difference to the stored tax spread one smallest unit per line, so the national document totals exactly what was posted and printed (DT-82); customer tax number, phone and governorate; credit notes reference the original invoice's number, unique ID and total and carry the reason | FR-060, FR-064 |
| JoFotara format | Invoice 388 / return 381 with the three-digit type (local · cash or receivable · income / general / special), ICV in AdditionalDocumentReference, income-source sequence in SellerSupplierParty, per-line discount and tax, header allowance = Σ line discounts, LegalMonetaryTotal, `currencyID="JO"` as in the guide's samples; market rules: special tax not supported, income registrations carry no sales tax | FR-060 |
| Queue | Invoice approval and credit-note creation queue a submission with the next **ICV** inside their own transaction (one atomic `UPDATE … OUTPUT`, gap-free with the document, one counter for all modules); the worker is woken at once | FR-043, FR-061 |
| Submission | `EInvoiceService.ProcessAsync` claims the row (UPDLOCK + READPAST, no double submission), validates (problems listed, document stays Queued, no attempt counted), submits, stores the request and the response; **accepted** (EINV_STATUS SUBMITTED / ALREADY_SUBMITTED) → Submitted with unique ID, system number, QR and time; **refused** → Rejected with the reason; **technical failure** → retried with back-off (2, 4, 8 … ≤ 60 min), after 5 attempts Rejected with the last error ("Failed", D-04). Manual submit / resubmit by inv.issue rebuilds the corrected document with the same UUID and ICV | FR-061, FR-062, FR-063, D-04 |
| Credit notes | Sent only after the invoice is accepted (stays Queued, "waiting for the invoice"; sent automatically once the invoice is accepted); an accepted credit note cannot be voided | FR-064 |
| Voids | Invoice void refused once accepted (cancel by full credit note, FR-046); a queued or rejected submission is cancelled (NotRequired, reason kept). The void waits for a submission in progress (row lock) | FR-045, FR-046 |
| QR | The QR returned by the national system is printed on invoices and credit notes (B6 print engine) | FR-062, FR-057 |
| Worker | `EInvoiceWorker` (hosted service): signalled on queueing/acceptance, otherwise every 2 minutes; per company scope as a system job; suspended tenants and archived companies skipped | FR-061, NFR-17 |
| List & report | Submissions with status (incl. "attention" = Rejected/Failed or queued with problems), document type, customer, period, number/system-number search, sort, paging, totals per status; Excel/CSV export; submission details with the document sent and the response; a document's submission from the invoice and credit-note views | FR-061, FR-065, FR-048 |

8 new endpoints (OpenAPI 227 paths, Redocly lint: valid, 0 warnings): `/settings/e-invoicing` (GET, PUT — settings.manage), `/einvoice-submissions` (list and export — inv.view or reports.view; details — inv.view; submit — inv.issue), `/invoices/{id}/e-invoice` and `/credit-notes/{id}/e-invoice` (inv.view).

**Format source and verification (R-01, TST-04).** No official ISTD guide was available to the build; the JoFotara format and response fields follow public secondary descriptions of the ISTD technical guide v1.5 (Qoyod knowledge base: endpoint, request, response, XML examples; ERPNext connector notes). Everything format-specific sits in `JoFotaraUbl` / `JoFotaraConnector` and must be confirmed with the owner's test credentials on the JoFotara test environment before launch (REL-04).

Deferred: AR receivable documents (D-15) reuse the same builder, counter and connector in B9 (`acc.ApArEInvoiceSubmissions`); dashboard alert for rejected e-invoices (B10, FR-138) and in-app notification (B11, FR-190); settings screen and status badges (step 8).

## Tests — 257 passing (240 → 257)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 6 | PRD 8.3 invoice as national lines (fees line, discount and tax add up exactly, each line within 0.001 of its own rounding); smallest-unit split; rounding difference spread one unit per line; tax-inclusive invoice and its credit note split exactly (DT-79, review M4) |
| Infrastructure.Tests | 5 | JoFotara UBL: type 388/381 and three-digit name (022 / 012), ICV, income-source sequence, governorate, escaped buyer name, totals with `currencyID="JO"`, tax category; credit-note BillingReference and reason; market rules (special, income with tax); responses judged by EINV_STATUS (SUBMITTED, ALREADY_SUBMITTED, NOT_SUBMITTED with coded reasons, 401, 502 HTML, 429); credentials never printed |
| Api.Tests | 6 | Settings: required values when switched on, Simulator not in Production, credentials write-only and encrypted (not in responses, columns or audit), empty keeps, row version required and checked, header-safe credentials, Dispatcher refused; approval queues with ICV 1, 2; the worker submits; system number, QR, request/response stored; QR printed; void of an accepted invoice refused; second submission refused; credit note waits for the invoice, then references it (381, BillingReference, reason); accepted credit note cannot be voided; rejection → correct the customer → resubmit with the same counter; technical failure → outcome unknown blocks the void → Rejected after the maximum attempts; connector switch refused while documents are pending; disabled company: problem listed and document kept queued, new invoices and their credit notes not queued; void cancels a queued submission; report totals by status, status filter, Excel export; Viewer lists but cannot submit, Dispatcher sees the report (reports.view) but not the document; cross-tenant 404 |

Not covered by automated tests: the real JoFotara endpoint (needs the owner's test credentials, TST-04) and concurrent races between two processes (covered by the row-lock design; reviewed).

## Code review — summary (independent reviewer + author)
- **Fixed:**
  - H1: a void could cancel a document the national system may already hold (timeout after the request arrived, crash after an accepted answer). Attempts are now marked in flight before the call (committed), technical failures are kept as "outcome unknown", and a void is refused (`einvoice.outcomeUnknown`) until a resubmission answers — the same UUID makes the national system reply ALREADY_SUBMITTED if it has the document.
  - H2: a credit note on an invoice approved before e-invoicing was switched on waited forever; credit notes are queued only when their invoice is e-invoiced.
  - M1: stuck documents could starve new ones; the due list is decided in SQL — never-tried first, technical failures after their back-off, documents with problems re-checked every 10 minutes (their re-check time moves without an audited change), oldest first; credit notes become due as soon as their invoice is accepted.
  - M2: switching connector or environment while documents are queued or rejected is refused (`einvoice.pendingDocuments`).
  - M3: the HTTP call ran inside a database transaction holding the row lock (voids could time out); processing is now claim + build (short transaction) → call (no transaction) → store the outcome (short transaction).
  - M5: undecryptable credentials (lost Data Protection keys) are reported as a credentials problem; client id and secret must be visible ASCII (sent as headers).
  - L1 ALREADY_SUBMITTED keeps earlier identifiers and QR; L3 internal invoice notes are not sent; L4 a credit note needs its reason in the document; L5 the HTTP client redacts the credential headers in logs.
- **Accepted / to confirm (R-01, TST-04):**
  - M4: per-line tax may differ from the line's own rounding by a few fils on tax-inclusive documents (the document still equals the books); JoFotara's per-line tolerance (0.001 per public descriptions) must be confirmed on the test environment.
  - M2 remainder: documents accepted in Test keep their status after switching to Production, and the counter continues (ICVs are unique per company in the design); use a test company for test-environment trials.
  - L2: cancelled documents leave counter values that are never sent; to confirm with ISTD.
  - L6: any 4xx with a JSON body is treated as a content rejection.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B7-01 | High | Void of a document the national system may hold (H1) | **Fixed** |
| SR-B7-02 | Medium | Database connection and row lock held during the external call (M3) | **Fixed** |
| SR-B7-03 | Medium | Header injection / undecryptable credentials (M5) | **Fixed** |
| SR-B7-04 | Low | Credential headers in HTTP logs (L5) | **Fixed** (explicit redaction; .NET already redacts by default) |
| SR-B7-05 | Info | Credentials storage | Checked: Data Protection with a dedicated purpose, write-only, masked in audit, `ToString` redacted, never returned |

Isolation (raw SQL parameterised and filtered by company; the worker runs each company in its own scope; tokens resolve through company-filtered sets), permissions per endpoint, XML built with escaping (no injection), no user-controlled URL (no SSRF; endpoints come from configuration) were checked and found sound. No new packages (HttpClientFactory is part of the ASP.NET Core framework).
