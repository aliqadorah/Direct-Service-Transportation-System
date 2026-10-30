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

### After cloning: enable the commit guard (DT-328)

Run once in every new clone, from the repository folder:

```
git config core.hooksPath .githooks
```

`.githooks/pre-commit` then refuses any commit that contains `src/DirectService.Transportation.Web/appsettings.json` (your local copy holds the real `UrlTokens:Key` and connection string; keep secrets there or in user secrets, never in git), anything under `docs/feedback/`, or any other `appsettings*.json` that sets a key, secret, password or token to a non-empty value (it names the line numbers, never the value). Visual Studio, the git command line and other git tools all run it; Visual Studio shows the refusal text in the Git Changes window. Stage files by explicit path, never a whole folder, and check `git diff --cached --name-only` before committing. Check it is on with `git config --get core.hooksPath` (prints `.githooks`).

## 2. Solution layout

| Project | Role |
|---|---|
The platform restructuring (`docs/platform/00_START_HERE.md`) moves the code in stages; after Stage 2 (Shared + Platform split) the layout is below. Which project may reference which is design a.2 (`docs/platform/01_Restructuring_Design.md`), enforced by `tests/DirectService.Architecture.Tests`.

| Project | Role |
|---|---|
| `src/Shared/DirectService.SharedKernel` | kernel types every layer uses (`AuditableEntity`, scope interfaces, stored-name pins, `IAppDbContext`, `IRequestContext`, `AppException`, `Fail` / `Validator`), the shared service interfaces, the cfg.* entities, `AuditLog`, printing helpers with the embedded brand fonts |
| `src/Shared/DirectService.Shared.Contracts` | browser-safe DTOs of the shared engines (paging, reports, imports, fields); references nothing |
| `src/Shared/DirectService.Shared.Infrastructure` | the composed `MasterDbContext` (each module adds its model through an `IModelContributor`), query filters, the audit and live-change interceptors, `MasterDatabase.UseMasterDatabase` (SQL Server, migrations assembly, history table), the shared engines (numbering, record locks, URL tokens, audit describer, report builder, saved reports, exports, imports, fields, search, spreadsheets, PDF / QR) |
| `src/Shared/DirectService.Shared.UI` | design system (`Ui/`), shell, client services and API client, `Loc` and common text, the shared screens; static files in `wwwroot` (css, js, img, brand fonts) served at the root URLs (`/css/app.css`, `/fonts/…`) |
| `src/Platform/DirectService.Platform.{Domain,Application,Infrastructure,Contracts,UI}` | the platform: core.* entities, identity and sign-in, tenants, subscriptions and billing, staff and provider console, system e-mail (outbox, Microsoft 365), notifications, audit-log viewer, company settings; the contribution interfaces a module implements (`Platform.Contracts/Modules`); the console, tenant and account screens |
| `src/DirectService.Transportation.Domain` | the module's entities (trn, acc and the rest of the design; Accounting moves out in Stage 3) |
| `src/DirectService.Transportation.Contracts` | the module's request/response records shared by the API and the UI (identifiers are URL tokens) |
| `src/DirectService.Transportation.Application` | the module's business rules and services (B1–B12) |
| `src/DirectService.Transportation.Infrastructure` | the module's EF Core configurations (`TransportationModelContributor`), e-invoicing connectors, the module's seed |
| `src/Hosts/DirectService.Database` | the EF Core migrations (20 + the model snapshot) and the design-time factory (user secrets `dst-infrastructure-designtime`); every `dotnet ef` command names this project |
| `src/DirectService.Transportation.Web` | ASP.NET Core host: minimal-API endpoints `/api/v1`, account pages (static SSR), workers, SignalR hub, hosts the UI (renamed `DirectService.Web` in Stage 4) |
| `src/DirectService.Transportation.Web.Client` | Blazor WebAssembly app: module screens (`Pages/`), the static menu, links and help content, AR/EN module text (`Localization/`), composed by `ClientComposition` |
| `tests/DirectService.Architecture.Tests` | layer and module rules (design a.2), stored names against golden files, migration history; runs first in CI |
| `tests/DirectService.Transportation.*.Tests` | Domain, Application (unit), Infrastructure (database rules), Api (integration, isolation, security), UI (bUnit) |
| `tests/DirectService.Testing` | shared test helpers (database fixture, API factory, golden-file helpers) |
| `tests/e2e` | Playwright end-to-end flows (PRD Section 9) and the WCAG 2.1 AA check, Arabic and English |
| `database/` | `00_CreateDatabase.sql`, `MasterDatabase_Idempotent.sql`, `90_ApplicationUser.sql` |
| `docs/` | step documents (requirements, architecture, database, block reports, design system, this guide), `docs/api/openapi-v1.json` |

## 3. Database

1. Create the database with the approved collation and read-committed snapshot (Q-07, DT-11):
   `sqlcmd -S <server> -E -I -i database/00_CreateDatabase.sql`
