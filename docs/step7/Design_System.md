# Step 7 — Design system and Blazor components

_11 Oct 2026. Built from the approved step 6 board (`docs/step6/`, direction **A · Ledger** with C's saved views, filter row, Ctrl+K bar and density switch). No new screens or features: the tokens and components only carry the approved look into Blazor._

## Where it lives
| Part | Location |
|---|---|
| WebAssembly UI project (references **Contracts and the UI libraries only**, ADR-02) | `src/DirectService.Transportation.Web.Client` (module screens, the static menu / links / help content and `ClientComposition`) |
| Design system, shell and client services (since Stage 2 session 15) | `src/Shared/DirectService.Shared.UI`; platform screens and layouts in `src/Platform/DirectService.Platform.UI` |
| Design tokens | `Shared.UI/wwwroot/css/tokens.css` (served at `/css/tokens.css`: the library's static web assets have the base path "/", DT-368) |
| Component styles (board class names, logical properties, RTL + LTR) | `Shared.UI/wwwroot/css/app.css` |
| Brand fonts (one copy, SIL OFL 1.1) | `assets/fonts/*.woff2` → served at `/fonts/…` (`Shared.UI/wwwroot/css/fonts.css`) and embedded in printouts (`SharedKernel/Printing/BrandFonts.cs`) |
| Icons (sprite from the board + 9 additions) | `Shared.UI/wwwroot/img/icons.svg` |
| Browser helpers (focus trap, shortcuts, outside click, device preferences) | `Shared.UI/wwwroot/js/ui.js` (ES module, no inline script); `theme.js` before the first paint |
| Components | `Shared.UI/Ui/` (primitives, feedback, forms, grid), `Shared.UI/Layout/` (shell); platform layouts in `Platform.UI/Layout/` |
| Interface text AR/EN | `Shared.UI/Localization/UiText*.cs` (common, reports, settings) with `Loc`; `Platform.UI/Localization/UiText.Platform.cs` (`PlatformTexts`) and the module parts in the browser app (`AppTexts`), added by `ClientComposition.Apply` |
| Host wiring | `Web/Components/App.razor`, `Program.cs` (`AddRazorComponents().AddInteractiveWebAssemblyComponents()`, `MapStaticAssets`, `MapRazorComponents`) |
| Component gallery (development only) | `/design` — every component in its states inside the real shell |
| Tests | `tests/DirectService.Transportation.UI.Tests` (bUnit, 52 tests) |

Render mode: interactive WebAssembly **without server prerendering** (the UI reaches data only through `/api/v1`); account pages (step 8) opt out of interactive routing and render statically on the server. `<html lang dir>` comes from the user's language (or the browser for anonymous pages).

## Tokens
| Group | Tokens | Values (from the board) |
|---|---|---|
| Brand | `--primary`, `--primary-dark`, `--primary-hover`, `--tint`, `--focus-ring` | #0F4C5C, #0A3540, #0C3F4D, #E4EDEE |
| Neutrals | `--text`, `--muted`, `--placeholder`, `--page`, `--surface`, `--surface-*`, `--border`, `--border-control`, `--border-input`, `--line-strong` | #171A1C, #5B6570, #6B757D, #F6F8F7, #FFFFFF … |
| Semantic | `--ok/-bg/-line`, `--warn…`, `--err…`, `--info…` | as the board |
| Side menu | `--side-bg`, `--side-bg-provider`, `--side-text`, `--side-group`, `--side-foot` | #0A3540, #1E2A2E … |
| Type | `--font-ar` (IBM Plex Sans Arabic), `--font-en` / `--font-num` (Inter), `--fs-xs … --fs-title`, weights 400–700, `--lh` 1.5 | 11.5 / 12.5 / 13.5 / 14 / 15 / 16 / 17 / 18 px |
| Spacing | `--sp-1 … --sp-12` | 2, 4, 6, 8, 10, 12, 14, 16, 20, 24 px |
| Shape | `--r` 3 px, `--r-lg` 4 px, `--r-pill` | Ledger: square-ish, hairline borders |
| Elevation | `--shadow-dropdown`, `--shadow-panel`, `--shadow-toast`, `--shadow-dialog`, `--scrim` | only floating layers have shadows |
| Motion | `--dur-fast` .15 s, `--dur-live` 1 s, `--dur-skeleton` 1.4 s | off under `prefers-reduced-motion` |
| Density | `--row`, `--ctl`, `--fs` on `[data-density]` | compact 32/32/14 px (default, DT-67) · comfortable 40/36/14.5 px |
| Layout | `--side-w` 224, `--top-h` 46, `--cmdk-w` 360, `--dialog-w` 480, `--panel-w` 360 | |

### Contrast (WCAG 2.1 AA)
| Pair | Ratio |
|---|---|
| Text #171A1C on white | 17.5 |
| Muted #5B6570 on white / page / table head / muted badge / read-only | 5.9 / 5.6 / 5.4 / 5.2 / 5.5 |
| Placeholder #6B757D on white (board #8A949C was 3.1 — changed) | 4.7 |
| Primary on white, white on primary | 9.5 |
| OK / Warn / Error / Info tags | 5.6 / 5.7 / 5.8 / 5.9 |
| Side menu text / group / footer on #0A3540 | 10.4 / 8.7 / 6.3 |
| Input borders and toggle "off" #8A9592 on white (non-text, 1.4.11; board #CDD5D3 was 1.5) | 3.1 |

Buttons, chips and the command bar keep the board's light border (#CDD5D3): they are identified by their text. Filter-row inputs keep it too (their placeholder and label identify them). Focus: 2 px primary outline (white on the dark menu) on every interactive element.

## Components
All components take Arabic and English, render left-to-right runs isolated (`<bdi class="num">` — owner fix 1), and use only tokens.

| Group | Component | Notes (states · accessibility) |
|---|---|---|
| Primitives | `Icon`, `Tx` (bidi text), `Num`, `Money`, `Kbd` | Icons decorative unless labelled; directional icons mirror in LTR (`Flip`) |
| | `Badge`, `StatusTag`, `StatusTags` | Icon + text always (UX-05); 38 status keys in `Statuses` (`st.*` text) |
| | `Btn` | default · primary · ghost · danger · danger-primary · small · disabled · busy (spinner, `aria-busy`) · shortcut hint; icon-only requires `AriaLabel`; `Href` renders a link |
| Layout | `PageHeader` (h1 + breadcrumbs + badges + actions), `Box`, `Figures`, `KeyValues` (dl), `TabStrip` (ARIA tabs, arrow keys follow reading order), `SavedViews` (+ Save view, list tools), `ListFrame`, `HistoryList`, `ProgressBar`, `Chip` (aria-pressed), `StepItem` | |
| Grid | `DataGrid<TItem>` + `GridColumn<TItem>`, `Pager` | sticky header; filter row; server sort with `aria-sort`; selection with select-all; clickable rows focusable (Enter); totals footer; group rows; void rows muted; live rows highlighted (UX-20); pager with range, gaps, rows per page (≤ 200, API-04) |
| Forms | `TextField`, `TextAreaField`, `NumberField`, `IntField`, `DateField`, `SelectField<T>`, `ComboField<T>`, `CheckField`, `ToggleField` | built on `InputBase` (EditForm validation); label linked to control; required mark; help or error next to the field with `aria-invalid` / `aria-describedby`; source note (e.g. "from tariff"); grid span. Numbers: LTR, tabular, Arabic-Indic digits accepted, decimals enforced, unit read with the field. Dates: typed in the user's format (default YYYY-MM-DD) + calendar button. Combo: WAI-ARIA combobox, Arabic-folded filtering or server search, ↓/↑/Enter/Tab/Escape, widens to the full name on focus (owner fix 4) |
| | `ErrorSummary`, `ProblemValidator` | summary with count and links that focus the field (UX-16); API problem details (`errors[].field`, `field`) placed next to the fields, the rest returned for a banner |
| Feedback | `Banner` (info · ok · warn · err · lock), `ToastService` + `ToastHost` (polite live region, success auto-closes, errors stay), `Modal` (focus trap, Escape, returns focus), `ConfirmDialog` (reason and/or typed word «حذف» / DELETE), `SidePanel`, `LockBar` (record lock / changed by another user) | |
| States | `StateView` (loading → skeleton of the screen kind, error with retry + reference, empty, content), `Skeleton`, `EmptyState`, `ErrorState` | the five states of every screen (step 6) |
| Shell | `AppShell`, `SideNav`, `TopBar`, `CommandBar`, `MenuButton`, `NavModel`, `ShellState` | skip link; menu filtered by permission (the API still checks every request), setup entry only while incomplete, Tenant group only with core.tenant.manage (DT-66); company switcher; Ctrl+K / "/" command bar (records + menu commands); bell with count; user menu with density (saved on the device, DT-69), language, sign out; provider console variant |

### Usage example
```razor
<PageHeader Title="@L["nav.trips"]" Crumbs="@([new Crumb(L["nav.operations"]), new Crumb(L["nav.trips"])])">
    <Actions><Btn Primary="true" Icon="plus" Text="@L["nav.newTrip"]" Shortcut="Ctrl N" /></Actions>
</PageHeader>
<StateView State="_state" OnRetry="Load" ErrorReference="@_problem?.Reference">
    <Empty><EmptyState Icon="truck" Title="…" Text="…" /></Empty>
    <ChildContent>
        <ListFrame>
            <ChildContent>
                <DataGrid TItem="TripListItem" Items="_page.Items" Key="t => t.Id" ShowFilters="true" Filters="_filters" OnFilter="Filter"
                          SortKey="@_sort" SortDesc="_desc" OnSort="Sort" OnRowClick="Open">
                    <Columns>
                        <GridColumn TItem="TripListItem" Title="…" Value="t => t.Number" Ltr="true" SortKey="number" FilterKey="number" />
                        <GridColumn TItem="TripListItem" Title="…" Value="t => Fmt.Money(t.Revenue)" Numeric="true" Footer="@Fmt.Money(_totals.Revenue)" />
                    </Columns>
                </DataGrid>
            </ChildContent>
            <Footer><Pager Page="_p" PageSize="_size" Total="_page.TotalCount" OnPage="Go" OnPageSize="Size" /></Footer>
        </ListFrame>
    </ChildContent>
</StateView>
```

## Other changes in this step
- **Printouts use the bundled fonts** (open issue B6): `DocumentPrinter` embeds the fonts as data: URIs and the PDF engine now allows **only** data:/about: requests (Google Fonts removed from the allow-list).
- **CSP** for WebAssembly: `script-src 'self' 'wasm-unsafe-eval'`, `style-src 'self' 'unsafe-inline'` (component style attributes; no inline scripts), `img-src 'self' data: blob:`, `font-src 'self' data:`, `connect-src 'self'`.
- `.claude/launch.json` configuration **web** runs the application (https://localhost:52233, gallery at `/design`).

## Code review (independent, 11 Oct 2026)
No High findings. Fixed:
- **M1** grid header/footer one render late after a column change → columns notify the grid when title/footer/visibility change.
- **M2** server messages could block a new submit → `ProblemValidator` clears them on every validation request (test added).
- **M3** API field paths used the first segment → last segment, with the line index for collections (`lines[1].amount`; test added).
- **M4** Escape in an open combo list / menu also closed the surrounding dialog → key events stop at the open popup.
- **M5** focus fell to the page after a menu item → returns to the menu button.
- **M6** non-tab controls inside `tablist` → "Save view" and tools render beside it; keys handled on tabs only; tabs have ids and optional `aria-controls`.
- **M7** English pages flipped back to RTL on start → the shell starts in the language the server rendered; a language change re-renders the shell and sets `dir`/`lang` at once.
- **M8** static files ran the full pipeline (session check, rate limiter, tenant query) → `MapStaticAssets().ShortCircuit()`.
- Low: units and the calendar button no longer overlap long values in RTL; stale combo results cleared and the picked option kept for server search; Enter on controls inside a clickable row no longer opens the row, `aria-selected` removed from rows; pager clamps the range and drops `aria-pressed`; number/date parse errors shown also outside forms, and "1,5" is refused instead of read as 15; visible menu focus distinct from the checked item, input focus visible in Windows high-contrast; gallery excluded from Release builds; links from data restricted to app-relative paths (`SafeHref`, test added); command bar "loading" state kept while a newer search runs.
- Not changed: `SelectField` with a non-nullable value type and an empty option returns the type's default (screens use nullable values for optional selections).

## Decisions taken without asking (recorded in BUILD_STATUS)
- DT-137 Contrast fixes to the board: placeholder #6B757D (4.7:1), input borders and toggle "off" #8A9592 (3.1:1, WCAG 1.4.11); buttons/chips/filter inputs keep #CDD5D3.
- DT-138 Dates are typed in the user's date format (FR-178, default YYYY-MM-DD) with a calendar button, instead of the browser's date input, whose display follows the browser locale.
- DT-139 No server prerendering of the WebAssembly UI (data only through the API; prerendering would need server-side API calls with the user's cookie).
- DT-140 Component gallery `/design` in development builds only (for visual comparison with the board and the step 9 accessibility check).
- DT-141 Menu permissions per entry (`NavModel`) follow the PRD permission of the screen's main read endpoint; master-data lists are shown to every member (their read endpoints are member-level); step 8 verifies each against the endpoints.
