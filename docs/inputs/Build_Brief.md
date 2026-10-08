# Direct Service Transportation — Build Brief (start here)

Read this first in any build chat. It points to the approved inputs and lists the decisions that are already settled.

## Approved inputs (in this project)
1. `claude/PRD_v1.2.md` — Product Requirements Document v1.2, approved for build (Sections 1–19).
2. `claude/Database_Design.md` — agreed Master Database design, revision 4 (incl. `core.Users.EmailConfirmedAt`): 66 tables (22 Transportation with the TRN_ prefix, 44 shared), 118 business foreign keys; every column with type, size, nullability, default, description and rules, plus named keys, unique constraints, indexes, relationships and business rules per table. It takes precedence over PRD Section 11 for physical names.
3. Visual version of the database design (diagrams + searchable catalogue): https://claude.ai/artifact/BKNbYXkgCEYo3KVF9JWzZ5
4. `claude/Build_Prompt.md` — the build prompt to paste into the first message of the build chat.
5. Word and PDF copies of the PRD were delivered to the owner (Direct_Service_Transportation_PRD_v1.2.docx / .pdf).

Note: the other docs in this project (`masar-project/…` and `PRD_Masar.docx`) describe the reference system that was analysed. The new product follows its business flows and rules but must never mention or refer to it — in code, UI text, comments or documents.

## Product
- Name: Direct Service Transportation. Owner: Direct Service Company.
- Multi-tenant SaaS for bulk-haulage companies. Launch market Jordan (JOD, 16 % sales tax, JoFotara e-invoicing UBL 2.1); more markets later.
- Tenant → Companies (legal entities) → Users with one role per company.

## Settled technical decisions
- .NET 10 (latest LTS), Blazor Web App, **Entity Framework Core (code-first, Fluent API, versioned migrations) as the only data-access technology**, layered (clean) architecture, SignalR for live updates and record locks, background jobs for posting retries and e-invoice submission.
- SQL Server 2025, one shared **Master Database** for a family of projects. Shared tables (core / acc / cfg) are reused, never duplicated. Transportation tables use the `TRN_` prefix in schema `trn`.
- Accounting (chart, journal, periods, cash/banks, AP/AR) is shared by all projects; transport posts into it.
- Soft delete for every entity — no hard deletes ever.
- 2FA (authenticator app) mandatory for every user, including provider staff; recovery codes hashed.
- URLs carry only the hashed random PublicId — internal IDs never exposed.
- Custom fields as a `CustomValues` JSON column; record history from append-only `core.AuditLogs`.
- API is for the platform's own interface only. Light theme only.
- One truck per driver, registered and edited from the driver's profile. Driver details shown in full to users with permission.
- An e-invoice can only be cancelled by a credit note. Invoice approval setting defaults to on.

## Brand (light theme)
Primary #0F4C5C · primary dark #0A3540 · tint #E4EDEE · text #171A1C · secondary text #5B6570 · borders #E3E7E6 · page #F6F8F7 · surfaces #FFFFFF. Fonts: IBM Plex Sans Arabic (Arabic), Inter (English). Logo: Direct Service double-chevron mark.

## Confirmed design decision (8 Oct 2026)
- `core.Users.Email` stays unique platform-wide: one email = one account in one tenant. A person working for two subscribing organisations uses two email addresses. Inside one tenant a user can still work in several companies with a role per company. No open points remain.

## Deferred (decide during/after build — use provider-neutral interfaces)
- Hosting provider and data residency.
- Payment gateway and email provider.
- Legal check on anonymising personal data (owner handles).

## How the build runs
- Code goes into a folder on the owner's computer (connect it to the chat). The .NET 10 SDK and SQL Server are already installed there.
- First step: the Master Database (shared tables + TRN_ tables) from `claude/Database_Design.md`, as an Entity Framework Core model + initial migration, plus an idempotent SQL script for review.
- The Direct Service logo is not stored in the project (text only); the owner places it in the code folder as `assets/logo.png`.
- Then build module by module; the owner reviews each module before the next starts.
- Raise any requirement gap with the owner instead of assuming.
