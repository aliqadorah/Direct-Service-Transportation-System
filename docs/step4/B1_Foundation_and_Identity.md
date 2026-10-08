# Step 4 · Block B1 — Foundation & Identity: delivery and review

_Completed 8 Oct 2026. Working mode: no stop after the module (owner instruction); quality gates applied._

## What was built (backend only — no UI code before mockups, step 6)

| Area | Implementation | Requirements |
|---|---|---|
| Web host | ASP.NET Core 10 API host: `/api/v1`, problem details (ar/en), security headers, HTTPS/HSTS, rate limiting (auth per IP, API per user), health checks `/health/live` `/health/ready`, OpenAPI 3.1 (`docs/api/openapi-v1.json`, Redocly lint clean) + Scalar in development, SignalR hub `/hubs/live`, background-worker host | API-01…07, NFR-15/16/17/18, SEC-12/14 |
| Request context | Session → user/tenant/language; `{company}` route token → company of the user's tenant + role → permissions; data scope for EF Core filters | SEC-05/06, ADR-04 |
| Authorization | Policies `perm:<key>` (company) and `tenantperm:<key>` (tenant), member policy; every endpoint declares its key (test-enforced); write guard (suspended tenant, archived company, support session) | 3.3, BR-010, D-13, ADR-06 |
| URL tokens | AES-256 per entity type (HKDF), 22-char base64url, tampered → 404 | SEC-22, ADR-07 |
| Identity | Custom Identity store on `core.Users` / `core.UserRecoveryCodes` (no `AspNet*` tables); tenant password policy (rev. 5); lockout 5 → 15 min; email verification; password reset (single use, revokes sessions) | SEC-01…04, FR-175 |
| Mandatory 2FA | Password step → partial cookie → authenticator set-up (first sign-in, 10 hashed recovery codes) or code/recovery-code verification → session | FR-177, SEC-02 |
| Sessions | `core.UserSessions` row per sign-in (token hash), validated every request: revoked, 12 h absolute, tenant idle timeout, security stamp; sign out one / others / by admin | FR-176, SEC-03, Q-03/Q-04 |
| Sign-up | Tenant (Trial 14 d, TRIAL plan) + Owner + first company + 5 preset roles + terms acceptances + company reference data, one transaction | US-31, P-17, A-21, G-20 |
| Company provisioning | Built-in "Company" ownership type, 8 settings (6 TRN + 2 market formats), 12 number sequences (D-16), e-invoice settings (disabled), 2 invoice layouts | FR-161, 7.9.1, BR-037, FR-054 |
| Users & roles | List, assign/remove role, disable/enable, archive, unlock, password reset, 2FA reset, sign out; custom roles; BR-001/002/003; no privilege escalation (SR-01) | FR-173, FR-174, BR-001…003 |
| Invitations | 7-day single-use links, resend, cancel, accept as new or existing user, inviter notified | FR-172, ASM-08, A-18, FR-190 |
| Tenant & companies | Tenant security settings; create company within plan limit (only Active, non-demo count); archive/restore (last active kept) | 5.1, 5.3, D-08, D-09, Q-06 |
| Company settings | Profile + logo (PNG/JPEG ≤ 512 KB, content-checked), market, operations (FR-170 confirmation), numbering (BR-038a), migration complete (FR-171) | FR-168…171, SEC-13 |
| Numbering | `UPDLOCK, ROWLOCK` inside the document transaction; rollback returns the number | BR-038, TST-10 |
| Period guard | Closed period refusal service (used from B2 on) | BR-036, FR-133 |
| Record locks | Acquire / heartbeat / release; 90 s expiry worker; server-side guard for updates; live lock notifications | BR-039, FR-186, RT-01 |
| Email | Provider-neutral `IEmailSender`, development `.eml` drop folder; Arabic + English templates | INT-03, DEF-02 |

## Tests — 85 passing
| Project | Tests | Covers |
|---|---|---|
| Domain.Tests | 4 | Entity defaults = design defaults incl. rev. 5 |
| Application.Tests | 27 | Preset roles = PRD 3.5; 22 keys (D-01); number format & prefixes (D-16); password policy incl. Arabic letters/digits; digit normalisation; error catalogue AR/EN complete; email; Jordan phone/IBAN patterns |
| Infrastructure.Tests | 22 | Step 3 data rules + numbering concurrency (20 parallel issuers → 1…20, no gaps), rollback returns number, per-company sequences, seeded keys = code |
| Api.Tests | 32 | Sign-up data; verification; mandatory 2FA; lockout; recovery codes; sessions; reset; BR-001/002/003; roles; invitations; plan limit; archive; settings FR-169…171; locks; isolation on every company endpoint; tampered tokens; 403 without permission; antiforgery; language of problems; security headers; endpoint coverage (permission + summary) |

## Code review (code-reviewer) — summary
- **Intent:** the foundation every later module relies on — server-side context, identity with mandatory 2FA, tenant isolation, administration.
- **Positive:** isolation enforced in one place and tested generically on every endpoint; no internal ids in any response; all writes audited with reasons; services thin over EF with explicit transactions.
- **Fixed during review:** UserManager `Set*` persisting unsaved users (sign-up 500); partial-cookie stamp after authenticator key creation; RowVersion not advancing when only child rows change (role edits); missing `{company}` parameter, summaries, operation ids and error responses in OpenAPI.
- **Accepted (documented):** permission and session caches are per instance for up to 30 s (revocations on another instance take effect within 30 s; the session row itself is revoked immediately); `LiveHub` checks the session only when the connection opens.

## Security review (security-reviewer) — findings
| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-01 | **High** | Privilege escalation: an Administrator (all keys except tenant.manage) could assign itself the Owner role, create a role holding tenant.manage, invite an Owner, or disable/reset an Owner | **Fixed** — `GrantGuard`: a caller can only grant keys it holds; only tenant.manage holders may act on Owner-level users or roles with keys they lack; regression test `An_administrator_cannot_escalate_to_owner_level_SR01` |
| SR-02 | Medium | Rate limiter ran before authentication, so the API limit was per IP instead of per user | **Fixed** — moved after `UseAuthentication` |
| SR-03 | Low | CHECK/FK violations returned 500 | **Fixed** — 422 `validation.failed` (logged) |
| SR-04 | Low | Authenticator codes can be replayed within their 30-second window (ASP.NET Identity default) | Accepted for now; candidate hardening later (store last used time-step) — recorded as a Suggestion |
| SR-05 | Info | Dependency scan: no vulnerable packages; secret scan: none in the repository (URL-token key in user-secrets) | — |
| SR-06 | Info | Data Protection keys: local folder protected with DPAPI on Windows; production key store with hosting (DEF-01) | Deferred |

Automated checks run: `dotnet list package --vulnerable --include-transitive` (clean), repository secret scan (clean), Redocly lint of the OpenAPI document (valid, 0 warnings with `redocly.yaml`).
