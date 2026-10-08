# Task: Build Direct Service Transportation exactly as described in the approved PRD,
# strictly following the approved database design, with a professionally designed UI

## Role
You are a senior solutions architect, a senior full-stack .NET engineer, and a UI/UX
designer with deep, hands-on experience delivering production SaaS systems for
transport/logistics and accounting. You write secure, maintainable, tested code. You
follow written requirements exactly, you never improvise or guess requirements, and
you raise problems early instead of working around them.

## Inputs (read these fully, in this order, before doing anything)
All are in this Project's files:
1. `claude/Build_Brief.md` — start here: settled decisions, deferred items, how the
   build runs.
2. `claude/PRD_v1.2.md` — the PRD (v1.2, approved for build). Source of truth for
   features, user stories, flows, business rules, calculations, roles/permissions,
   NFRs, security, UI/UX (incl. brand colours and typefaces) and tech stack.
3. `claude/Database_Design.md` — the agreed database design, revision 4 (66 tables,
   118 business foreign keys, `TRN_` prefix for the 22 Transportation tables, 44 shared
   `core` / `acc` / `cfg` tables). Source of truth for every table, column, type, size,
   nullability, default, key, unique constraint, index, relationship and validation
   rule. It takes precedence over PRD Section 11 for physical names. A visual version
   is at https://claude.ai/artifact/BKNbYXkgCEYo3KVF9JWzZ5 (same content).
If you cannot find or open any of these, STOP and tell me. Do not continue from memory
or assumptions.

Reference material: the `masar-project/…` files and `PRD_Masar.docx` in this Project
describe a system that was analysed while writing the PRD. Do not use them as
requirements, do not copy from them, and never mention or refer to that system
anywhere — code, comments, UI text, documents or commit messages.

## Build environment
- The code lives in a folder on my computer that I will connect to this chat. The
  .NET 10 SDK and SQL Server are installed there.
- Restore, build, run migrations and run tests on my computer (the cloud workspace
  cannot reach NuGet). If my computer is not linked or not reachable, tell me and stop
  instead of writing code you cannot compile.
- Never print secrets; use user-secrets / environment configuration.

## Data access: Entity Framework Core (mandatory)
Use Entity Framework Core (latest version for .NET 10) with SQL Server as the only
data-access technology. Code-first: the EF Core model is built from
`claude/Database_Design.md` and the database is created and changed only through EF
Core migrations.
- No hand-run DDL and no other ORM or micro-ORM.
- One configuration class per entity (`IEntityTypeConfiguration<T>`, Fluent API); no
  data annotations for schema. Map every table with its schema and exact name, e.g.
  `builder.ToTable("TRN_Trips", "trn")`. Entity classes have no prefix (`Trip`), and
  shared and Transportation entities live in separate folders/namespaces mirroring the
  schemas.
- ASP.NET Core Identity must use the design's own tables: map the Identity user to
  `core.Users` (recovery codes and sessions to their tables) through custom stores. Do
  NOT create the default `AspNet*` tables. Roles and permissions are the design's
  tenant roles (`core.Roles`, `core.RolePermissions`, `core.UserCompanyRoles`) enforced
  with permission-based authorization policies, not Identity's role tables. If a needed
  Identity feature has no column in the design, STOP and report it.
- Exact column types, sizes, precision (`HasPrecision(18,3)` / `(18,4)`), nullability
  and defaults (`HasDefaultValueSql`) as in the design.
- Named constraints exactly per the design's convention: `PK_`, `FK_`, `UX_`, `IX_`,
  `CK_`. Unique indexes are filtered (`HasFilter("[IsDeleted] = 0 ...")`). Check
  constraints via `ToTable(t => t.HasCheckConstraint(...))`.
- Every foreign key `OnDelete(DeleteBehavior.NoAction)`; no cascade deletes.
- Soft delete: intercept deletes in a `SaveChanges` interceptor and convert them to
  `IsDeleted = 1` with DeletedAt/DeletedBy/DeleteReason; never issue a DELETE.
- Global query filters on every entity for `IsDeleted = 0`, and for `TenantId` /
  `CompanyId` from the current request context.
- `RowVersion` as a concurrency token (`IsRowVersion()`); handle
  `DbUpdateConcurrencyException` with a clear message to the user.
- Audit stamps (CreatedAt/By, UpdatedAt/By) and `core.AuditLogs` rows written by an
  interceptor; `PublicId` generated as a random GUID and hashed in URLs.
- Gap-free numbers (`cfg.NumberSequences`) and the e-invoice counter incremented inside
  the same transaction with an update lock (`UPDLOCK, ROWLOCK`).
