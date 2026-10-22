# Security review — system email via Microsoft 365 (22 Oct 2026)

Scope: DT-250…DT-253 — the Microsoft Graph sender (`Infrastructure/Email/EmailTransports.cs`), the MIME builder, the outbox writer and worker (`Infrastructure/Email/EmailOutbox.cs`, `Web/Workers/EmailOutboxWorker.cs`), the console service and endpoints (`Application/Platform/SystemEmailService.cs`, `/api/v1/platform/system-email/*`), the console screen, tables `core.SystemEmailSettings` and `core.EmailOutbox` (revision 6 §B10), the shared email layout. Method: code reading against the threats below, OWASP ASVS 4 (V2 authentication, V4 access control, V5 validation/encoding, V6 stored cryptography, V7 logging, V13 API), with a test for each control. Reviewer's checklist: secure-code-guardian (threat model → controls → validation → documentation).

Result: **no open high or medium finding.** Two issues found during the review were fixed before the commit (F1, F2). Residual risks and owner actions are listed at the end.

## 1. Secret storage and exposure

| Check | Result |
|---|---|
| Where the client secret is stored | `core.SystemEmailSettings.ClientSecretEncrypted`, ASP.NET Core Data Protection, own purpose `DST.SystemEmail.ClientSecret.v1` (the key ring is the hosting setting already used for the e-invoicing credentials and 2FA secrets). Clear text only in memory for the token request. |
| Returned by the API? | Never. `SystemEmailSettingsView` has `HasSecret`, `SecretSavedAt`, `SecretExpiresOn` only. Test: the PUT and GET bodies do not contain the secret (`SystemEmailTests.Staff_only_fresh_code_on_save_…`). |
| Shown again on screen? | No. The field is `type=password`, `autocomplete=new-password`, empty after saving; "Saved — leave empty to keep it" with saved/expiry dates (`SystemEmailPageTests`). |
| Audit log | The interceptor masks `ClientSecretEncrypted` (`"***"`), the vocabulary marks it `Masked`; test reads the audit rows. |
| Application logs | No log line carries the secret: HttpClient logging never logs bodies (the token request body holds it), `Authorization` is redacted on the Graph client, Azure.Identity's event source is not connected to the logger, error details keep only the `AADSTS` code and Microsoft's first sentence (test: no secret in any refusal's detail). |
| Errors returned to the console | Kind + Microsoft's message (e.g. `AADSTS7000215: Invalid client secret provided.`) — Microsoft never echoes the secret. |
| Lost key ring | The secret cannot be read → the email fails with "unknown: cannot be read"; staff enter the secret again. Documented in the guide. |
| Secret in source / config | None. Tests use a fake Graph and fake values only. |

## 2. Who can change the settings

- Every `/platform/*` route is behind `PlatformStaffFilter`: anyone else gets **404** (test: a tenant Owner gets 404 for GET and PUT).
- **Every save needs a fresh authenticator code** (`AuthService.VerifyFreshCodeAsync`): the code is used once (DT-212 replay protection), recovery codes are not accepted here, wrong codes count towards the account lockout like sign-in failures (DT-204). Tests: no code → 400 on `code`; wrong code → 400, nothing saved.
- **A change of mode, mailbox, tenant ID, client ID or secret never takes effect without a successful test email**: it is saved as Pending, the Active row keeps sending; only `TestAsync` with a successful send activates it (old row → Retired, one-Active unique index). A stolen session without the authenticator cannot save; a save alone cannot redirect the platform's mail.
- Every change is audited with the acting staff member and IP: Create / Update / Cancel (pending dropped) / Activate / Deactivate / EmailTest of `CORE.SystemEmailSetting`, Resend of `CORE.EmailOutbox`.
- Optimistic concurrency (`RowVersion`) on saves.

## 3. Link tokens never logged

- Every email is written to `core.EmailOutbox`; the message (subject, bodies, links) is only in `PayloadEncrypted` (Data Protection, purpose `DST.SystemEmail.Outbox.v1`), **wiped when sent**, and wiped on failure when the email holds a single-use link (`HasSecretLink`: verification, password reset, invitations, email change). A database check (`CK_EmailOutbox_PayloadEncrypted`) enforces it.
- The log columns are recipient, type, language, times, status, attempts, error kind and Microsoft's reason — no subject, no body, no URL. Tests: the token is not in the stored bytes nor in the log API; the payload is NULL after sending and after the last failure.
- Resend of a failed single-use email asks the flow for a **new** link (password reset, email verification, staff invitation — the old link stops working, the flow's own checks apply); a company invitation or an email-change confirmation cannot be remade by staff — the screen says who must ask again. Notices without a secret are resent as they were.
- Retention: recipient addresses older than 180 days (`Email:LogRetentionDays`) are replaced by "(removed)"; a deleted tenant's addresses are removed with its data.

## 4. Header injection

- Recipient, sender mailbox and reply-to must be one plain address (`SystemEmailService.IsAddress`: no control characters, no spaces, no display name, parsed address equals the input). Validated when saved and again in the MIME builder (`MimeMessageBuilder.Address` throws → the email fails as "invalid address", nothing is sent).
- Sender name and subject (which can contain a tenant or company name typed by users) are RFC 2047 encoded words with **every control character removed** before encoding, folded at 75 characters; the sender name is also refused with control characters when saved (test: `DS\r\nBcc:` → 400 on `displayName`).
- HTML bodies: every user-typed value is HTML-encoded (existing rule, test with `<script>`).
- Tests: `GraphEmailTransportTests.No_header_can_be_injected` (CRLF in recipient, subject, sender name).

## 5. Sending as other mailboxes

- The application sends only as the **one** mailbox in the settings (`/users/{mailbox}/sendMail`), changed only by staff with a fresh code and a successful test.
- In Microsoft 365 the app is limited to that mailbox by **Exchange Online RBAC for Applications** ("Application Mail.Send" with a management scope) — guide step 3, including removing any tenant-wide Entra `Mail.Send` grant (Entra and Exchange grants add up) and checking with `Test-ServicePrincipalAuthorization`.
- Defence in depth: the token's `roles` claim is read on each send; if it contains the tenant-wide `Mail.Send`, the test email shows a warning ("the app can send as any mailbox — remove it"). Test: `A_token_with_the_tenant_wide_permission_is_reported_as_a_warning`.

## 6. Throttling and abuse

| Threat | Control |
|---|---|
| Someone requests password resets / verification emails for another person's address again and again | Existing: the anonymous `/auth/*` endpoints (`password-reset/request`, `resend-verification`) are limited to 20 requests a minute per IP (`RateLimits.Auth`), answer the same whatever the address, and send only to an active, verified account. **New:** at most 5 emails of the same kind to the same address an hour (`Email:PerRecipientPerHour`) — further ones are logged as **Suppressed**, not sent (test: 7 requests → 5 queued, 2 suppressed). This also covers server-rendered account pages and any future flow, since it sits in the outbox. |
| Using the platform to spam (exhausting Exchange limits, reputation) | At most 30 emails a minute platform-wide (`Email:PerMinute`, counted in the database so several hosts share it); Graph `429/503` Retry-After pauses the whole queue. |
| A staff member sending test emails in bulk | **F1 (fixed):** test emails go out at once, outside the queue's per-address limit — now at most 10 test emails in 10 minutes for the console (429). |
| Retrying forever / duplicates | 6 attempts with back-off (1 min … 12 h); claimed rows are locked (`UPDLOCK, READPAST`) so two hosts never send the same row; a send interrupted mid-way is marked Failed ("may have been delivered"), **never retried automatically** (at-most-once); test: `The_outbox_retries_honours_retry_after_and_never_sends_an_email_twice`. |
| Alert floods | Staff failure alerts at most one email in 6 hours; secret-expiry warnings once at 30 and once at 7 days. |

## 7. Tenant isolation of the log

- `core.EmailOutbox` and `core.SystemEmailSettings` are platform tables, read only through `/platform/*` (staff only, 404 for everyone else). No tenant screen or API reads them.
- `TenantId` is stored for each email caused by a tenant user (masking on tenant deletion; a future tenant-facing view could filter by it). The tenant-filter guard test lists the new places where a filter is lifted (staff accounts, the claim query) with their reasons.
- The log shows no content; staff see recipient addresses, which they already see in the console's tenant and staff screens.

## 8. Other checks

- Transport security: HTTPS only to `login.microsoftonline.com` and `graph.microsoft.com` (configurable for other clouds via `Email:AuthorityHost` / `Email:GraphBaseUrl`, not via the API).
- No SMTP, no mailbox password, no basic auth anywhere.
- Emails are marked automatic (`Auto-Submitted`, `X-Auto-Response-Suppress`) so out-of-office replies do not loop.
- **F2 (fixed):** the audit interceptor read `IsDeleted` of every modified row; the outbox (not soft-deletable) made it throw — guarded so only soft-deletable rows are checked.
- Dependencies: `Azure.Identity` 1.17.1 (already present through Microsoft.Data.SqlClient); NuGet audit on, warnings as errors: build clean.

## Residual risks and owner actions

1. **Key ring backup** (hosting, DEF-01): without the Data Protection keys the secret and any queued email cannot be read.
2. The secret's expiry date is typed by staff: if it is wrong the warning comes at the wrong time (the test email still fails clearly when the secret has expired).
3. A send interrupted mid-way is not repeated automatically — staff resend it from the log if it did not arrive.
4. Penetration test before launch (SEC-15 / REL-05) should include the console's settings and test endpoints.
