# Direct Service Transportation — Setup, run and operations

_Step 9, 11 Oct 2026. For developers and the people who will host the system. Everything here was run on the owner's development PC (Windows 11, SQL Server 2025 Developer)._

## 1. What you need

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.0.401 (pinned in `global.json`, roll-forward latestFeature) | build, run, tests |
| SQL Server | 2019 or later (developed on 2025 Developer), Windows or SQL authentication | the Master Database |
| `dotnet-ef` | 10.0.12 (`dotnet tool install -g dotnet-ef --version 10.0.12`) | migrations, idempotent script |
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
4. Regenerate the script after a model change: `dotnet ef migrations script --idempotent --project src/DirectService.Transportation.Infrastructure --startup-project src/DirectService.Transportation.Infrastructure --output database/MasterDatabase_Idempotent.sql`. Do not re-run `tools/design/gen_efcore.py` (it would overwrite hand-maintained entities).

## 4. Configuration

Settings come from `appsettings.json`, then `appsettings.<Environment>.json`, then user secrets (Development) and environment variables (`Section__Key`). **Secrets never go into the files** (`.gitignore` excludes local secret files; `UrlTokens:Key` is empty on purpose).

| Key | Meaning | Default / note |
|---|---|---|
| `ConnectionStrings:MasterDb` | the Master Database | required |
| `UrlTokens:Key` | 16-byte base64 key for the hashed public ids in URLs | **required secret** (`dotnet user-secrets set UrlTokens:Key <base64>` in development; secret store in production). Changing it breaks every saved link |
| `App:PublicBaseUrl` | absolute address used in e-mails | e.g. `https://app.example.com` |
| `App:AbsoluteSessionHours` / `App:PlatformStaffIdleMinutes` | session lifetime 12 h (Q-04) / staff idle timeout | 12 / 30 |
| `App:TrialDays`, `App:InvitationDays`, `App:TermsVersion` | trial length, invitation validity, terms version recorded at sign-up | 14 / 7 / draft-1 |
| `DataProtection:KeysPath` | key ring (cookies, antiforgery, encrypted e-invoicing credentials) | `.local/keys`; **shared, persistent storage in production** (DPAPI-protected on Windows) |
| `Email:DropFolder`, `Email:From` | development e-mail: messages are written as `.eml` files | provider to choose (DEF-02) |
| `Files:Root` | stored files (background exports) | `App_Data/files` |
| `Printing:ChromiumPath` | Chrome/Edge executable for PDFs | found automatically when installed |
| `RateLimits:AuthPerMinutePerIp`, `RateLimits:ApiPerMinutePerUser` | brute-force and API limits | 20 / 600 |
| `Jobs:Enabled` | background workers (pending postings, e-invoicing queue, lock expiry, exports, tenant lifecycle) | true |
| `Platform:*` | subscription invoices: `TaxRate` 16, `Currency` JOD, `SellerName`, `SellerTaxNumber`, `GraceDays` 7, `SuspendedDaysBeforeCancel` 30, `RetentionDays` 90 | provider's own data |
| `Billing:WebhookSecret`, `Billing:PaymentPageUrl` | payment gateway (development fake; no secret → every webhook refused) | provider to choose (DEF-02) |
| `EInvoicing:JoFotara:TestUrl` / `ProductionUrl` / `TimeoutSeconds` | national e-invoicing endpoints | **to verify with the owner's test credentials (R-01)** |
| `EInvoicing:AllowSimulator` | offers the e-invoicing simulator connector | development/tests only, never in production |

## 5. Run (development)

```bash
dotnet build DirectService.Transportation.slnx
```
```bash
dotnet run --project src/DirectService.Transportation.Web --launch-profile DirectService.Transportation.Web
```
Open `https://localhost:52233`. Health: `/health/live`, `/health/ready`. API reference (Development only): `/scalar`, OpenAPI document `/openapi/v1.json` (exported copy in `docs/api/openapi-v1.json`, linted with `npx @redocly/cli lint docs/api/openapi-v1.json`). Development e-mails (verification, invitations, password resets) are written to `.local/mail`.