2. Apply the schema and reference data (permissions, preset role keys, TRIAL plan, posting keys…):
   `sqlcmd -S <server> -E -I -d DirectServiceMaster -i database/MasterDatabase_Idempotent.sql`
   (or, for development: `dotnet ef database update --project src/Hosts/DirectService.Database --startup-project src/Hosts/DirectService.Database`; the host also applies missing migrations itself in Development, step 4).
3. Production: create the application login and run `database/90_ApplicationUser.sql` (read/write, `DENY DELETE` on business schemas, `DENY UPDATE` on the audit, maintenance and rate-history logs, no DDL). The host connects as that user, never as an administrator.
4. **Version check at start-up (DT-209):** the host compares the database with the migrations of its version. In Development it applies any missing migration itself and logs which ones; in every other environment it refuses to start and names the missing migrations — apply step 2's script after every update, then start the application.
5. Regenerate the script after a model change: `dotnet ef migrations script --idempotent --project src/Hosts/DirectService.Database --startup-project src/Hosts/DirectService.Database --output database/MasterDatabase_Idempotent.sql`. A new migration: `dotnet ef migrations add <Name> --project src/Hosts/DirectService.Database --startup-project src/Hosts/DirectService.Database` (only with an approved design revision). Check that the model matches the snapshot: `dotnet ef migrations has-pending-model-changes --project src/Hosts/DirectService.Database --startup-project src/Hosts/DirectService.Database` (also an Architecture test). The history table stays `dbo.__EFMigrationsHistory` (named explicitly, schema left to the default so the script is unchanged). Do not re-run `tools/design/gen_efcore.py` (it would overwrite hand-maintained entities).

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
| `Email:DropFolder`, `Email:From` | the **Development** mode of system email: messages written as `.eml` files | `.local/mail`; the mode itself (Development / Microsoft 365) is chosen in the provider console → **System email**, see [Email_Microsoft365_Setup.md](Email_Microsoft365_Setup.md) |
| `Email:PerMinute`, `Email:MaxAttempts`, `Email:PerRecipientPerHour`, `Email:LogRetentionDays` | system email outbox: emails a minute (Exchange Online limit), attempts before Failed, same kind to one address an hour, days the recipient stays in the log | 30 / 6 / 5 / 180 |
| `Email:GraphBaseUrl`, `Email:AuthorityHost` | Microsoft Graph and sign-in (other Microsoft clouds only) | `https://graph.microsoft.com/v1.0/`, `https://login.microsoftonline.com/` |
| `Files:Root` | stored files (background exports) | `App_Data/files` |
| `Printing:ChromiumPath` | Chrome/Edge executable for PDFs | found automatically when installed |
| `RateLimits:AuthPerMinutePerIp`, `RateLimits:ApiPerMinutePerUser` | brute-force and API limits | 20 / 600 |
| `Jobs:Enabled` | background workers (pending postings, e-invoicing queue, lock expiry, exports, tenant lifecycle, email outbox) | true |
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

