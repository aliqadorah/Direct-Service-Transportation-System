# Security review — support access (owner testing 19 Oct 2026, item 2)

_Reviewed 19 Oct 2026. Scope: `SupportAccessService` (tenant side: list, approve, refuse, end), `ConsoleService` support section (request, staff list, new subscriber search), `RequestContextMiddleware`/`CompanyContextMiddleware` support sessions, the `/me` environment field, the screens `Tenant/SupportApprovals.razor` and `Console/SupportAccess.razor`, and a sweep of the whole application for the same defect. Decisions recorded as DT-213 … DT-216._

## Critical finding SA-1 — a tenant could see other tenants' support requests (fixed)
**What the owner saw.** On 19 Oct 2026 at 14:32 (Amman) staff member "Direct Service Admin" requested access to tenant 2 ("Ali Transportation", owner `ali@gmail.com`). Thirty seconds later the owner signed in as `aliabdelqader311@gmail.com`, the Owner of **tenant 3** — another tenant also named "Ali Transportation". Tenant 3's *Support access requests* page listed tenant 2's request. Tenant 2's Owner has never signed in (no session rows), so the request could only have been seen from tenant 3. The isolation test `A_tenant_never_sees_or_decides_another_tenants_request_even_with_the_same_name` reproduced it before the fix: tenant B's list contained tenant A's request.

**Cause.** `SupportAccessService.ListAsync` read the staff member's and the Owner's names with sub-queries `db.Set<User>().IgnoreQueryFilters()` inside the projection of `db.Set<SupportAccessGrant>()`. In EF Core `IgnoreQueryFilters()` applies to the **whole query**, wherever it is written, so the tenant filter of the root (`TenantId == signed-in tenant`) was switched off too. The list returned the newest 100 requests of **all** tenants, each labelled with the viewer's own tenant name.

**What a tenant could do with it.** Read only: the staff member's name, the reason, the request time, the status, the window times and the deciding Owner's name of other tenants' requests (no business data). It could **not** act on them: approve, refuse and end looked the request up with `MasterDataSupport.FindAsync`, a separate query that kept the tenant filter, so every decision on a foreign request answered 404 — which is exactly the "غير موجود" the owner got. Staff support sessions never depended on this list (the middleware checks `TenantId` + `RequestedBy` + window explicitly), so no access window could be opened by the wrong tenant.

**Fix (DT-213).**
- The list query names the signed-in tenant explicitly (`g.TenantId == ctx.TenantId`) and contains no ignored filters; staff/Owner names and refusal reasons are read in separate queries by id.
- Approve / refuse / end find the request with the tenant filter **and** an explicit tenant predicate (`Mine`), so a future change to the filter cannot reopen the hole.
- A foreign, unknown or malformed token answers the same specific 404 `support.requestNotFound` (translated: "This access request was not found in your subscription; it may have been withdrawn. Refresh the list.") — the answer does not reveal whether the request exists in another tenant (SEC-22).

**Tests.** `SupportAccessTests` (API): two tenants with the same name — B sees nothing, and B's approve / refuse / end of A's request answer `support.requestNotFound` and change nothing; request → approve → read-only access → ends at the end time; request → refuse; subscriber search. All pass, with the existing `PlatformTests` (support flow BR-014) and `SecurityTests` (generic isolation and endpoint coverage).

## Sweep for the same defect
A scan (19 Oct 2026, a small script over `src`, excluding migrations) of every statement whose first `db.Set<T>()` keeps its filters but which contains an `IgnoreQueryFilters` anywhere found two:

| Place | Effect | Result |
|---|---|---|
| `SupportAccessService.ListAsync` | Cross-tenant listing (SA-1) | Fixed |
| `InvoicePrintService.AsIssuedAsync` (invoice lines joined to `Trip` with ignored filters) | Filters off for the whole query, but the query is restricted to `InvoiceId == inv.Id` of an invoice already loaded under the company scope | Not a leak; **suggestion:** ignore only the soft-delete filter there (`IgnoreQueryFilters([SoftDelete])`) so the company filter stays as a second guard |