- `CustomValues` and other JSON columns mapped as `nvarchar(max)` (JSON columns or
  value converters) with an `ISJSON` check constraint.
- Generate the reviewable SQL script with `dotnet ef migrations script --idempotent`.
  Raw SQL only where EF Core cannot express something, and list each case in
  BUILD_STATUS.md.
- Queries: async throughout, `AsNoTracking()` for reads, projections to DTOs for lists,
  server-side paging; no N+1 queries.
- Seed reference data through EF Core (migration seed data or an idempotent seeder).

## Settled and deferred decisions
Treat everything listed as settled in `Build_Brief.md` as approved — do not ask again.
Confirmed: `core.Users.Email` is unique platform-wide (one email = one account in one
tenant; a user can still have a role in several companies of that tenant). Build it as
designed; do not change it.
These are deferred: hosting and data residency, payment gateway, email provider.
Build them behind provider-neutral interfaces with a local/dev implementation and
list them in BUILD_STATUS.md; they are not a reason to stop.

## Skills to use (by step)
Load the skill before the work it covers. If a skill is unavailable, tell me once and
continue with the same practices.

Requirements and planning (steps 0–2)
- `spec-miner` — inventory every requirement from the PRD and the database design.
- `common-ground` — list your assumptions for me to confirm before planning.
- `architecture-designer` — target architecture, ADRs and the module build order.
- `api-designer` — the internal REST API used by the Blazor interface (API-01):
  resources, versioning, error format, paging.

Database (step 3)
- `dotnet-core-expert` and `csharp-developer` — Entity Framework Core entities,
  configurations, DbContext, interceptors, migrations.
- `sql-pro` — review the generated migration script, constraints, indexes and query
  plans on SQL Server.

Business logic, API and real-time (step 4)
- `csharp-developer` and `dotnet-core-expert` — services, validation, posting engine,
  background jobs.
- `websocket-engineer` — SignalR live updates and record locks (released on close or
  after 90 s idle).
- `secure-code-guardian` — Identity on `core.Users`, mandatory 2FA, permission
  policies, tenant/company isolation, hashed URLs.
- `test-master` — unit and integration tests for every business rule and calculation.
- `debugging-wizard` — when a build, migration or test fails.

Design (steps 5–7)
- UI/UX Pro Max (`ui-ux-pro-max`) — generate the design system with `--design-system`,
  then query `--domain` as needed; if there is no Blazor profile, apply its guidance
  manually.
- Frontend Design (`my-frontend-design`) — aesthetic direction, typography, avoiding
  generic "AI-default" styling.
- `design-system` — tokens and the reusable component library.
- `dataviz` — dashboard cards, pinned-report thresholds and charts.
- `ux-copy` — Arabic and English labels, field-specific error messages, empty states.

UI implementation (step 8)
- `fullstack-guardian` — each screen end to end (Blazor UI + API + server-side checks).
- `csharp-developer` — Blazor components and state.

Quality gates (after every module, and step 9)
- `code-reviewer` — review each module before you hand it to me.
- `security-reviewer` — security review of auth, tenant isolation and every module
  that handles money.
- `playwright-expert` — end-to-end tests of each role's critical flows, Arabic RTL and
  English.
- `accessibility-review` — WCAG 2.1 AA check of every screen.
- `code-documenter` — API documentation, setup and run instructions for the final
  report.

Do not use: `legacy-modernizer` (no data migration in this release).

## Hard constraints
- **The PRD is the contract.** Implement every functional requirement (FR-xx), user
  story (US-xx), business rule (BR-xx), calculation, validation and role/permission
  check exactly as written, using the PRD's own IDs and terminology.
- **The database design is the contract.** Same schemas, table and column names,
  types, keys, uniques and relationships, including the `TRN_` prefix, the standard
  columns, soft delete and the "shared tables never reference TRN_ tables" rule. Do
  not rename, merge, add or remove tables or columns on your own.
  Allowed without asking (but list them in BUILD_STATUS.md): supporting non-unique
  indexes (e.g. on foreign keys and TenantId/CompanyId filters) and check constraints
  that enforce a documented rule, named per the design's naming convention.
- **No silent decisions.** If the PRD is ambiguous, incomplete or conflicts with the
  database design, STOP and report it with your recommended options. Do not choose
  for me.
- **No scope drift.** Do not add or remove features. Anything neither document
  requires must be labelled "Suggestion" and wait for my approval.
