# #263 — Bare-bones startup UX: success summary page, degraded error layout

**Status:** Planning
**GitHub issue:** #263
**Tiers required:** T1, T2
**Depends on:** none (builds on #254's `DatabaseHealthState`/`DatabaseHealthGateMiddleware` and #251's
`IFileResourceRepository`/`IImportBatchRepository`, both already shipped)

---

## Spec requirements

1. A degraded error experience exists in the Blazor UI itself — today the entire Blazor UI is
   unreachable during a degraded startup (`DatabaseHealthGateMiddleware` returns a bare JSON `503` for
   every non-exempt route, including `/`, `/rest-api`, `/about`).
2. The error experience shows database status, bundled/user file info, and totals across categories,
   then leads into a persistent degraded layout: Home disabled, REST API labelled as limited, About
   unaffected.
3. A success startup summary shows the same kind of status (database status, bundled/user files,
   totals), once per app run, with a button through to the normal app.
4. Scoped as "bare bones" — the fuller ongoing notification concept (import warnings, "what's new"
   after upgrade) is #81's job (Notification System milestone), explicitly out of scope here.

---

## Background — why this issue exists

#254 built a first-pass safety net for critical database startup failures: `DatabaseHealthState`
(`src/Quotinator.Api/Startup/DatabaseHealthState.cs`, in-memory `IsHealthy`/`FailureReason`) and
`DatabaseHealthGateMiddleware` (`src/Quotinator.Api/Middleware/DatabaseHealthGateMiddleware.cs`), which
returns a raw JSON `503` for everything except health/version/admin/OpenAPI/Scalar/static-asset routes.
This protects the REST API, but the entire Blazor UI is currently unreachable during a degraded
startup — a user opening the app (or the HA add-on's own web UI) sees a bare JSON error, not a page.

**Confirmed before starting** (per this project's standing rule):
- `DatabaseHealthGateMiddleware`'s exempt-path list is `["/api/v1/health", "/api/v1/version",
  "/api/v1/admin", "/openapi", "/scalar", "/_framework/", "/_content/", "/lib/"]` — Blazor's own page
  routes (`/`, `/rest-api`, `/about`) and its SignalR circuit endpoint (`/_blazor`) are **not** exempt.
- Nothing in Razor currently references `DatabaseHealthState` — it is purely middleware/endpoint-side
  today (`Program.cs`, `DatabaseHealthGateMiddleware.cs`, and `AdminEndpoints.cs`'s reset endpoint,
  which calls `MarkHealthy()`).
- `IDatabaseInitializer` (`src/Quotinator.Data/Database/IDatabaseInitializer.cs`) already exposes every
  count needed (`QuoteCount`/`SourceCount`/`CharacterCount`/`PeopleCount`/`SeriesCount`/`UniverseCount`/
  `StageDirectionCount`/`SoundCueCount`/`ConversationCount`/`SchemaVersion`/`DataSchemaVersion`/
  `MigrationApplied`/`LastSeedReport`) as plain properties on an injectable singleton — the same data
  `StartupSummaryLogger` already logs. On a failed startup, #254's backup/restore safety net leaves the
  database at its last known-good state (the pre-seed backup), so these counts remain meaningful even
  when degraded.
- `IFileResourceRepository`/`IImportBatchRepository` (#251, `src/Quotinator.Data/Repositories/`) are
  already injectable and filterable by `FileResourceOrigin`/`ImportBatchType`/`ImportBatchStatus` — no
  new backend query is needed for "list bundled files, user files."
- No Razor component testing framework exists in this project today — `Home`/`RestApi`/`About` have no
  dedicated test files, and no `bunit`-equivalent package reference exists anywhere in `tests/`.

**Developer decisions confirmed during planning (2026-08-08):**
- Success summary shows on **every** startup (not just first-ever) — simplest, no seen-state tracking.
  Shown once per app process lifetime; dismissing it moves to normal Home for the rest of that run.
- Error layout's Home nav link: **disabled/greyed out**, not removed or degraded-but-clickable.
- `DatabaseHealthGateMiddleware`'s exempt list must be extended to let Blazor's own page/circuit routes
  through when unhealthy — there is no way to render any degraded Blazor content without this. REST
  data endpoints (`/api/v1/quotes/*` etc.) stay gated exactly as today; only the page-serving routes
  change.

---

## Approach

1. **Middleware** — extend `DatabaseHealthGateMiddleware`'s exempt list with Blazor's page/circuit
   routes (`/`, `/rest-api`, `/about`, `/_blazor`) alongside the existing REST/static exemptions.
   Everything else stays gated exactly as today.
2. **`IStartupUxState`** (new, `src/Quotinator.Api/Startup/`) — a small DI-registered singleton
   tracking only whether the success summary has been dismissed yet this process run
   (`bool SummaryDismissed`, `void Dismiss()`). Deliberately separate from `DatabaseHealthState` — one
   tracks health (can flip back after a Reset), the other tracks a one-time interstitial (never resets
   during a process run).
3. **`Home.razor`/`.razor.cs` becomes the single state-aware entry point** — no new routes. Injects
   `DatabaseHealthState`, `IStartupUxState`, `IDatabaseInitializer` and branches: unhealthy → degraded
   summary content; healthy and not yet dismissed → success summary content; otherwise → today's normal
   `QuoteCard` content, unchanged. Two new components under `Components/Controls/`
   (`StartupSummaryCard.razor`, `StartupErrorCard.razor`) hold the actual markup for each state.
4. **`NavMenu.razor`/`.razor.cs` becomes health-aware** — injects `DatabaseHealthState`. When unhealthy:
   Home's `NavLink` renders disabled (no `href`, greyed CSS class); REST API's `NavLink` gets a small
   "limited" badge. About is untouched in every state. No separate `ErrorLayout.razor` — one
   `MainLayout`/`NavMenu` pair, conditionally styled.
5. **`RestApi.razor`** gets a one-line conditional banner (injects `DatabaseHealthState`) when
   unhealthy: "limited functionality — database unavailable," above the existing content (which already
   links `/api/v1/health`/`/api/v1/version`, both reachable).
6. **Translation strings** — every new piece of UI text goes through `i18ntext/UI.en-GB.json` +
   `UI.de.json`/`UI.nl.json` in the same commit, no hardcoded strings in the new `.razor` markup.

---

## Out of scope (explicitly, per the "bare bones" framing)

- Any recovery action beyond what already exists (`POST /admin/database/reset`, reachable today via
  `/rest-api`'s admin section when an admin key is configured) — the issue's own body defers "menu
  entries that allow for recovery" as a future feature.
- "What's new after upgrade" / ongoing import-warning notifications — #81's job, not this one's.
- Per-user/per-browser dismissal persistence (cookie/localStorage) — a single process-wide flag is
  enough for "every startup, once per run"; no per-session tracking needed for bare bones.

---

## Files touched

- `src/Quotinator.Api/Middleware/DatabaseHealthGateMiddleware.cs` — exempt-list extension.
- `src/Quotinator.Api/Startup/IStartupUxState.cs` + implementation — new, DI-registered.
- `src/Quotinator.Api/Components/Pages/Home.razor` + `.razor.cs` — state branching.
- `src/Quotinator.Api/Components/Controls/StartupSummaryCard.razor` + `.razor.cs` — new.
- `src/Quotinator.Api/Components/Controls/StartupErrorCard.razor` + `.razor.cs` — new.
- `src/Quotinator.Api/Components/Layout/NavMenu.razor` + `.razor.cs` — health-aware nav.
- `src/Quotinator.Api/Components/Pages/RestApi.razor` + `.razor.cs` — degraded-state banner.
- `src/Quotinator.Api/i18ntext/UI.en-GB.json` + `UI.de.json` + `UI.nl.json` — new keys.
- `Program.cs` — DI registration for `IStartupUxState`.

---

## Steps

### 1. Write the failing middleware/state tests (red)
**Status:** ⬜ Not started

- `DatabaseHealthGateMiddlewareTests` — confirm `/`, `/rest-api`, `/about`, `/_blazor` are reachable
  (not gated) when unhealthy, while `/api/v1/quotes/random` still returns `503`.
- `IStartupUxStateTests` — `Dismiss()`/`SummaryDismissed` behaviour.

### 2. Implement the fix
**Status:** ⬜ Not started

### 3. Verify
**Status:** ⬜ Not started

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ⬜ | Blazor page/circuit routes stay reachable when unhealthy; REST data endpoints stay gated | Unit test | `DatabaseHealthGateMiddlewareTests` |
| 2 | ⬜ | `IStartupUxState` dismiss/state behaviour | Unit test | `IStartupUxStateTests` |
| 3 | ⬜ | No regression | Build + test | `dotnet build --configuration Release`; `dotnet test --configuration Release` |
| 4 | ⬜ | Translation completeness holds with new keys | Unit test | `TranslationCompletenessTests` |
| 5 | ⬜ | T1 — success summary, degraded error card, and nav both render correctly in Visual Studio | Live (T1) | Developer's own pass |
| 6 | ⬜ | T2 — success summary shows once then Home takes over; a broken schema shows the degraded card, Home disabled, REST API/About reachable, REST data endpoints still 503 | Live (T2) | Docker, `execute-sql.csx` schema-break technique (smoke-tests.md §29) |

---

## Notes

None yet — implementation has not started.