`NotificationService.HoldersAsync` ignores the tenant filter by name on both the root and the sub-query and names the company explicitly — correct. Console and worker queries start from `IgnoreQueryFilters()` on purpose (staff/system) and scope by explicit ids.

## Other checks
| # | Area | Finding | Result |
|---|---|---|---|
| 1 | Authorization of decisions | Approve / refuse / end need `core.tenant.manage` (Owner) and the tenant's own request | OK |
| 2 | Decided requests | A second decision is refused with `support.notRequested`; ending a window that is not open with `support.notOpen` (translated, specific) | OK |
| 3 | Read-only window | Unchanged: view permissions only, writes and exports refused, every request audited in the tenant's log, access ends at `EndsAt` (test moves the window into the past → 404) | OK |
| 4 | Notification of the staff member (DT-215) | Email only to the requesting staff account (`IsPlatformStaff`, not deleted); names, tenant and reason are HTML-encoded in the body; the decision is saved first and a failed email is logged, never undoing it | OK |
| 5 | Refusal reason | Read from the decision's own audit row (`Reject`, `_reason`), restricted to the tenant on the tenant side; no schema change | OK |
| 6 | Subscriber search (DT-214) | `GET /platform/support-access/tenants` is staff-only (tenants get 404, tested); at least 2 characters, at most 20 results; shows owner email, plan, sign-up date, status and, for a match through another user, that user's email — the same data the console's tenant list and global search already show staff | OK |
| 7 | Unambiguous requests | The request carries the chosen subscriber's token (unique); the dialog sends only for a subscriber picked from the results and shown again; typed text alone never sends | OK |
| 8 | Environment field (DT-216) | `/me` returns the host environment name only to platform staff; tenants never receive it | OK |

## Residual points (for the owner)
- **Data already seen in development.** The development database's only request (tenant 2, reason "test") was visible to tenant 3. It is test data on this PC; no other tenant data was exposed through this defect.
- **Console list rows** still name the subscriber only by name; with two same-named subscribers a row is told apart by its reason and time. Showing the owner email there too would need one more field in the list (not built — outside this round). **Done 19 Oct 2026 (DT-219): the list shows "name · owner email".**
- **Staff are told by email only**: the provider console has no notification bell. The console page itself shows the decision as soon as it is opened.

## Hardening after SA-1 (owner request 19 Oct 2026, DT-217 … DT-219)

### Build-time guard (DT-217)
`tests/DirectService.Transportation.Application.Tests/TenantFilterGuardTests.cs` runs with the unit tests and reads every server source file under `src` (browser client and migrations excluded) as a Roslyn syntax tree. It fails when:
- `IgnoreQueryFilters()` lifts the tenant or company filter (no names, or "Tenant"/"Company", or names it cannot resolve), raw SQL is used (`SqlQuery`, `FromSql…`, `ExecuteSql…` — the filters never apply to it), or the data scope is changed (`DataScope.Set/Use`) outside the allow-list;
- a filter is lifted **inside** a query whose own root keeps its filters (the SA-1 pattern), whatever the allow-list says;
- an allow-list entry is stale or has no justification.

Lifting only the soft-delete filter (`IgnoreQueryFilters([SoftDelete])`) is allowed anywhere. A self-test checks each rule on sample code.