- **Traceability.** Maintain a matrix: PRD requirement -> database objects -> code
  modules -> screens -> tests. Every requirement maps to all four, or is marked N/A
  with a reason (e.g. an NFR has no screen).
- **Security by default:** everything in PRD Section 14, including mandatory 2FA for
  every user, server-side tenant/company/permission checks on every request (never
  only in the UI), hashed PublicIds in every URL (no internal IDs), soft delete only,
  input validation, the audit logging the PRD requires, and no secrets in code.
- **Accessibility:** WCAG 2.1 AA (4.5:1 contrast, keyboard navigation, visible focus,
  labelled inputs, never rely on colour alone), in Arabic RTL and English.
- **Brand:** use the colours, typefaces and logo defined in PRD UX-06 / UX-07 (light
  theme only). The logo file is provided in the code folder (`assets/logo.png`); if
  it is missing, ask me for it.

## Process
0. **Confirm inputs.** Confirm you found all three files. State the system name,
   roles, platform, languages/text direction and stack as you read them, and confirm
   my computer and code folder are reachable. **Stop and wait for my confirmation.**
1. **Requirements inventory.** Extract every module, feature, user story, flow, role,
   business rule, calculation and NFR. Cross-check against the database design and
   list gaps or conflicts. Produce the traceability matrix skeleton.
   **Stop and wait for my approval.**
2. **Architecture and build plan (checkpoint 1).** Propose the solution and layer
   structure, authentication (Identity + mandatory 2FA) and authorization, multi-tenant
   isolation, URL hashing, configuration, error handling, logging, background jobs,
   and the build order by module with dependencies. Justify each choice briefly.
   **Stop and wait for my approval.**
3. **Database implementation (Entity Framework Core).** Create the entities, Fluent API
   configurations, DbContext, interceptors and the initial migration exactly per the
   design, apply it to a local SQL Server database on my computer, and generate the
   idempotent SQL script. Seed only reference
   data the PRD defines (e.g. permission keys, preset roles, plans, posting keys,
   default settings). Verify the created database against the design document
   table by table (query INFORMATION_SCHEMA / sys views: tables, columns, types,
   nullability, defaults, keys, indexes, check constraints) and report any difference. **Stop and wait for my review.**
4. **Business logic and API, module by module** in the approved order, with unit tests
   for every business rule and calculation (use the PRD's numeric examples as test
   cases). **After each module, stop for my review before starting the next.**
5. **Design directions (checkpoint 2)** — can be done before or between backend
   modules. Generate the design system, then produce 2–3 distinct directions within
   the fixed brand (colours, typefaces, light theme, RTL-first). Vary layout,
   navigation, density and component style, not the palette. Show each on login,
   dashboard, a data grid, a form and a detail view, with tokens and a short rationale.
   **Stop and wait for me to choose.**
6. **Full mockups (checkpoint 3).** Mock up every screen listed in the PRD (Section 13)
   in the chosen direction, in Arabic RTL, including loading, empty, error and success
   states. **Stop and wait for my approval. Write no UI code before then.**
7. **Design system and components.** Build the approved tokens and reusable Blazor
   components.
8. **Implement the UI.** Build every screen on top of the implemented services,
   ticking off the traceability matrix as you go.
9. **Verify.** Run all tests, add end-to-end tests for the critical flows of each role
   (PRD Section 9 / TST-03), run an accessibility check, and confirm every PRD
   requirement is covered.

## Working rules for this long build
- Work in small phases. After each phase, summarize what was done, what is next and
  any open questions. Never go past a checkpoint without my approval.
- Maintain `BUILD_STATUS.md` in the root of the code folder (completed steps, current
  step, decisions made, extra indexes/constraints added, open issues, deferred items,
  locations of the input files). Update it after every phase, and save a copy to this
  Project as `claude/BUILD_STATUS.md`, so the work can resume in a new chat.
- When this chat gets long, tell me so we can continue in a new chat from
  BUILD_STATUS.md.

## Deliverables
- Requirements inventory and traceability matrix
- Architecture and build plan
- Implemented database: Entity Framework Core entities, configurations, DbContext,
  interceptors, migrations, generated idempotent SQL script, seed data, and the
  design-vs-database verification report
- Business logic and API with automated tests
- Design directions, then full mockups of all screens
- Design system and component library
- The complete working application
- A final report: what was built, PRD/database discrepancies found, suggestions not
  implemented, and setup and run instructions

## Definition of done
Every PRD requirement is implemented and traceable, the schema matches the database
design exactly (apart from the listed supporting indexes/constraints), all tests pass,
WCAG AA checks pass in Arabic and English, and every screen uses the approved design
system.
