# Direct Service Transportation — Setup, run and operations

_Step 9, 11 Oct 2026. For developers and the people who will host the system. Everything here was run on the owner's development PC (Windows 11, SQL Server 2025 Developer)._

## 1. What you need

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.0.401 (pinned in `global.json`, roll-forward latestFeature) | build, run, tests |
| SQL Server | 2019 or later (developed on 2025 Developer), Windows or SQL authentication | the Master Database |
| `dotnet-ef` | 10.0.13 (`dotnet tool install -g dotnet-ef --version 10.0.13`; update an older one with `dotnet tool update -g dotnet-ef --version 10.0.13`) | migrations, idempotent script |
| `sqlcmd` | any recent | database scripts (always pass `-I`: filtered indexes need QUOTED_IDENTIFIER ON) |
| Chrome or Edge | current | PDF printing (headless Chromium, Q-08) and the E2E tests |
| Node.js | 24 LTS (npm 11) | E2E tests (`tests/e2e`), OpenAPI lint |

## 2. Solution layout

| Project | Role |
|---|---|
| `src/DirectService.Transportation.Domain` | entities (66 tables of the design, revision 5) |
| `src/DirectService.Transportation.Contracts` | request/response records shared by the API and the UI (identifiers are URL tokens) |
| `src/DirectService.Transportation.Application` | business rules and services per module (B1–B12) |
| `src/DirectService.Transportation.Infrastructure` | EF Core (`MasterDbContext`, configurations, migrations, interceptors), e-mail, PDF engine, e-invoicing connectors, payment gateway |
| `src/DirectService.Transportation.Web` | ASP.NET Core host: minimal-API endpoints `/api/v1`, account pages (static SSR), workers, SignalR hub, hosts the UI |
| `src/DirectService.Transportation.Web.Client` | Blazor WebAssembly UI: design system (`Ui/`, `Layout/`), screens (`Pages/`), AR/EN text (`Localization/`) |
| `tests/*.Tests` | Domain, Application (unit), Infrastructure (database rules), Api (integration, isolation, security), UI (bUnit) |
| `tests/e2e` | Playwright end-to-end flows (PRD Section 9) and the WCAG 2.1 AA check, Arabic and English |
| `database/` | `00_CreateDatabase.sql`, `MasterDatabase_Idempotent.sql`, `90_ApplicationUser.sql` |
| `docs/` | step documents (requirements, architecture, database, block reports, design system, this guide), `docs/api/openapi-v1.json` |

## 3. Database

1. Create the database with the approved collation and read-committed snapshot (Q-07, DT-11):
   `sqlcmd -S <server> -E -I -i database/00_CreateDatabase.sql`
2. Apply the schema and reference data (permissions, preset role keys, TRIAL plan, posting keys…):
   `sqlcmd -S <server> -E -I -d DirectServiceMaster -i database/MasterDatabase_Idempotent.sql`
   (or, for development: `dotnet ef database update --project src/DirectService.Transportation.Infrastructure --startup-project src/DirectService.Transportation.Infrastructure`).
3. Production: create the application login and run `database/90_ApplicationUser.sql` (read/write, `DENY DELETE` on business schemas, `DENY UPDATE` on the audit, maintenance and rate-history logs, no DDL). The host connects as that user, never as an administrator.
4. **Version check at start-up (DT-209):** the host compares the database with the migrations of its version. In Development it applies any missing migration itself and logs which ones; in every other environment it refuses to start and names the missing migrations — apply step 2's script after every update, then start the application.
5. Regenerate the script after a model change: `dotnet ef migrations script --idempotent --project src/DirectService.Transportation.Infrastructure --startup-project src/DirectService.Transportation.Infrastructure --output database/MasterDatabase_Idempotent.sql`. Do not re-run `tools/design/gen_efcore.py` (it would overwrite hand-maintained entities).

## 4. Configuration

Settings come from `appsettings.json`, then `appsettings.<Environment>.json`, then user secrets (Development) and environment variables (`Section__Key`). **Secrets never go into the files** (`.gitignore` excludes local secret files; `UrlTokens:Key` is empty on purpose).

