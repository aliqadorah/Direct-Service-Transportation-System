# Step 4 · Block B11 — Collaboration: delivery and review

_Completed 10 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied._

## What was built (backend; screens follow in step 8)

| Area | Implementation | Requirements |
|---|---|---|
| Live changes | `LiveChangeInterceptor`: business records of a company changed by a save are announced to that company's SignalR group (record type, URL token, action — never ids or values) after the commit; nothing on rollback, nothing for changes undone by a savepoint rollback or a failed save; more than 100 changes of a type in one commit → one "Bulk" message; technical rows (locks, sessions, notifications) not announced; publishing never fails or blocks the business operation. `IAfterCommit` runs other post-commit actions (notification pushes) the same way | FR-185, RT-01, RT-02 |
| Record locks | Built in B1 (acquire, heartbeat, release, expiry, lock-changed messages); now complete with live record updates | FR-186 |
| Global search | Receipt and document numbers (trips, invoices, credit notes, receipts, advances, settlements, partner payments, AP/AR documents, journal entries), drivers (name, plate, phones; national ID only with drv.view), customers, partners, accounting parties; Arabic spelling variants folded on both sides with `TRANSLATE` (أ إ آ → ا, ة → ه, ى → ي; diacritics and case by the collation); digits in ASCII; five hits per type; each type only with its permission (DT-118) | FR-187 |
| Notifications | Per user (and company); text stored in both languages as readable JSON within the 500-character column, shown in the reader's language; pushed to the user's connections after commit; list with unread count, mark read, mark all read. Raised for rejected / failed e-invoices (inv.issue holders; acct.apar for AP/AR), failed postings (je.export holders), finished and failed exports, accepted invitations (B1); support-access requests in B12 | FR-190 |
| Audit log viewer | The company's rows plus the tenant's user, role, permission, invitation and sign-in rows; filters by user, action, record type, period, text; records shown by number or name (never an internal id); changed fields old → new, reason, IP; driver personal fields masked without drv.view; Excel/CSV/PDF export; read-only; settings.manage or users.manage (DT-121). New audit rows keep Arabic readable (search works). Every export of a company route is now recorded as an "Export" action (route and file name) | FR-188, FR-189 |
| Background exports | `POST /exports` (report builder, saved report, standard report, audit log) checked with the caller's permissions, queued (at most 3 waiting per user), run by `ExportWorker` with the requester's current permissions and background limits (builder detail up to 100,000 rows), stored under the requester's folder for seven days (`IFileStore`, local folder in development, DEF-01), notification with the download link; only the requester downloads it, in that company | FR-151 |

9 new operations (OpenAPI 332 paths, Redocly lint: valid).

## Tests — 294 passing
Api.Tests `CollaborationTests`: a created customer is announced after commit and a refused save announces nothing; search finds «مؤسسة الأمانة» from «مؤسسه الامانه» and an invoice by number, a Dispatcher does not see invoices, another tenant finds nothing; a rejected e-invoice notifies the Owner with the submission link, read / read-all; audit log shows the customer's creation by name, the audit export is itself recorded, Viewer refused, notifications personal; a background CSV export is notified, downloadable by its requester only, and a Dispatcher cannot queue an audit-log export. Application.Tests: notification text always fits with Arabic unescaped. The test host records live messages instead of SignalR (`RecordingNotifier`).

## Code review and security review (independent reviewer + author)
- **Fixed:** H1 notification text could exceed its column (Arabic `\u`-escaped JSON) and fail a document save inside the failed-posting path — readable JSON, budget shared between languages, trimmed to fit; M1 changes undone by a savepoint rollback or a failed save inside a transaction were still announced; M2 the post-commit publish used the request's cancellation and could turn a committed save into a 500 — now `CancellationToken.None`, all errors swallowed; M3 notifications were pushed before the commit — now after it (`IAfterCommit`); M4 driver national ID and bank details visible in the audit log without drv.view — masked; M5 sign-ins and role/permission changes (no company) were missing from the company log — included for the tenant; M6 Arabic in audit JSON was escaped and unsearchable — readable for new rows; M7 audit-log export now re-checks permission when the job runs; M8 one user could fill the export queue — three waiting jobs per user; L1 read-only tenants can still read notifications and queue exports (BR-013); L2 export audit also matches `/exports`; L4 roles soft-deleted no longer receive notifications; L5 no blocking on the synchronous commit path; L6 notifications are not audited; L7 export file names ASCII.
- **Accepted:** L3 search runs one `LIKE '%…%'` query per record type (about 12, five hits each) — fine for the 500,000-trip target with a client debounce; full-text or prefix indexes are a candidate if measured slow in step 9.

| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-B11-01 | Medium | Personal data in the audit log without drv.view (M4) | **Fixed** |
| SR-B11-02 | Medium | Background audit export with stale permission (M7) | **Fixed** |
| SR-B11-03 | Medium | Export queue exhaustion by one user (M8) | **Fixed** |

Isolation (notifications by owner, export files by tenant/company/user and random job id, key validation in the file store, live messages to the record's own company group, search per company and type permission, audit record names through the tenant/company filters) checked and found sound. No new packages.