**Code changed to pass the guard (no change for the tenant's own data):** 17 look-ups in tenant services that lifted every filter only to see soft-deleted rows now lift the soft-delete filter alone, so the tenant and company filters stay as a second guard — AP/AR parties (`ApArDocumentService`, `ApArPaymentService` ×2, `ApArPrintService`, `ApArReturnService`), `CashSupport` ×2, `VoucherPrintService`, `EInvoiceDocumentBuilder` ×2, `InvoicePrintService` ×3 (incl. `AsIssuedAsync`, DT-219), `TripService` ×3, `MonthlyRateService.HistoryAsync`; `IssuedNames` lifts nothing (the audit log has no soft-delete and the query already names the company). Two nested lifts found by the guard in `SessionService` (`ValidateAsync`, `EndExpiredAsync`: a `Tenant` sub-query lifted every filter of the session query) are made explicit on the outer query — the same behaviour, now visible: the session is found by its unique token hash before a tenant is known.

**Allow-list (entry → justification):**

| Entry | Why every query there is safe |
|---|---|
| `ConsoleService` | Provider console, platform staff only; every tenant query names the tenant id or token explicitly. |
| `StaffService` | Platform staff accounts (no tenant): staff rows by IsPlatformStaff, scope set to the acting staff member. |
| `StaffAccountService` | Operations command creating a staff account: system scope, no tenant data. |
| `ProviderSetupService` | First provider set-up (operations): staff rows by IsPlatformStaff under the system account. |
| `TenantLifecycleService` | Daily lifecycle job (system account): every query by the explicit tenant id being processed. |
| `EInvoiceWorker` | Background job: queued submissions of all companies, each processed with the scope set to its own tenant/company. |
| `PendingPostingWorker` | Background job: open postings of all companies, each retried with the scope set to its own tenant/company. |
| `LockExpiryWorker` | Background job: expired record locks, each released with the scope set to the lock's own tenant/company. |
| `ExportWorker` | Background exports (data export): each job runs with the scope of the tenant/company/user that queued it. |
| `RequestContextMiddleware` | Sets the data scope from the validated session (the only place a request gets its tenant). |
| `CompanyContextMiddleware` | Sets the company scope after checking the user's role (or an approved support window by explicit tenant/staff ids). |
| `SessionService` | Sessions by unique token hash or the user's own id, before a tenant is known; scope set to the session's own user. |
| `AuthService` | Sign-in by unique email/username before a tenant is known; scope set to that user's own tenant. |
| `AccountService` | Sign-up, email verification, password reset by unique single-use tokens; scope set to that user's own tenant. |
| `InvitationService` | Invitation acceptance by unique single-use link token; scope set to the invitation's own tenant. |
| `TwoFactorSettingsService` (ConfirmChangeAsync, DisableAsync, NewRecoveryCodesAsync) | The signed-in user's own 2FA settings; scope set to that user's own tenant. |
| `UserStore` | ASP.NET Identity store: users by id/unique name and their own recovery codes (users of all tenants share one table). |
| `ReplaySafeAuthenticatorTokenProvider` | Raw SQL claiming the signed-in user's own last code step by user id (DT-212). |
| `AccessService` (GrantsAsync) | The signed-in user's own company grants (by user id) across the user's tenant, before a company is chosen. |
| `TenantPasswordValidator` | The password policy of the user's own tenant (explicit tenant id), also before sign-in. |
| `MeService` (GetAsync, RevokeSessionAsync, SessionsAsync, UpdatePreferencesAsync) | The signed-in user's own profile, preferences and sessions by the user's id (staff have no tenant). |
| `LiveHub` (CompanyIdAsync) | Realtime connection: the signed-in user by own public id; the company is then checked against the user's grants. |
| `TenantService` (DemoCompanyIdsAsync) | Lifts only the company filter to find the signed-in tenant's demo company; the tenant filter stays. |
| `DemoCompanyService` | Lifts only the company filter of the signed-in tenant; scope set to the new demo company of that tenant. |
| `CompanyProvisioningService` | Sets the scope to the company just created in the signed-in tenant while provisioning it. |
| `PersonalDataService` (AnonymiseUserAsync) | Invitations by explicit TenantId, sessions by the id of a user of this tenant (checked by GrantGuard). |
| `PlanLimits` (ActiveUsersAsync, CountTripsAsync, EnsureTripsAsync, EnsureUserAsync, UsageAsync) | Plan usage and limits of one tenant: every query by the explicit tenant id passed in. |
| `BillingService` (ApplyPaymentAsync, SuspendedByStaffAsync) | Subscription invoices of one tenant: queries by the invoice's explicit tenant id. |
| `SubscriptionService` (WebhookAsync) | Signed payment webhook (no session): the invoice by gateway reference, scope set to that invoice's tenant. |
| `PlatformNumbers` | Raw SQL: the platform-wide subscription invoice number sequence (provider data, no tenant rows). |
| `NotificationService` (HoldersAsync, NotifyAsync, PushAsync, ReportSetupErrorAsync, ClearSetupErrorAsync) | Notification holders/recipients by explicit company or user ids (users of the company; staff have no tenant). |
| `RecordLockService` (AcquireAsync, HolderNameAsync) | Record locks: scope set to the lock's own tenant/company; the holder's name by user id. |
| `AuditDescriber` (LoadAsync, PrepareAsync) | Names of the users found in this tenant's own audit rows (support staff have no tenant): by those ids only. |
| `AuditLogService` (FacetsAsync) | Users who have a row in this company's log: explicit CompanyId condition, names only. |
| `RateHistoryLog` (ReadAsync) | Names of the users who changed this company's rates (ids from the company's own rows), names only. |
| `SupportAccessService` (ListAsync, RefusalReasonsAsync, TellStaffAsync) | Names of staff/Owners by ids from this tenant's own grants; audit rows by explicit TenantId (DT-213). |
| `MasterDataSupport` (SaveCreateAsync) | Only whether a client id (random GUID) is already taken anywhere, to refuse its reuse; returns no data. |
| `NumberingService` | Raw SQL row lock on the current company's own sequence row (explicit CompanyId). |
| `EInvoiceStore` | Raw SQL locks/claims of e-invoice rows by explicit document ids already found in the company scope. |
| `MonthlyRateLock` | Raw SQL lock of the current company's monthly rate rows (explicit CompanyId). |

