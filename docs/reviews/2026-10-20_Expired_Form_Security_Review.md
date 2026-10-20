# Security review — expired account forms and the key ring (DT-221, 20 Oct 2026)

Scope: `Web/Pipeline/ExpiredFormMiddleware.cs`, `Web/Security/DataProtectionSetup.cs`, `Components/Account/AccountPage.cs` / `AuthCard.razor`
and the page refills, `Web.Client/Services/Api.cs` (second antiforgery refusal), `ShellState.PageExpired`, `Layout/AppShell.razor`.

## What was reported
After entering email and password and pressing Enter the browser showed the framework text "A valid antiforgery token was not provided
with the request…". That text is written by the Razor components endpoint (Development; in Production a blank 400) when the form's token
does not validate.

## Cause
- **Key ring:** persisted (one key in `src/DirectService.Transportation.Web/.local/keys` since 8 Oct 2026, DPAPI-protected, application name
  `DirectService.Transportation`), so an app restart does not invalidate tokens when the host is started from the project folder. But the
  path was relative to the **process working directory**: a host started from another folder (published app, service, other tooling) would
  create a new key ring and sign everybody out. Now anchored to the content root.
- **The likely cause of the report:** an antiforgery token is bound to the signed-in user. A sign-in page opened while anonymous becomes
  invalid once the same browser signs in in another tab (or a form opened while signed in, after signing out), and a token is also refused
  when the browser has lost the antiforgery cookie (session cookies cleared, private window closed). Reproduced in
  `ExpiredFormTests.Signing_in_in_another_tab_makes_the_open_form_expire_politely`.

## Design and checks

| # | Point | Finding |
|---|---|---|
| 1 | Is antiforgery still enforced? | Yes. `UseAntiforgery` validates as before; the middleware runs after it and only for Razor component endpoints (`ComponentTypeMetadata`) whose validation **failed**. It changes the method to GET, so the page renders as a plain GET: no form binding, no `OnSubmit` handler. A refused post can never act. Test: cross-site post to forgot-password sends no email. API endpoints are untouched (their `AntiforgeryFilter` answers 400 `auth.antiforgery`). |
| 2 | Reading a form whose token failed | The framework blocks it until the result has been looked at; the middleware sets a "checked" feature only around one `ReadFormAsync` for the kept fields, then restores the refused one. Nothing downstream reads the form (GET). |
| 3 | Reflected values (XSS, login CSRF) | Only fields named `Email`, `FullName`, `OrganisationName`, at most 256 characters, rendered by Razor (HTML-encoded). Never passwords, codes, recovery codes, the provider-setup session or tokens (those in the address stay in the address). Kept only when `Sec-Fetch-Site` is `same-origin`/`none` or absent: a cross-site post keeps nothing, so another site cannot prefill a victim's form. Old browsers without fetch metadata: an attacker could at most prefill an email on the sign-in page — no sign-in without the password (accepted). |
| 4 | Caching | The re-rendered page issues a new token, so the antiforgery system marks it `no-cache, no-store`. |
| 5 | Rate limiting / lockout | Unchanged: the page keeps `[EnableRateLimiting(Auth)]`; a refused post does not reach the sign-in service, so it neither counts as a failed sign-in nor bypasses the lockout. |
| 6 | `?expired=1` | Added only to the redirect to sign-in from a later step's expired form; it carries no data and only shows the banner on a GET. Forging it shows a harmless message. |
| 7 | Logging | Information line with the path only; no form values. |
| 8 | Key ring at rest | Windows: DPAPI (as before). Any system: `DataProtection:CertificatePath` + `CertificatePassword` (secret) encrypt the keys with a certificate and decrypt with the same one (`UnprotectKeysWithAnyCertificate`, tested across a "restart"). Production on Linux/containers **refuses to start** with unprotected keys unless `DataProtection:AllowUnprotectedKeys=true` states that the volume is encrypted by the host. |
| 9 | Key folder | Content-root relative; container `/keys` volume owned by the `app` user; must be shared by all instances and backed up with the database (it also decrypts the stored JoFotara credentials). |
| 10 | In-app changes | The client retries once with a fresh token (as before); a second refusal now shows "This page expired… Reload" with a button (no automatic reload loop, no raw error). |

No findings open. Residual: item 3 (old browsers) accepted.

## Tests
`Api.Tests/ExpiredFormTests` (11 account pages × missing and outdated token; email kept, password not; Arabic text; next try with the fresh
token works; cross-site post keeps nothing and does nothing; sign-in in another tab; restart keeps the session and an open form valid, with a
control host on a new key ring that signs the user out), `Api.Tests/DataProtectionSetupTests` (content root, certificate at rest and read
back, application name independent of the install folder, Production refusal on Linux — runs in CI), `UI.Tests/PageExpiredTests`.
