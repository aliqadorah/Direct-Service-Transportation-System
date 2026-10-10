# Step 4 · Block B8 — Cash, drivers & partners: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied._

## What was built (backend; screens follow in step 8)

| Area | Implementation | Requirements |
|---|---|---|
| Common rules | `CashSupport`: method Cash / Bank (transfer) / CliQ; cash → an active cash box, transfer and CliQ → an active bank account with a required reference; date required, not in the future, not in a closed period; own gap-free sequence per document type (RCV-, ADV-, STL-, PPY-), taken inside the transaction; cash/bank posting line on the box's or bank's ledger account; idempotent creates by client id; row version required on edit and void; generic pending-posting handler (`CashPostingHandler<T>`) for retries and journal links | FR-089, 7.4.1, FR-101 |
| Receipts | Against one of the customer's approved invoices with remaining balance (≤ remaining; invoice paid amount, remaining and status updated under the receipt-number lock and the invoice row version) or on account; entry Dr cash/bank, Cr trade receivables with the customer; void with reason (reversal dated like the receipt, invoice balance restored, number kept); list with customer, period, method, cash box/bank, applied/on-account, status filters, number/reference search, totals; Excel/CSV export | FR-067 … FR-069, FR-071 |
| Advances | Numbered; entry Dr driver advances with the driver, Cr cash/bank; edit only while unsettled and outside closed periods (old and new date) — the entry is reversed and posted again when its amount, date, driver or money account changes, the change recorded old → new; void refused once settled (BR-032) | FR-072 … FR-074 |
| Settlements | Preview of the driver's open own-fleet trips and open advances up to a date with gross / advances / net and shortage suggestions; confirm with all or a chosen subset (BR-031), deductions (shortage, fine, other — note required for other; zero lines not kept), net = gross − advances − deductions ≥ 0 (8.5), method and cash box/bank; items linked and locked under the settlement-number lock; entry Dr driver payable (trips whose wage was accrued — posted or pending) and driver wage expense (the others, BR-031a), Cr driver advances, driver deductions, cash/bank (net); void releases trips and advances and reverses the entry | FR-076 … FR-081, 8.5 |
| Partner payments | Numbered; entry Dr partner payable with the partner, Cr cash/bank; never above the partner's current balance due (partner cost of active trips − active payments), checked under the payment-number lock; void with reason | FR-085, FR-086 |
| Accounts & statements | Driver account (open trips and advances, gross / advances / net due, balance owed, settlement history); partner account (trips, cost, paid, balance due, recent payments); driver and partner statements for any period with opening balance, events and running balance, totals and closing balance (partner lines carry route, weight, rate, cost); JSON, PDF/HTML print and Excel/CSV export | FR-076, FR-083, FR-084, FR-088 |
| Vouchers | Receipt voucher (customer, amount in figures and words, method, cash box/bank, reference, invoice or "on account", attachments count, signatures); payment vouchers for advances and partner payments (paid by / received by); settlement statement (driver and plate, trips, advances, deductions, gross, net, words, signatures) followed by the payment voucher for the net; VOID stamp; every print after the first marked "Copy"; every print logged | FR-070, FR-075, FR-082, FR-087, FR-090 |
| Print chrome | `DocumentPrinter` (shared with invoices): A4 shell in the reader's direction, company header, encoding helpers, HTML/PDF output with page X of Y | UX-22, UX-23 |

31 new endpoints (OpenAPI 253 paths, Redocly lint: valid) under `/receipts`, `/advances`, `/settlements`, `/partner-payments`, `/drivers/{id}/account|statement`, `/partners/{id}/account|statement`.

Deferred: cash and bank balances on the dashboard and the cash/bank statement (B10 with the ledger reports, FR-091); unapplied receipts in customer statements and ageing (B9/B10 reports read receipts with no invoice); PDF export of lists (B10 report engine).

## Tests — 262 passing (257 → 262)
| Project | New | Covers |
|---|---|---|
| Api.Tests | 5 | Receipts: above remaining refused, against an invoice (RCV- number, remaining 760.000, PartiallyPaid, entry Dr cash / Cr receivables with the customer), bank transfer needs a reference, on account, list filter and totals, invoice void refused with a receipt, voucher with amount in words and "Copy" on reprint, void restores the balance, second void refused, export. Advances and settlements (8.5): wage 111.475, advance with driver party, edit re-posts, preview of open items and totals, net < 0 refused, settlement 222.950 − 120 − 10 = 92.950 with balanced entry (driver payable, advances, cash), zero deduction lines dropped, settled trip/advance locked, empty settlement refused, driver account and statement net to zero, statement export, settlement print with plate, trips and the payment voucher, void releases items, subset settlement, item already settled refused. Accrual off (BR-031a): accrued trip → driver payable, the other → wage expense; shortage suggestion 0.840 t × 6.000 = 5.040. Partner payments: balance due = partner cost, above balance refused, payment with partner party, balance 0, statement with rate, paid trip cannot be voided (overpayment), voucher with reference, void restores the balance. Permissions: Viewer reads receipts but cannot record, Dispatcher views driver accounts but cannot record advances or read receipts, Accountant records; idempotent create (201 then 200); cross-tenant 404 |

Not yet covered by automated tests (listed by the review): closed-period refusals per document (the shared `IPeriodGuard` is covered in B2), concurrency races (covered by row versions → 409, reviewed), pending-posting retries per cash handler, archived box/bank/party paths.

## Code review — summary (independent reviewer + author)
No High findings: receipt/credit-note/void races, settlement vs trip/advance edits and double settlement end in 409 through row versions and number locks; entries balance with the right parties and dates; design CHECKs enforced; vouchers and statements HTML-encoded; the `DocumentPrinter` extraction leaves invoice output unchanged (extra CSS classes only).
- **Fixed:**
  - M1: a trip whose wage posting was pending counted as accrued at settlement, but a later retry could post nothing if accrual had been switched off; a settled trip's pending posting now always accrues, and only posted (not reversed) or pending wage entries count as accrued (also L3).
  - M2: a partner trip voided or made cheaper could leave the partner overpaid; refused under the partner-payment lock (`trip.partnerOverpaid`).
  - M3: archived drivers, partners and customers could not receive a final settlement, a payment or a receipt against an open invoice; allowed (DT-97); new advances and on-account receipts still need active parties.
  - L1 an advance keeps its stored, since-archived cash box/bank on edit; L2 receipt void checks the invoice's record lock; L4 statement export format is case-insensitive; L5 list totals count active documents only; L6 no payment voucher page for a zero net; L7 dead code removed.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B8-01 | Medium | Partner overpayment through a later trip change (M2) | **Fixed** |
| SR-B8-02 | Medium | Driver payable left in debit by a pending wage posting (M1) | **Fixed** |
| SR-B8-03 | Low | Receipt void without the invoice record-lock check (L2) | **Fixed** |

Isolation (tokens through company-filtered sets; `IgnoreQueryFilters` only by the foreign key of a record already in the company), permissions per endpoint (rcpt.manage, drv.view, drv.advance, drv.settle; inv.view reads receipts), mass assignment and encoding were checked and found sound. No new packages.