| Key | Meaning | Default / note |
|---|---|---|
| `ConnectionStrings:MasterDb` | the Master Database | required |
| `UrlTokens:Key` | **32-byte** (256-bit) base64 key for the hashed public ids in URLs — 44 characters ending in `=` | **required secret**, checked when the host starts (see below). Changing it breaks every saved link |
| `App:PublicBaseUrl` | absolute address used in e-mails | e.g. `https://app.example.com` |
| `App:AbsoluteSessionHours` / `App:PlatformStaffIdleMinutes` | session lifetime 12 h (Q-04) / staff idle timeout | 12 / 30 |
| `App:TrialDays`, `App:InvitationDays`, `App:TermsVersion` | trial length, invitation validity, terms version recorded at sign-up | 14 / 7 / draft-1 |
| `DataProtection:KeysPath` | key ring (cookies, antiforgery, encrypted e-invoicing credentials) | `.local/keys` under the content root; **shared, persistent storage in production**, protected at rest (see "The Data Protection key ring" below) |
| `Email:DropFolder`, `Email:From` | development e-mail: messages are written as `.eml` files | provider to choose (DEF-02) |
| `Files:Root` | stored files (background exports) | `App_Data/files` |
| `Printing:ChromiumPath` | Chrome/Edge executable for PDFs | found automatically when installed |
| `RateLimits:AuthPerMinutePerIp`, `RateLimits:ApiPerMinutePerUser` | brute-force and API limits | 20 / 600 |
| `Jobs:Enabled` | background workers (pending postings, e-invoicing queue, lock expiry, exports, tenant lifecycle) | true |
| `Platform:*` | subscription invoices: `TaxRate` 16, `Currency` JOD, `SellerName`, `SellerTaxNumber`, `GraceDays` 7, `SuspendedDaysBeforeCancel` 30, `RetentionDays` 90 | provider's own data |
| `Platform:SetupCode` | setup code of the **first-run provider setup** (`/provider/setup`) while no platform staff account exists | empty → a one-time code is generated at each start and written to the log; **secret** when set (environment variable or user secrets, never the files) |
| `Billing:WebhookSecret`, `Billing:PaymentPageUrl` | payment gateway (development fake; no secret → every webhook refused) | provider to choose (DEF-02) |
| `EInvoicing:JoFotara:TestUrl` / `ProductionUrl` / `TimeoutSeconds` | national e-invoicing endpoints | **to verify with the owner's test credentials (R-01)** |
| `EInvoicing:AllowSimulator` | offers the e-invoicing simulator connector | development/tests only, never in production |

### The URL token key (`UrlTokens:Key`)

The key must be exactly **32 random bytes**, written in base64 (44 characters). A 16-byte key, a key that is not base64 or a missing key stops the host at start-up with a message that says which of the three it is and how to set it. Create one key per environment and keep it:

- PowerShell (Windows PowerShell 5.1 or PowerShell 7):
  `$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)`
- Git Bash / Linux: `openssl rand -base64 32`

Store it outside the files:

- Development (user secrets, kept in your Windows profile):
  `dotnet user-secrets set UrlTokens:Key "<key>" --project src/DirectService.Transportation.Web`
- Production: the environment variable `UrlTokens__Key` (two underscores) or the hosting provider's secret store.

Do not put it in `appsettings.json` (it would be committed with the code). Changing the key later breaks every saved or e-mailed link to a record.

### The Data Protection key ring (`DataProtection:*`, DT-221)

The key ring signs the sign-in cookies and the antiforgery tokens of every form and encrypts the stored e-invoicing credentials. If it is lost or regenerated, every user is signed out and every open form is refused after a restart or update. It is set up in `Web/Security/DataProtectionSetup.cs`:

- **Stable application name** `DirectService.Transportation` (never the default, which depends on the install folder).
- **Persisted keys** in `DataProtection:KeysPath` (default `.local/keys`). A relative path is taken under the **content root** (the project folder in development, the application folder when published), not the process working directory — so `dotnet run`, Visual Studio, a Windows service and a container all use the same folder. Development: `src/DirectService.Transportation.Web/.local/keys` (one key, in use since 8 Oct 2026, excluded from git). Container: `/keys` (`Dockerfile`, a named volume). Several instances must share the folder.
- **Protected at rest:** `DataProtection:CertificatePath` (+ `DataProtection:CertificatePassword`, a secret) encrypts the keys with that certificate (.pfx) on any system; otherwise DPAPI of the account the host runs as (Windows — run the service under one fixed account). In **Production on Linux/containers** the host refuses to start without a certificate, unless `DataProtection:AllowUnprotectedKeys=true` is set because the key volume itself is encrypted by the hosting provider. Back up the key folder (and the certificate) with the database: without them the stored e-invoicing credentials cannot be decrypted.
- Keys rotate automatically every 90 days; old keys stay in the folder, so cookies and forms signed with them stay valid until they expire. Never delete the folder on an update.
- A form posted with an outdated token (page opened before the user signed in or out in another tab, or before the browser session ended) is shown again with "This page expired. Please try again." and the email/username kept (never the password); a change inside the application refused twice for its token shows "This page expired… Reload" at the top of the page.

## 5. Run (development)

