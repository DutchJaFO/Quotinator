# #263 — Bare-bones startup UX: degraded error layout, permanent Statistics page

**Status:** Released
**GitHub issue:** #263
**Tiers required:** T1, T2
**Depends on:** none (builds on #254's `DatabaseHealthState`/`DatabaseHealthGateMiddleware` and #251's
`IFileResourceRepository`/`IImportBatchRepository`, both already shipped)

---

## Spec requirements

1. A degraded error experience exists in the Blazor UI itself — before this issue the entire Blazor UI
   was unreachable during a degraded startup (`DatabaseHealthGateMiddleware` returned a bare JSON `503`
   for every non-exempt route, including `/`, `/rest-api`, `/about`).
2. The error experience shows database status, per-file import history, and totals across categories,
   then leads into a persistent degraded layout: Home disabled, REST API labelled as limited, About
   unaffected.
3. Database status (counts, per-file import history) is reachable at any time via a permanent
   Statistics page — see "Design change" below for why this replaced the issue's original one-time
   success-summary-interstitial framing.
4. A one-time startup-success notification still exists per the original ask, but as a modal overlay
   on Home rather than a page-replacing branch — see "Design change."
5. Scoped as "bare bones" — the fuller ongoing notification concept (import warnings, "what's new"
   after upgrade) is #81's job (Notification System milestone), explicitly out of scope here.

---

## Design change (mid-implementation, 2026-08-08): one-time success interstitial → Statistics page + modal popup

The issue's original body asked for a one-time success summary at `/` (shown once per app run, with a
Continue button through to normal Home) mirroring the error card, implemented as a third render branch
of Home. This hit three consecutive Blazor Server timing bugs live in Docker/T1 (see Notes) — each one
a genuine correctness problem in the "show once, then remember it was dismissed" mechanic itself, not
the surrounding feature. The developer's own live feedback (having Home revisit the summary
unexpectedly, and directly proposing a modal/dialog as the architecturally cleaner alternative to an
interstitial that overrides which branch of Home renders) led to two changes:

- Home renders exactly two *page* states — the degraded error card (unhealthy) or the normal
  `QuoteCard` content (healthy) — with no third "first time you've seen this" branch. This is what Home
  looked like before #263 touched it at all, on the healthy path.
- The counts/per-file-history content is a permanent, always-reachable **Statistics** page (`/stats`),
  linked from the nav like About, so it's never gated behind a one-time interstitial at all.
- The one-time "startup just completed" notification the developer explicitly still wanted is a
  **modal overlay** (`StartupSuccessModal`) rendered inside Home's healthy branch instead of a
  page-replacing branch. A modal never changes which top-level content Home renders underneath it, so
  none of Bugs 1–3 apply: it needs no `EventCallback` up to a parent (it's self-contained, injecting
  `StartupUxState` and dismissing itself), and it needs no dismiss-on-navigate-away logic (it only
  hides via its own explicit Close/Continue button) — the two mechanics that caused all three bugs.

---

## Background — why this issue exists