**System email (DT-250…DT-253).** Every email goes into the outbox table `core.EmailOutbox`; the email outbox worker (part of `Jobs:Enabled`) sends it with the mode chosen in the provider console → **System email**: *Development* (the `.eml` files above, the default — nothing leaves the machine) or *Microsoft 365* (Microsoft Graph as the mailbox `no-reply@directservicetech.com`, an Entra app with a client secret). How to set up Microsoft 365, DNS (SPF, DKIM, DMARC), the sending limits, rotating the secret and changing the mailbox: **[Email_Microsoft365_Setup.md](Email_Microsoft365_Setup.md)**. New settings take effect only after a successful test email from that screen; saving them asks for a fresh authenticator code. The `create-staff` command sends its email itself before it exits.

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
| One suite | `dotnet test --project tests/DirectService.Transportation.Api.Tests` | ~8 min (without the performance check below, which it leaves out); on a PC low on memory run `dotnet build-server shutdown` first (SQL memory grants) |
| Coverage | add `--coverage --coverage-output-format cobertura` | results in `TestResults/` |
| Same as CI | `dotnet build DirectService.Transportation.slnx -c Release`, then per project (first `tests/DirectService.Architecture.Tests`, then `Domain`, `Application`, `UI`, `Infrastructure`, `Api`) `dotnet test --project tests/DirectService.Transportation.<Name>.Tests -c Release --no-build --coverage --coverage-output-format cobertura --coverage-output <Name>.cobertura.xml` | every test project shares `tests/Directory.Build.props` (xUnit v3 on Microsoft.Testing.Platform + coverage extension), so the same arguments work for each, on Windows and Linux (DT-225) |
| Performance check (before a release) | `powershell -ExecutionPolicy Bypass -File tools/perf/run-performance-check.ps1` (Windows) or `tools/perf/run-performance-check.sh` (Linux / Git Bash) | **not** in `dotnet test` or CI (DT-227): the NFR-01 test — one company with 500,000 trips, the Created Trips list (first page, filters, totals, search) under **1 second** — is an explicit xUnit test (`[Fact(Explicit = true)]`, category `Performance`). The script builds the API tests in Release and runs only the explicit tests (`--explicit only`) on their own database `DirectServiceMaster_PerfTests` (recreated each run; another server with `-ConnectionString "…"` or `DST_API_TEST_DB`). Takes a few minutes, most of it seeding. Run it on a quiet machine — close Visual Studio, browsers and dev servers, the script stops idle compiler servers itself — ideally on hardware like the hosting environment; it times the answers, so a busy PC makes it fail. Exit code 0 = passed; each query's time is in the output. Before every release: run it, and treat a failure that repeats on a quiet machine as a regression |
| UI component and page tests | `dotnet test --project tests/DirectService.Transportation.UI.Tests` | bUnit against a fake API |
| E2E (Playwright) | `cd tests/e2e && npm ci && npx playwright test` | builds nothing: run `dotnet build` first. Recreates `DirectServiceMaster_E2E`, creates the staff account, starts the host on port 52301 with its own mail folder (`.local/e2e/mail`), uses the installed Edge. Report: `TestResults/e2e-report` |
| Accessibility | `npx playwright test accessibility` (in `tests/e2e`) | axe-core WCAG 2.1 AA on every screen, AR and EN; findings in `TestResults/a11y-*.json` |
| Visual layout | `npx playwright test visual` (in `tests/e2e`) | every screen, dialog, side panel and menu, AR and EN, at 1280 and 1366 px wide, light and dark theme: fails on overlapping text (text over text, or a picture/field over text), text cut off by its box, and sideways page scroll (`support/layout.ts`). Screenshots of every combination in `TestResults/visual/<lang>/<width>-<theme>/`, problems in `TestResults/visual/problems-*.json`; `VISUAL_SHOTS=0` skips the screenshots. `visual-checker.spec.ts` proves the checker catches the two layouts reported on 12 Oct 2026 and stays quiet on dialogs, scrolled lists, ellipses, unit adornments and sticky totals. ~45 min |

## 9. Hosting notes (the provider and residency are deferred, DEF-01)

- One stateless host image (ASP.NET Core) behind HTTPS (TLS 1.2+); the WebAssembly UI is served by the same host.
- Include Chrome/Chromium in the image for PDFs (or set `Printing:ChromiumPath`).
- Several instances need: the shared data-protection key ring, a shared file store for `Files:Root`, and a SignalR backplane (Redis or Azure SignalR) — the code is ready, the backplane is a hosting choice.
- SQL Server: encryption at rest (TDE), automated encrypted backups with point-in-time restore (NFR-05: RPO 15 min, RTO 4 h), quarterly restore drill (TST-07).
- Background jobs run in every instance; the tenant lifecycle job takes a platform-wide lock (`sp_getapplock`), the queues claim rows with `READPAST`.
- Before launch: e-invoicing certification against the JoFotara test environment (TST-04 / REL-04), payment gateway (DEF-02; e-mail is done: Microsoft 365, see [Email_Microsoft365_Setup.md](Email_Microsoft365_Setup.md)), penetration test (SEC-15 / REL-05), legal check of anonymisation and audit retention (SEC-19, DT-134).
- Behind a TLS-terminating proxy or load balancer set `Hosting:BehindProxy=true` and list the proxies in `Hosting:KnownProxies` (addresses) and/or `Hosting:KnownNetworks` (CIDR ranges, e.g. the container network); the host refuses to start behind a proxy without them. The scheme and client address then come from `X-Forwarded-*`. In the container image set `Printing:NoSandbox=true` (done by the Dockerfile) because Chromium cannot use its own sandbox there.
- Monitoring (NFR-18): liveness `/health/live`, readiness `/health/ready` (database); structured logs from ASP.NET Core logging (no secrets or personal data are logged); request, connection, rate-limiter and SignalR metrics are published by the built-in .NET meters (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.Http.Connections`) — read them with `dotnet-counters` or connect an OpenTelemetry exporter when the hosting provider is chosen.
- Container: `Dockerfile` at the repository root (see its header for build/run); CI: `.github/workflows/ci.yml` on GitHub (ubuntu-24.04, SQL Server container: restore with NuGet audit, Release build, all five .NET test projects with coverage, coverage upload, OpenAPI lint; Chrome prints the PDFs without its own sandbox, `Printing__NoSandbox`). The E2E, accessibility and visual suites are not in CI: they need the Windows tools of the development PC (installed Edge, Windows `sqlcmd`, `taskkill`) and 30+ minutes; run them on that PC before a release. The container image has not been built yet (no container runtime on the development PC).