```bash
dotnet build DirectService.Transportation.slnx
```
```bash
dotnet run --project src/DirectService.Transportation.Web --launch-profile DirectService.Transportation.Web
```
Open `https://localhost:52233`. Health: `/health/live`, `/health/ready`. API reference (Development only): `/scalar`, OpenAPI document `/openapi/v1.json` (exported copy in `docs/api/openapi-v1.json`, linted with `npx @redocly/cli lint docs/api/openapi-v1.json`). Development e-mails (verification, invitations, password resets) are written to `.local/mail`.

The only launch profile is `DirectService.Transportation.Web`; any other profile name silently runs as Production without a connection string.

### Visual Studio stops on `NavigationException` when you sign in

Signing in, signing out and the two-step pages move you to another page with Blazor's `NavigationManager.NavigateTo`. On the server-rendered account pages Blazor does this by throwing a `Microsoft.AspNetCore.Components.NavigationException`, which the framework catches and turns into a normal redirect. It is expected and harmless, but when you run with the debugger (F5) Visual Studio pauses on it as a "first-chance exception". Either:

- press **Continue** (F5) — the redirect completes; or
- turn the break off: when Visual Studio pauses, clear **Break when this exception type is user-handled** in the exception box; or open **Debug → Windows → Exception Settings** (Ctrl+Alt+E), search for `NavigationException` (under Common Language Runtime Exceptions) and clear its check box; or
- run without the debugger: **Debug → Start Without Debugging** (**Ctrl+F5**) or `dotnet run` as above.

"Just My Code" (Tools → Options → Debugging → General) should stay on; with it off Visual Studio also stops inside framework code.

## 6. First platform staff account and staff invitations

There is no public sign-up for platform staff. The **first** staff account is created in the browser; every later one is invited from the provider console.

**First-run setup** (only while no platform staff account exists; afterwards `/provider/setup` answers 404 for good):

1. Get the one-time setup code — either way works:
   - **From the log (default).** When the host starts and finds no staff account, it generates a code such as `K7QPM-2XWRT-9HNAD` and writes it to the application log / console:
     `Provider first-run setup is open: no platform staff account exists. Open /provider/setup and enter this one-time setup code: K7QPM-2XWRT-9HNAD`.
     In development this is the `dotnet run` console (or the Visual Studio *Output → ASP.NET Core Web Server* window); on a server, the host's log (container logs, systemd journal, Azure log stream…). A new code is generated at every start until the first account exists. With several instances behind a load balancer each has its own code — use the configured code below instead.
   - **From configuration.** Set `Platform:SetupCode` before starting: environment variable `Platform__SetupCode=<your code>`, or in development `dotnet user-secrets set "Platform:SetupCode" "<your code>" --project src/DirectService.Transportation.Web`. The code must have at least 16 characters (spaces and dashes not counted; a shorter one is ignored with a warning and a code is generated instead). The host then uses that code and **never logs it** (the log only says the setup is open). Remove the setting once the first account exists.
2. Open `https://<host>/provider/setup`, enter the code (case, spaces and dashes do not matter). Five wrong codes from one address within 15 minutes block that address for 15 minutes (more than 100 wrong codes in an hour from all addresses block everyone for 15 minutes); the per-address sign-in rate limit also applies. Every attempt is recorded in the audit log (`CORE.ProviderSetup`) with the caller's address.
3. Enter the name, e-mail and password (at least 8 characters with letters and digits) within 20 minutes of the code; the account is created and the mandatory two-step set-up follows at once (authenticator app + 10 recovery codes). The code can never be used again.

**More staff:** provider console → **Platform staff** → *Invite staff* (name, e-mail). The invitee receives a single-use link valid for 48 hours, sets a password and then two-step verification before the first sign-in. Pending invitations can be resent (the old link stops working) or cancelled; staff can be deactivated (with a reason; their sessions end at once) and reactivated — never one's own account nor the last active staff member. All of it is audited.

**Recovery:** the `create-staff` operations command below still works (for example when every staff member has lost access).

## 7. Operations commands

| Command | What it does |
|---|---|
| `dotnet DirectService.Transportation.Web.dll create-staff <email> <full name>` (or `dotnet run --project src/DirectService.Transportation.Web -- create-staff …`) | **recovery only** (the normal ways are the first-run setup and invitations, section 6): creates a **platform staff** account (provider console, D-13; no tenant, no password) and e-mails a set-password link; the first sign-in sets up two-step verification. Running it again for an existing staff account sends a new link. Sign-up never creates staff (DT-131) |

## 8. Tests