#254 built a first-pass safety net for critical database startup failures: `DatabaseHealthState`
(`src/Quotinator.Api/Startup/DatabaseHealthState.cs`, in-memory `IsHealthy`/`FailureReason`) and
`DatabaseHealthGateMiddleware` (`src/Quotinator.Api/Middleware/DatabaseHealthGateMiddleware.cs`), which
returns a raw JSON `503` for everything except health/version/admin/OpenAPI/Scalar/static-asset routes.
This protects the REST API, but the entire Blazor UI was unreachable during a degraded startup — a user
opening the app (or the HA add-on's own web UI) saw a bare JSON error, not a page.

**Confirmed before starting** (per this project's standing rule):
- `DatabaseHealthGateMiddleware`'s exempt-path list was `["/api/v1/health", "/api/v1/version",
  "/api/v1/admin", "/openapi", "/scalar", "/_framework/", "/_content/", "/lib/"]` — Blazor's own page
  routes and its SignalR circuit endpoint (`/_blazor`) were **not** exempt.
- `IDatabaseInitializer` (`src/Quotinator.Data/Database/IDatabaseInitializer.cs`) already exposes every
  count needed (`QuoteCount`/`SourceCount`/`CharacterCount`/`PeopleCount`/`SeriesCount`/`UniverseCount`/
  `StageDirectionCount`/`SoundCueCount`/`ConversationCount`) as plain properties on an injectable
  singleton. On a failed startup, #254's backup/restore safety net leaves the database at its last
  known-good state, so these counts remain meaningful even when degraded.
- `IFileResourceRepository`/`IImportBatchRepository` (#251, `src/Quotinator.Data/Repositories/`) persist
  per-file provenance (filename, origin, source URL, first/last-seen timestamps) independently of any
  single process's own startup — this became the actual data source for the Statistics page (see
  Notes — `IDatabaseInitializer.LastSeedReport` was tried first and found unsuitable).
- No Razor component testing framework exists in this project — `Home`/`RestApi`/`About` have no
  dedicated test files, and no `bunit`-equivalent package reference exists anywhere in `tests/`. The
  Blazor-specific pieces of this issue are verified live (T1/T2), not via a component-test harness.

**Developer decisions confirmed during planning (2026-08-08):**
- Error layout's Home nav link: **disabled/greyed out**, not removed or degraded-but-clickable.
- `DatabaseHealthGateMiddleware`'s exempt list extended to let Blazor's own page/circuit routes through
  when unhealthy — there is no way to render any degraded Blazor content without this. REST data
  endpoints (`/api/v1/quotes/*` etc.) stay gated exactly as today; only the page-serving routes change.

---

## Approach (final)

1. **Middleware** — `DatabaseHealthGateMiddleware`'s exempt list gains `/rest-api`, `/about`, `/stats`,
   and `/_blazor` alongside the existing REST/static exemptions. Everything else stays gated.
2. **`Home.razor`/`.razor.cs`** — two-way branch only: `<StartupErrorCard>` when unhealthy, the
   pre-existing `QuoteCard` content otherwise. Carries `@rendermode InteractiveServer` at the page level
   (see Notes for why this has to live on the page, not the individual child components).
3. **`StartupErrorModal.razor`** (new, `Components/Controls/`) — failure reason, last known-good
   database status via `DatabaseStatsSummary`, and a Continue button through to `/rest-api`. Rendered
   as a modal overlay (same shell as `StartupSuccessModal`) for visual consistency between the two —
   unlike the success modal, it has no dismiss state of its own; it shows for as long as `Home` renders
   it (i.e. for as long as the database is unhealthy).
4. **`DatabaseStatsSummary.razor`** (new, `Components/Controls/`) — the counts `<dl>` (all nine
   categories) and per-file import-history `<table>`, shared by `StartupErrorCard` and `Stats.razor`.
   Reads `IFileResourceRepository`/`IImportBatchRepository` directly (durable, survives restarts) rather
   than `IDatabaseInitializer.LastSeedReport` (process-lifetime only — see Notes).
5. **`Stats.razor`** (new, `Components/Pages/`) — permanent `@page "/stats"`, embeds
   `DatabaseStatsSummary`, renders unconditionally (no health branching of its own — the underlying data
   is already meaningful in both states).
6. **`NavMenu.razor`/`.razor.cs`/`.razor.css`** — health-aware: Home's link renders disabled (no
   `href`) while unhealthy; REST API gets a "Limited" badge. Statistics and About are always enabled in
   every state — Statistics is the same kind of always-useful diagnostic content as About, not a
   normal-operation page like Home that genuinely can't function without a working database. A
   `.nav-link.disabled` CSS rule was added (see Notes — none existed before, so the disabled `<span>`
   was visually indistinguishable from an active link).
7. **`RestApi.razor`** — one-line conditional banner when unhealthy, above the existing content.
8. **`StartupUxState`** (`src/Quotinator.Api/Startup/`) — a small DI-registered singleton tracking only
   whether the startup-success modal has been dismissed yet this process run (`bool SummaryDismissed`,
   `void Dismiss()`).
9. **`StartupSuccessModal.razor`** (new, `Components/Controls/`) — self-contained modal overlay, embeds
   `DatabaseStatsSummary`, dismisses itself via `StartupUxState.Dismiss()`. Rendered inside `Home`'s
   healthy branch alongside `QuoteCard`, not in place of it.
10. **Translation strings** — every new piece of UI text goes through `i18ntext/UI.en-GB.json` +
    `UI.de.json`/`UI.nl.json` in the same commit, no hardcoded strings in the new `.razor` markup.

---

## Out of scope (explicitly, per the "bare bones" framing)

- Any recovery action beyond what already exists (`POST /admin/database/reset`, reachable today via
  `/rest-api`'s admin section when an admin key is configured) — the issue's own body defers "menu
  entries that allow for recovery" as a future feature.
- "What's new after upgrade" / ongoing import-warning notifications — #81's job, not this one's.

---

## Files touched

- `src/Quotinator.Api/Middleware/DatabaseHealthGateMiddleware.cs` — exempt-list extension.
- `src/Quotinator.Api/Components/Pages/Home.razor` + `.razor.cs` — two-way health branch, page-level
  `@rendermode InteractiveServer`.
- `src/Quotinator.Api/Components/Controls/StartupErrorCard.razor` + `.razor.cs` — new.
- `src/Quotinator.Api/Components/Controls/DatabaseStatsSummary.razor` + `.razor.cs` — new; shared
  counts/file-history markup, backed by `IFileResourceRepository`/`IImportBatchRepository`.
- `src/Quotinator.Api/Components/Pages/Stats.razor` + `.razor.cs` — new; permanent `/stats` page.
- `src/Quotinator.Api/Components/Layout/NavMenu.razor` + `.razor.cs` — health-aware nav, plus a
  permanent "Statistics" link (always enabled, like About).
- `src/Quotinator.Api/Components/Pages/RestApi.razor` + `.razor.cs` — degraded-state banner.
- `src/Quotinator.Api/Startup/StartupUxState.cs` — new; DI-registered singleton (plain class, no
  interface — mirrors `DatabaseHealthState`'s own shape).
- `src/Quotinator.Api/Components/Controls/StartupSuccessModal.razor` + `.razor.cs` — new; self-contained
  modal popup shown once per process run on the healthy path.
- `src/Quotinator.Api/Components/Layout/NavMenu.razor.css` — added a `.nav-link.disabled` rule (see
  Notes — none existed before).
- `src/Quotinator.Api/i18ntext/UI.en-GB.json` + `UI.de.json` + `UI.nl.json` — new keys.
- `tests/Quotinator.Api.Tests/Middleware/DatabaseHealthGateMiddlewareTests.cs` — exempt-path coverage
  for the new routes (including the fingerprinted CSS asset shape — see Notes), plus a guard confirming
  REST data endpoints stay gated.
- `tests/Quotinator.Api.Tests/Startup/StartupUxStateTests.cs` — new; `Dismiss()`/`SummaryDismissed`
  behaviour, mirroring `DatabaseHealthStateTests`'s pattern.

---

## Steps

### 1. Write the failing middleware tests (red)
**Status:** ✅ Done

`DatabaseHealthGateMiddlewareTests` — added `/`, `/rest-api`, `/about`, `/stats`, `/_blazor` to the
exempt-path `[DataRow]` matrix, plus a dedicated `Unhealthy_QuotesEndpoint_StaysGated` test guarding
that the new Blazor exemptions don't widen the gate for real REST data endpoints. Verified red via
`git stash` against the pre-fix middleware, then restored and green.

### 2. Implement
**Status:** ✅ Done

### 3. Verify
**Status:** ✅ Done — see Verification checklist and Notes below.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Blazor page/circuit routes and fingerprinted CSS assets stay reachable when unhealthy; REST data endpoints stay gated | Unit test | `DatabaseHealthGateMiddlewareTests` |
| 2 | ✅ | `StartupUxState` dismiss/state behaviour | Unit test | `StartupUxStateTests` |
| 3 | ✅ | No regression | Build + test | `dotnet build --configuration Release` (0/0); `dotnet test --configuration Release` (625/625 passed) |
| 4 | ✅ | Translation completeness holds with new keys | Unit test | `TranslationCompletenessTests` |
| 5 | ✅ | T1 — degraded error card, startup-success modal, Statistics page, and nav all render correctly (and correctly *styled*) in Visual Studio | Live (T1) | Developer's own pass, confirmed 2026-08-08 |
| 6 | ✅ | T2 — fresh startup shows the success modal over `QuoteCard`, dismissible, no reappearance after; a broken schema shows the degraded card (fully styled) with durable per-file history, Home disabled, REST API/About/Statistics reachable, REST data endpoints still 503; admin Reset recovers health and Home; Statistics page correct both healthy and degraded, and survives a container restart | Live (T2) | Docker, `execute-sql.csx` schema-break technique (smoke-tests.md §29; now `docs/automated-testing/`, whose README maps the old section numbers), verified visually via screenshot — documented in Notes |

---

## Notes

**Bug 1 — a static parent cannot receive an `EventCallback` invocation from an interactive child
island.** The first implementation applied `@rendermode="InteractiveServer"` individually to each child
card inside `Home.razor`, while `Home.razor` itself stayed statically rendered — matching the official
per-component interactivity pattern and `QuoteCard`'s own pre-existing usage. Live in Docker, clicking a
card's Continue button produced no visible effect and no server-side log activity at all, across
repeated clean-state tests (fresh container, fresh browser origin, `WebSocket.prototype.send` patched to
rule out even a suppressed client-side send). Root cause: a card whose button needs to change which
branch its *parent* renders has no way to do that when the parent itself was never made part of a live
circuit — `QuoteCard`'s button works because it only re-renders itself. Fixed by moving
`@rendermode InteractiveServer` onto `Home` itself (page-level directive) so the whole page shares one
circuit.

**Bug 2 — Blazor's prerender-then-reconnect handoff disposes a throwaway static instance before the
real interactive one runs.** An attempt to dismiss a one-time summary from a component's `Dispose()`
(so navigating away counted the same as clicking Continue) caused a fresh container's very first `/`
load to skip the summary entirely — the prerendered instance's own normal teardown looked identical to
a genuine navigate-away. This was masked at first because the standing DI fix ultimately made this whole
mechanic unnecessary (see Design change above); documented here since the underlying Blazor behaviour
(prerendering skips `OnAfterRender`, so gating dismiss-style logic behind an `OnAfterRender`-set flag is
the correct fix if this pattern is ever needed again elsewhere) is a real, reusable lesson.

**Bug 3 — a stale browser tab across a `docker restart` can trigger Blazor's own automatic
reconnect-and-reload before a human ever sees a page.** Testing the same dismiss-on-navigate mechanic
across a container restart (not just a fresh container) showed the one-time summary already dismissed
on the very first request of the new process. Root cause: a tab left open on Home from before the
restart auto-reconnects and silently reloads via `blazor.web.js`'s own failed-reconnect recovery; that
reload's Home instance then gets disposed a moment later by whatever the test script does next,
satisfying "navigated away from Home" from the framework's perspective before any person actually read
the page. This — combined with Bug 2 and the fact that navigating away and back without ever clicking
Continue was surprising on its own (the very first live observation that started this whole
investigation) — is what led to dropping the one-time interstitial entirely rather than continuing to
patch timing edge cases in it (see Design change above).

**Real bug found in the delivered feature: `manifest.json` misattributed another file's source URL and
record count.** `manifest.json` is captured and linked to *every* seed batch it drives (confirmed by
smoke-tests.md §30's own `BatchLinks = 4` check; now `docs/automated-testing/`, whose README maps the
old section numbers), not just one. Naively picking "the most recently
linked batch" for a file's displayed Source/Records attributed vilaboim's URL and record count to the
manifest row, which is wrong — the manifest isn't sourced from that URL at all. Fixed in
`DatabaseStatsSummary.razor.cs` by only showing Source/Records when every one of a file's linked batches
agree on `Url` (picking the most recent among them when they do); otherwise showing neither, since no
single batch can honestly represent that file's provenance. Verified live: `manifest.json` now shows a
blank Source/Records cell while the four real quote-source files show correct, distinct values.

**Missing counts found in the delivered feature:** the first version of `DatabaseStatsSummary` only
showed 6 of `IDatabaseInitializer`'s 9 available counts (Stage Directions, Sound Cues, and Conversations
were omitted, despite being logged by `StartupSummaryLogger` all along). Fixed by adding all three, with
matching translation keys in all three locale files.

**Real bug found via the developer's own T1 screenshot, missed entirely by T2: the degraded error card
rendered completely unstyled** — a huge unscaled logo, no sidebar, no card styling, plain stacked text.
Root cause: `App.razor` references `app.css` and `Quotinator.Api.styles.css` through ASP.NET Core's Map
Static Assets fingerprinting (`@Assets["app.css"]`), which bakes a build-specific content hash into the
*served filename itself* (confirmed live: `app.khy4lop6wu.css`, `Quotinator.Api.ngd3z69k33.styles.css`)
— the middleware's exempt list had hardcoded the literal, unfingerprinted names, so neither stylesheet
request ever matched and both were silently 503'd during degraded startup. `favicon.png` and everything
under `/lib/`, `/_framework/`, `/_content/` were unaffected — those either aren't fingerprinted
(`favicon.png`, referenced as a plain `href` not `@Assets[...]`) or keep their directory structure with
only the filename hashed, so the existing directory-prefix exemptions still matched. Fixed by replacing
the two literal-name exemptions with a shape match (`StartsWith("/app.") && EndsWith(".css")`,
`StartsWith("/Quotinator.Api.") && EndsWith(".styles.css")`) that tolerates any hash. **This bug existed
throughout every T2 pass in this session and was never caught** — T2 verification relied entirely on
`get_page_text`/`read_page` (DOM/text extraction), which reads correctly regardless of whether CSS
loaded at all. A real `computer{action:"screenshot"}` check, taken only after the developer's own T1
screenshot surfaced the problem, caught it immediately. Any future Blazor/UI-facing change in this
project should include at least one actual screenshot in T2, not just text-content checks.

**Startup-success modal re-added after the developer clarified the earlier "drop it" decision meant
replacing the interstitial's *mechanism*, not dropping the notification itself.** See "Design change"
above — `StartupUxState` (dismiss-once-per-process-run state) and `StartupSuccessModal` (a Bootstrap
modal rendered via plain conditional markup, `.modal.show.d-block` classes, no JS Modal API needed since
Home is already fully interactive) were added back. Verified live via screenshot: modal renders over a
dimmed, fully-styled Home on first load; clicking Continue or the close button dismisses it and reveals
`QuoteCard` beneath with no further action; the degraded error card is unaffected (the modal only
renders in Home's healthy branch).

**Full T2 sequence exercised, in order (final design):** clean container → startup-success modal shows
over a fully-styled Home, dismisses via Continue, `QuoteCard`/"New quote" work underneath → Statistics
page shows correct counts/file history including the `manifest.json` fix → **container restarted**
(same data, no reseed) → Statistics page still shows the full, correct file history (proving the
durability the process-lifetime `LastSeedReport` approach lacked) → stopped container, broke schema via
`execute-sql.csx` (`DROP TABLE Quotinator_Quote`), restarted → backup/restore/degraded sequence logged
exactly per #254's own established behaviour → degraded error card renders at `/`, **fully styled**,
with last-known-good counts and the same durable file history → Home nav renders as a disabled `<span>`,
REST API nav carries the "Limited" badge, Statistics/About unaffected → clicking the error card's
Continue navigates to `/rest-api` and shows the degraded banner, fully styled → `/api/v1/health` and
`/api/v1/quotes/random` both `503` throughout → admin `POST /admin/database/reset` returns `200` with
all-zero counts (per #156, Reset no longer reimports bundled content) → `/api/v1/health` flips to `200`.

**Two more real gaps found via the developer's own screenshots, both fixed:**
- **The disabled Home nav link was visually indistinguishable from an active one.** `NavMenu.razor`
  applies the `disabled` CSS class to Home's `<span>` while unhealthy, but `NavMenu.razor.css` had no
  rule for `.nav-link.disabled` at all — the shared `.nav-link` rule's `color: #d7d7d7` applied equally
  to every nav item regardless of state, so "disabled" only meant "not clickable," never "looks
  disabled." Fixed by adding a dedicated `.nav-link.disabled` rule (dimmed text colour, `cursor:
  not-allowed`).
- **The error card was visually inconsistent with the new success modal.** After the modal popup was
  added for the healthy path, the developer asked why the failure path still used an inline card rather
  than the same modal treatment. Converted `StartupErrorCard` to `StartupErrorModal` — same
  `.modal.show.d-block` shell as `StartupSuccessModal`, same red/danger accent it already had. Unlike
  the success modal, it has no independent dismiss state (no `StartupUxState`-equivalent tracking) —
  it's simply rendered by `Home` for as long as the database is unhealthy, so it reappears on every
  visit exactly as the card version did.

Both verified live via screenshot: the error modal now renders with the identical shell/backdrop as the
success modal (only the accent colour and content differ), and the disabled Home link now visibly reads
as dimmed/unclickable next to the other three nav items.

**Two final polish items from developer feedback on the screenshots above:**
- Both modal titles now show the running app version (`v@(VersionService.Version)`), matching the
  badge style already used on the About page — `IVersionService` injected into both
  `StartupSuccessModal` and `StartupErrorModal`.
- The modals' width was a fixed Bootstrap size class (`modal-lg`, 800px, then `modal-xl`, 1140px) —
  both cap out well short of a wide desktop window, leaving large empty margins either side (visible in
  the developer's own 1718px-wide screenshot). Bootstrap 5 has no built-in viewport-relative width
  utility (`w-*` classes are percentage-of-parent, not viewport), so both modals now set
  `style="max-width: 80vw;"` directly on `.modal-dialog` instead of a size class — confirmed live via
  `getBoundingClientRect()` that the rendered width tracks the viewport exactly (1360px at a 1700px
  viewport).

**Not implemented in this issue, flagged separately:** the developer noted mid-session that Quotinator
vendors Bootstrap locally (`wwwroot/lib/bootstrap/`, confirmed v5.3.3 via the CSS file's own header
comment) with no documented version anywhere in the project's docs. Spawned as a standalone follow-up
task rather than folded into #263 — checking for a newer release and updating docs is unrelated to this
issue's own scope.
