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
- **Console list rows** still name the subscriber only by name; with two same-named subscribers a row is told apart by its reason and time. Showing the owner email there too would need one more field in the list (not built — outside this round).
- **Staff are told by email only**: the provider console has no notification bell. The console page itself shows the decision as soon as it is opened.
