# Direct Service Transportation — Step 2 Owner Decisions and Working Mode

_Decided by the owner on 8 Oct 2026. Answers to Q-01 … Q-10 in `docs/step2/01_Architecture_and_Build_Plan.md` §1._

## Step 2 answers
| # | Topic | Decision |
|---|---|---|
| Q-01 | Architecture (sections 3–5, 8–9) | **Approved.** |
| Q-02 | Database design revision 5 | **Approved, option R5-A:** `TRN_Invoices.LayoutType` default 'Claim'; the four tenant columns on `core.Tenants` (PasswordMinLength, PasswordRequireLetter, PasswordRequireDigit, SessionIdleTimeoutMinutes) with their check constraints; `core.Companies.Status` VARCHAR(10) NOT NULL DEFAULT 'Active', CHECK IN ('Active','Archived'). |
| Q-03 | Default idle timeout | **30 minutes** (allowed 5–1,440). |
| Q-04 | Absolute session lifetime | **12 hours**, platform setting. |
| Q-05 | Password minimum length floor | **No going below 8** (allowed 8–64). |
| Q-06 | Archived company and plan limit | **Only Active companies count.** |
| Q-07 | Database collation | **`Arabic_100_CI_AI_SC_UTF8`.** |
| Q-08 | PDF engine | **HTML + headless Chromium.** |
| Q-09 | Build order (section 10) | **As proposed.** |
| Q-10 | S-01 local git repository, S-02 Node.js LTS | **Yes to both.** Commit locally after each completed module or phase; never push. |

## Working mode from now on (owner instruction — overrides the "stop after every step/module" rules in the build prompt)
The owner wants the build to keep running without stopping for approval after each step or module.

1. **Do not stop** after step 3 or after each module in step 4. Continue straight through steps 3, 4, 7, 8 and 9.
2. **Stop only for:**
   - **Step 5 — design directions:** the owner chooses a direction. Backend work (steps 3–4) may continue before and after.
   - **Step 6 — full mockups:** owner approval before any UI code.
   - **Chart-of-accounts template (D-10):** owner approval before it is seeded. Keep building other parts while waiting.
   - **A real blocker:** something that cannot be built, tested or verified without the owner (missing credentials, a failing environment you cannot fix, a decision that cannot be undone).
3. **New ambiguities or PRD/design conflicts:** choose the option you would recommend, write it in `BUILD_STATUS.md` under a new heading **"Decisions taken without asking (for owner review)"** with the options considered, and continue. Never change scope (no new features) this way; anything outside the PRD stays a "Suggestion" and is not built.
4. **Quality gates still apply to every module:** build, all tests passing, code review, security review for auth/tenant isolation/money modules. Do not move on with failing tests.
5. **After each module or phase:** update `BUILD_STATUS.md` (done, next, decisions taken without asking, extra indexes/constraints, raw SQL, open issues) and commit to the local git repository.
6. When the session's context gets long, write everything needed to resume into `BUILD_STATUS.md`, commit, and tell the owner to start a new Code session from it.