Limitation: the guard reads syntax, so a lift hidden in a query assembled from variables is not seen as nested; behaviour is covered by the sweep below.

### Cross-tenant sweep (DT-218)
`tests/DirectService.Transportation.Api.Tests/TenantIsolationSweepTests.cs`: two tenants **with the same name**, two companies each (their own company and the demo company), and in each a record of every kind a route can name — customers, partners, drivers, sites, materials, ownership types, tariffs, monthly rates, trips, an approved tax invoice (with its e-invoice submission), credit note, receipt, advance, settlement, partner payment, AP/AR party, bill, payment, return, receivable (with its e-invoice submission), cost centre, bank account, manual entry, draft, template, budget, pending posting, saved report and dashboard pin, custom field, role, invitation, record lock, background export, support request (and its notification) and an unpaid subscription invoice. Then, from the route table:
- **reads** (lists, details, reports incl. every standard report, exports, search, dashboard, notifications, tenant pages, profile) are called as tenant A in both of A's companies and scanned — text, and the parts of zip/xlsx files — for the URL token of **any** row of tenant B and for B's markers (B's names, B's Owner email);
- the same reads again with each **query filter that names a record** (customer, driver, supplier, account, …) set to B's record;
- every route that **names a record** (read or action) is called as A with B's real token and must answer 404;
- provider-console routes must answer 404 to A;
- a route family or route parameter the sweep cannot classify fails the test, and every parameter → entity mapping must open A's own record at least once (a wrong mapping would make the 404s meaningless).

It was checked against the original defect: with SA-1 put back, the sweep fails with `LEAK GET /api/v1/tenant/support-access`.

**Result:** 498 operations from the route table — 146 company reads and 10 tenant/profile reads scanned (534 own reads in both companies, all scanned, none binary-only), 75 record reads and 117 record actions called with tenant B's real token (192 calls, all 404), 866 reads with a query filter naming B's record, 29 provider-console routes (404 to tenants), 105 own writes without a record, 16 anonymous/webhook. No leak; every record route answered 404 to the other tenant.

**Findings of the sweep:** no leak of tenant data. Two points:
- `GET …/accounting/budgets/{budget}/import/template` answered 200 for another tenant's budget token: the handler ignored the token and built the template from the caller's own chart (no data of the other tenant). **Fixed:** the budget is now looked up first (404).
- `DELETE …/locks/{lockId}` answers 204 for a lock the caller cannot see (idempotent release, e.g. when a page closes): the look-up keeps the tenant/company filter, so another tenant's lock is never touched — the sweep checks that B's locks are still there. Accepted as designed.

Accepted as revealing nothing: a 400 validation answer given before the record is looked up.

### The two items left from SA-1 (DT-219)
- The console's support-access list shows each subscriber as "name · owner email".
- `InvoicePrintService.AsIssuedAsync` lifts only the soft-delete filter (named filter); the company filter stays as a second guard.
