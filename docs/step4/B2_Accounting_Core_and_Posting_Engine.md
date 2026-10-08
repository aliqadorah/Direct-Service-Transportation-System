# Step 4 · Block B2 — Accounting core + posting engine: delivery and review

_Completed 8 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied. Chart-of-accounts template approved by the owner (D-10, "approved as drafted, including your choices on points 1–3") and seeded as the built-in template._

## What was built (backend only — no UI code before mockups, step 6)

| Area | Implementation | Requirements |
|---|---|---|
| Posting engine | `PostingEngine`: posting keys → accounts, balanced (BR-033), open period checked before **and after** the journal-number lock (BR-034/036), active postable leaf accounts (FR-093), control-account party matching (FR-106, G-17), amounts within currency decimals and DECIMAL(18,3); gap-free JV- numbers; linked reversal dated like the original (FR-099, DT-22); `PostOrPendAsync` savepoint → `acc.PendingPostings` on technical failure (FR-101) | 8.6, FR-096…101, ADR-09 |
| Posting readiness | Required keys (12 + opening difference during setup); automatic posting refused while unmapped (FR-094); banner endpoint `/accounting/status` | FR-094, G-04 |
| Pending postings | List (je.view), retry one/all (je.export), worker every 5 min with back-off; `IPostingSourceHandler` registry that the document modules (B5, B6, B8, B9) implement to rebuild, attach and describe their entries | FR-101, FR-102, ADR-10 |
| Chart of accounts | Tree list (level, parent, children), add/edit/move (no cycles, child has parent's type), deactivate (refused while mapped/linked/with openings or active children), activate, soft delete of unreferenced accounts with counted refusal (BR-040) | FR-107, FR-108, FR-093 |
| Transport template (D-10) | 47 accounts, main cash box on 110101, 13 posting-key mappings, only for an empty chart | FR-107, REL-09 |
| Chart import/export | Excel export with separate opening debit/credit; import preview (duplicates, unknown parents, invalid types, type mismatch, cycles, parents in use) and strict import logged in `cfg.ImportBatches` | FR-109, ADR-13 |
| Opening balances | Per-account debit/credit; one opening entry (ACC/Opening); re-post = reverse + post; difference to `ACC.OpeningBalanceDifference` only while setup is open; warning until that account is 0.000 | FR-110, G-04 |
| Account mapping | Every key with account and Mapped/Missing; only active postable leaves; audited old → new | FR-092…095 |
| Cost centres, cash boxes, bank accounts | CRUD, archive/restore, delete when unreferenced; own postable asset account per cash box/bank (DT-25); IBAN by company pattern | FR-129, FR-131 |
| Periods | Year view (12 months), pre-close check (pending postings, draft invoices, control-account amounts without party), close with reason (+confirm when checks are open) under the journal-number lock, reopen with reason; audited | FR-132…134, BR-036 |
| Manual journal | Create; edit = reverse + re-post (D-05); void by reversal with reason (number kept); reversals themselves cannot be edited/voided; record-lock guard; import (CSV/Excel) with preview and strict commit | FR-111…114, D-05 |
| Journal views | Journal list (acct.view) and automatic-entries list (je.view, automatic only) with filters; entry detail with source and reversal links; Excel export; voucher data with amount in words (Arabic + English) | FR-102, FR-103, FR-115, BR-024 |
| Journal export | JSON / CSV batch per period range, JEX- number, logged (user, time, periods, count, totals), entries point to their latest batch | FR-104 |
| Files | `ISpreadsheetService` (ClosedXML + CsvHelper): values only, formula cells refused, size/row/column and unpacked-size limits, CSV injection neutralised on export | ADR-13, SEC-13 |

57 new endpoints under `/api/v1/companies/{company}/accounting/*` and `/ledger/*`, each with a permission key and an OpenAPI summary (test-enforced). OpenAPI exported to `docs/api/openapi-v1.json` — Redocly lint: valid, 0 warnings.

Deferred to the blocks that own them (by design): source-document handlers (B5 trips, B6 invoices/credit notes, B8 cash documents, B9 AP/AR); voucher **PDF** and journal-list PDF (print engine, B6); FR-105 reconciliation report, FR-116…121 financial reports, FR-130 statements, cost-centre report (B9/B10); dashboard cards for pending postings and opening difference (B10); FR-135/136 maintenance zone (B9).

## Tests — 143 passing (85 → 143)
| Project | New | Covers |
|---|---|---|
| Application.Tests | 40 cases | Amount in words (EX-1, EX-2, EX-4 amounts; dual/plural/construct forms; English); template tree integrity and mapping (D-10 points 1–3); required keys during/after setup; G-17 party matching; half-up rounding; amount limits; month format |
| Infrastructure.Tests | 7 | Engine on SQL Server: JV numbering and lines (EX-1 wage), unbalanced/precision refusals without consuming numbers, closed period, unmapped keys, control parties, reversal mirror and links, **technical failure → savepoint → pending posting → retry with a handler** (simulated with a command interceptor) |
| Api.Tests | 11 | Template; tree rules; mapping; manual post/edit/void incl. reversal protection; cost centres/cash/bank; opening balances incl. setup rule and migration-complete refusal; period close/reopen blocking post and void; journal export JSON/CSV incl. CSV injection; chart and journal imports; role permissions. The generic isolation and endpoint-coverage tests cover all 57 new endpoints |

## Code review — summary
- **Intent:** a single posting path for every module, with refusals as business errors and technical failures parked as pending postings.
- **Fixed during review:**
  - CR-B2-01 **High**: a reversal entry of a manual entry could be voided or edited, which brought the original amounts back. Now refused (`journal.isReversal`), with a regression test.
  - CR-B2-02 Medium: when a technical error dooms the transaction (deadlock victim, broken connection), the rollback to the savepoint fails. The original error is now rethrown and the operation fails cleanly.
  - CR-B2-03 Medium: a repeated failure for the same document now updates its open pending row (one per document, `UX_PendingPostings_Source`) instead of breaking the save.
  - CR-B2-04: everything tracked since the savepoint (entry, lines, audit rows) is detached before the pending row is saved.
  - CR-B2-05: ambiguous party names in the journal import are rejected instead of guessed. Accounting parties are keyed by role ("Supplier").
  - CR-B2-06: the worker takes the oldest attempts first.
  - CR-B2-08: export updates run in chunks of 1,000; the O(n²) lookup is replaced by a dictionary.
  - CR-B2-09: an account with opening amounts cannot be deactivated.
- **Accepted (documented):**
  - Manual-entry party lookups run one query per line, and the chart import runs about 7 count queries per existing parent account. Both are bounded by the entry and chart sizes.
  - Export stamps `ExportBatchId` with a bulk update. This bumps RowVersion (a user editing the same manual entry at that moment gets the normal 409) and writes no per-entry audit rows; the batch row itself is audited.

## Security review — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B2-01 | Medium | Amount check allowed values above DECIMAL(18,3), so a save failed with a 500, or with a pending posting that could never succeed | **Fixed**: limit 999,999,999,999,999.999 on lines and totals; unit test |
| SR-B2-02 | Medium | Size check was on the compressed .xlsx only; zip bomb or huge column range | **Fixed**: unpacked size ≤ 60 MB, ratio check, ≤ 60 columns, checked before parsing |
| SR-B2-03 | Low | Upload body buffered before the size check | **Fixed**: request size and multipart limits (11 MB) on the 4 upload endpoints |
| SR-B2-04 | Low | Raw exception text stored in the pending reason and shown to je.view users | **Fixed**: generic bilingual reason + reference; exception logged server-side |
| SR-B2-05 | Low | Export (allowed while read-only) writes a batch row during a support session | **Fixed**: support sessions refused (D-13); suspended tenants may still export (PRD 5.2) |
| SR-B2-06 | Low | Large export memory/CPU | Partly fixed (dictionary, chunks); row cap 200,000 per export. Streaming deferred to B10 background exports |
| SR-B2-07 | Low | Reference counts per parent during chart import | Accepted (bounded by chart size) |
| SR-B2-08 | Info | CSV import formula check covered only `=` | **Fixed**: `=`, `@`, and `+`/`-` not followed by a number |
| SR-B2-09 | Info | Sequence lock silently locked nothing when the sequence row was missing | **Fixed**: throws |
| SR-B2-10 | Info | The engine trusts the TRN party ids its callers pass | Noted for B5/B6/B8 handler reviews |

Isolation, permissions (PRD 3.4), antiforgery on multipart uploads, period race and double posting were checked and found sound. Dependency scan `dotnet list package --vulnerable --include-transitive`: clean, including ClosedXML 0.105.1 and CsvHelper 33.1.0.