| Suite | Command | Notes |
|---|---|---|
| All .NET tests | `dotnet test` | needs local SQL Server; Api/Infrastructure tests create `DirectServiceMaster_Tests` / `DirectServiceMaster_ApiTests` |
| One suite | `dotnet test --project tests/DirectService.Transportation.Api.Tests` | ~8 min; on a PC low on memory run `dotnet build-server shutdown` first (SQL memory grants) |
| Coverage | add `--coverage --coverage-output-format cobertura` | results in `TestResults/` |
| Same as CI | `dotnet build DirectService.Transportation.slnx -c Release`, then per project `dotnet test --project tests/DirectService.Transportation.<Name>.Tests -c Release --no-build --coverage --coverage-output-format cobertura --coverage-output <Name>.cobertura.xml` | every test project shares `tests/Directory.Build.props` (xUnit v3 on Microsoft.Testing.Platform + coverage extension), so the same arguments work for each, on Windows and Linux (DT-225) |
| UI component and page tests | `dotnet test --project tests/DirectService.Transportation.UI.Tests` | bUnit against a fake API |
| E2E (Playwright) | `cd tests/e2e && npm ci && npx playwright test` | builds nothing: run `dotnet build` first. Recreates `DirectServiceMaster_E2E`, creates the staff account, starts the host on port 52301 with its own mail folder (`.local/e2e/mail`), uses the installed Edge. Report: `TestResults/e2e-report` |
| Accessibility | `npx playwright test accessibility` (in `tests/e2e`) | axe-core WCAG 2.1 AA on every screen, AR and EN; findings in `TestResults/a11y-*.json` |
| Visual layout | `npx playwright test visual` (in `tests/e2e`) | every screen, dialog, side panel and menu, AR and EN, at 1280 and 1366 px wide, compact and comfortable density: fails on overlapping text (text over text, or a picture/field over text), text cut off by its box, and sideways page scroll (`support/layout.ts`). Screenshots of every combination in `TestResults/visual/<lang>/<width>-<density>/`, problems in `TestResults/visual/problems-*.json`; `VISUAL_SHOTS=0` skips the screenshots. `visual-checker.spec.ts` proves the checker catches the two layouts reported on 12 Oct 2026 and stays quiet on dialogs, scrolled lists, ellipses, unit adornments and sticky totals. ~45 min |

## 9. Hosting notes (the provider and residency are deferred, DEF-01)

- One stateless host image (ASP.NET Core) behind HTTPS (TLS 1.2+); the WebAssembly UI is served by the same host.
- Include Chrome/Chromium in the image for PDFs (or set `Printing:ChromiumPath`).
- Several instances need: the shared data-protection key ring, a shared file store for `Files:Root`, and a SignalR backplane (Redis or Azure SignalR) — the code is ready, the backplane is a hosting choice.
- SQL Server: encryption at rest (TDE), automated encrypted backups with point-in-time restore (NFR-05: RPO 15 min, RTO 4 h), quarterly restore drill (TST-07).
- Background jobs run in every instance; the tenant lifecycle job takes a platform-wide lock (`sp_getapplock`), the queues claim rows with `READPAST`.
- Before launch: e-invoicing certification against the JoFotara test environment (TST-04 / REL-04), payment gateway and e-mail provider (DEF-02), penetration test (SEC-15 / REL-05), legal check of anonymisation and audit retention (SEC-19, DT-134).
- Behind a TLS-terminating proxy or load balancer set `Hosting:BehindProxy=true` and list the proxies in `Hosting:KnownProxies` (addresses) and/or `Hosting:KnownNetworks` (CIDR ranges, e.g. the container network); the host refuses to start behind a proxy without them. The scheme and client address then come from `X-Forwarded-*`. In the container image set `Printing:NoSandbox=true` (done by the Dockerfile) because Chromium cannot use its own sandbox there.
- Monitoring (NFR-18): liveness `/health/live`, readiness `/health/ready` (database); structured logs from ASP.NET Core logging (no secrets or personal data are logged); request, connection, rate-limiter and SignalR metrics are published by the built-in .NET meters (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.Http.Connections`) — read them with `dotnet-counters` or connect an OpenTelemetry exporter when the hosting provider is chosen.
- Container: `Dockerfile` at the repository root (see its header for build/run); CI: `.github/workflows/ci.yml` on GitHub (ubuntu-24.04, SQL Server container: restore with NuGet audit, Release build, all five .NET test projects with coverage, coverage upload, OpenAPI lint; Chrome prints the PDFs without its own sandbox, `Printing__NoSandbox`). The E2E, accessibility and visual suites are not in CI: they need the Windows tools of the development PC (installed Edge, Windows `sqlcmd`, `taskkill`) and 30+ minutes; run them on that PC before a release. The container image has not been built yet (no container runtime on the development PC).