The only launch profile is `DirectService.Transportation.Web`; any other profile name silently runs as Production without a connection string.

## 6. Operations commands

| Command | What it does |
|---|---|
| `dotnet DirectService.Transportation.Web.dll create-staff <email> <full name>` (or `dotnet run --project src/DirectService.Transportation.Web -- create-staff …`) | creates a **platform staff** account (provider console, D-13; no tenant, no password) and e-mails a set-password link; the first sign-in sets up two-step verification. Running it again for an existing staff account sends a new link. Sign-up never creates staff (DT-131) |

## 7. Tests

| Suite | Command | Notes |
|---|---|---|
| All .NET tests | `dotnet test` | needs local SQL Server; Api/Infrastructure tests create `DirectServiceMaster_Tests` / `DirectServiceMaster_ApiTests` |
| One suite | `dotnet test --project tests/DirectService.Transportation.Api.Tests` | ~8 min; on a PC low on memory run `dotnet build-server shutdown` first (SQL memory grants) |
| Coverage | add `--coverage --coverage-output-format cobertura` | results in `TestResults/` |
| UI component and page tests | `dotnet test --project tests/DirectService.Transportation.UI.Tests` | bUnit against a fake API |
| E2E (Playwright) | `cd tests/e2e && npm ci && npx playwright test` | builds nothing: run `dotnet build` first. Recreates `DirectServiceMaster_E2E`, creates the staff account, starts the host on port 52301 with its own mail folder (`.local/e2e/mail`), uses the installed Edge. Report: `TestResults/e2e-report` |
| Accessibility | `npx playwright test accessibility` (in `tests/e2e`) | axe-core WCAG 2.1 AA on every screen, AR and EN; findings in `TestResults/a11y-*.json` |

## 8. Hosting notes (the provider and residency are deferred, DEF-01)

- One stateless host image (ASP.NET Core) behind HTTPS (TLS 1.2+); the WebAssembly UI is served by the same host.
- Include Chrome/Chromium in the image for PDFs (or set `Printing:ChromiumPath`).
- Several instances need: the shared data-protection key ring, a shared file store for `Files:Root`, and a SignalR backplane (Redis or Azure SignalR) — the code is ready, the backplane is a hosting choice.
- SQL Server: encryption at rest (TDE), automated encrypted backups with point-in-time restore (NFR-05: RPO 15 min, RTO 4 h), quarterly restore drill (TST-07).
- Background jobs run in every instance; the tenant lifecycle job takes a platform-wide lock (`sp_getapplock`), the queues claim rows with `READPAST`.
- Before launch: e-invoicing certification against the JoFotara test environment (TST-04 / REL-04), payment gateway and e-mail provider (DEF-02), penetration test (SEC-15 / REL-05), legal check of anonymisation and audit retention (SEC-19, DT-134).
- Behind a TLS-terminating proxy or load balancer set `Hosting:BehindProxy=true` and list the proxies in `Hosting:KnownProxies` (addresses) and/or `Hosting:KnownNetworks` (CIDR ranges, e.g. the container network); the host refuses to start behind a proxy without them. The scheme and client address then come from `X-Forwarded-*`. In the container image set `Printing:NoSandbox=true` (done by the Dockerfile) because Chromium cannot use its own sandbox there.
- Monitoring (NFR-18): liveness `/health/live`, readiness `/health/ready` (database); structured logs from ASP.NET Core logging (no secrets or personal data are logged); request, connection, rate-limiter and SignalR metrics are published by the built-in .NET meters (`Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.RateLimiting`, `Microsoft.AspNetCore.Http.Connections`) — read them with `dotnet-counters` or connect an OpenTelemetry exporter when the hosting provider is chosen.
- Container: `Dockerfile` at the repository root (see its header for build/run); CI: `.github/workflows/ci.yml` (build, tests with coverage, OpenAPI lint). Neither has been run yet.
