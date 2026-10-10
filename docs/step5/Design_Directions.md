# Step 5 — Design directions (checkpoint 2)

_10 Oct 2026. **Waiting for the owner's choice.** No UI code is written before the step 6 mockups are approved._

**Interactive board:** `docs/step5/design-directions.html` (open it in a browser). The same page is also published as a private artifact (link in the session message). Each direction shows five screens in Arabic RTL with real transport content: sign-in, dashboard, trips data grid with quick entry, trip form, driver detail.

## Design system basis (UI/UX Pro Max, `--design-system`, density 8 · motion 2 · variance 3)
- **Style chosen by the tool:** Minimalism & Swiss Style ("enterprise apps, dashboards, professional tools"), dense dashboard spacing (8–32 px scale), subtle motion only, a clear type hierarchy, no decorative effects. This matches PRD UX-01 and UX-08.
- **Overridden by the fixed brand:** the tool proposed a dark navy palette with a green accent and the Fira typefaces. Neither is used. All three directions keep PRD UX-06/UX-07 exactly: Primary #0F4C5C, Primary dark #0A3540, Tint #E4EDEE, Text #171A1C, Secondary #5B6570, Borders #E3E7E6, Page #F6F8F7, Surfaces #FFFFFF; IBM Plex Sans Arabic (Arabic) and Inter (English and figures, tabular numbers); light theme only (UX-10).
- **Semantic colours (added, all ≥ 4.5:1 text on their backgrounds):** success #1D6B45 on #E3F2EA, warning #8A5300 on #FFF2D6, error #B42318 on #FDECEA, info #1D5C9E on #E7F0FA. A status always carries an icon and text, never colour alone (UX-05).
- **Common rules in every direction:** RTL-first, with numbers isolated as left-to-right runs; one SVG icon set (no emoji); a visible 2 px focus ring; 32 px minimum targets on desktop and 44 px on touch; numeric inputs right-aligned with fixed decimals; the source of each price and rate shown under its field (UX-17); errors next to the field.

## The three directions

| | **A · Ledger** | **B · Dispatch board** | **C · Command** |
|---|---|---|---|
| Idea | The accountant's book: everything visible, dense and quiet | List and detail side by side; work through records one by one | Keyboard and tabs: jump anywhere, keep several records open |
| Navigation | Fixed dark side menu (right, 228 px) with the six PRD groups (UX-11) | 68 px icon rail for the groups + 200 px light list of the group's pages with live counts | Top bar of groups with drop-downs, open records as tabs, Ctrl+K command bar |
| Density | Row 32 px · control 32 px · base text 14 px | Row 40 px · control 36 px · base text 14.5 px | Row 36 px · control 34 px · base text 14 px, with a compact/comfortable switch (30 px) |
| Spacing | 4 · 8 · 12 · 16 · 24 | 8 · 12 · 16 · 24 · 32 | 4 · 8 · 12 · 16 · 20 · 24 |
| Components | 3 px corners, hairline borders, no shadows, square tags | 10 px panels, 8 px controls, pill filters and badges, drawer for forms | 6 px corners, segmented controls, toolbars, saved views, visible keyboard hints |
| Trips screen | Quick-entry strip on the page above a 15-column grid with totals | Status chips with counts; list beside a live detail pane; new trip in a drawer | Saved views, a filter row under the headers, multi-select with bulk actions, full width |
| Trip form | Two areas: fields and a calculation panel | Drawer over the list with totals in its footer | Full page with totals in a fixed bottom bar |
| Dashboard | One strip of eight month-to-date figures, an alert table, balances, pinned indicators | A follow-up queue: each alert has its next action on a button | Wide card grid, pinned reports with threshold bars, balance strip |
| Grid width at 1280 px | about 1,050 px | about 610 px with the pane open | full 1,280 px |
| Best for | High-volume entry clerks and accountants | Dispatchers and managers reviewing and correcting | Mixed teams switching between trips, invoices and ledger |
| Main risk | Whole menu at once; density tires readers | Pane takes grid width | Top menu holds fewer items; tabs need limits |

## Question for the owner
Which direction should the step 6 mockups use: **A**, **B** or **C**? Parts can be combined, for example "C, with A's side menu" or "A, with B's detail pane on the trips list". Step 6 then mocks up every screen of PRD 13.5 in the chosen direction, in Arabic RTL, with loading, empty, error and success states, and stops again for approval.
