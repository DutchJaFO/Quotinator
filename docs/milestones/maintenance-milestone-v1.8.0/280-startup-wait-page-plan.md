# #280 — Show a startup "please wait" page while the database is created/updated/seeded, with progress if feasible

**Status:** Released
**GitHub issue:** #280
**Tiers required:** T1, T2
**Depends on:** #278 (must land first so its status-surface infrastructure exists — done)

---

## Background

Found live during #269's own T1 verification: while database initialisation (migration, backup,
source refresh, seeding) takes noticeable time on a fresh install or a large re-seed, a user opening
the app in a browser sees nothing at all — no response — until `[Server] listening on ...` is logged.
Confirmed against the actual code: `Program.cs` calls `await dbInitializer.InitialiseAsync()`
**before** any middleware registration and before `app.Run()` — Kestrel does not even bind a port until
initialisation is fully complete. The only visible progress today is the console/`docker logs` output,
which the project's primary audience (HA add-on / homelab users) does not typically have open.

**Item 1's architectural question, confirmed (2026-08-10):** showing a wait page requires Kestrel to be
listening *during* initialisation, with every request gated to a "starting up" response until it
completes. The standard ASP.NET Core pattern for this is splitting `app.Run()` into
`await app.StartAsync()` (binds Kestrel, starts accepting connections, returns immediately) →
application-specific async startup work → `await app.WaitForShutdownAsync()` (blocks until shutdown,
replacing what `Run()` did internally). **Developer confirmed via AskUserQuestion (2026-08-10) to
proceed with this restructure** despite it touching top-level startup sequencing that every request —
and every `WebApplicationFactory`-based test — goes through.

**Spiked and empirically verified before committing to this design (2026-08-10):** changed only
`Program.cs`'s tail (`app.Run()` → `await app.StartAsync(); await app.WaitForShutdownAsync();`, no
other change) and ran the full `Quotinator.Api.Tests` suite: all 664 tests still pass.
`WebApplicationFactory<Program>` is fully compatible with this split — it does not depend on `Run()`
specifically. This de-risks the rest of the restructure before building on top of it.

**Item 5, confirmed via AskUserQuestion (2026-08-10):** `GET /api/v1/health`/`/version` report a new,
distinct `"starting"` state during the pre-ready window (developer chose this over reusing the existing
`503 unhealthy` response) — this lets a caller (monitoring, the HA supervisor, a load balancer)
distinguish "still booting, wait" from "genuinely unhealthy, needs a Reset," which is what the existing
unhealthy response already means and would otherwise be conflated with. `/health` returns `503` (not
`200`) during this window so automated readiness probes still treat it as not-yet-ready, but with
`{"status":"starting"}` instead of `{"status":"unhealthy","reason":...}` so the two are distinguishable
by status text, not just by the human reading logs. `/version` returns `{"status":"starting","version":...}`
with the `database` stats object omitted (those counts don't exist yet).

**Item 3 (bonus phase display), decided (2026-08-10):** dropped from this issue's scope, per the
issue's own explicit escape hatch ("only if achievable without material added complexity"). Reporting
a granular phase (e.g. "Checking for source updates", "Seeding database") would require a new
cross-layer reporting abstraction threaded from deep inside `Quotinator.Data`/`Quotinator.Core`'s
seeding pipeline up to `Quotinator.Api` — the same shape of change #277's `IDiskSpaceProvider` needed,
but for a purely cosmetic improvement rather than a correctness/safety one. Not worth the added surface
for a homelab project (Simplicity is priority #2). The wait page shows a single static "Quotinator is
starting up..." message; a future issue can revisit granular phase reporting if it turns out to matter
in practice.

## Approach

### New `StartupPhaseState` (Quotinator.Api.Startup, internal — mirrors `DatabaseHealthState`)

```csharp
internal sealed class StartupPhaseState
{
    public bool IsComplete { get; private set; }
    public void MarkComplete() => IsComplete = true;
}
```

Registered as a singleton. Starts `false`; flipped to `true` once `InitialiseAsync()` (success or
failure) and the #279 notification-seeding step have both finished — deliberately independent of
`DatabaseHealthState.IsHealthy`: even a *failed* initialisation stops showing the wait page (the
existing `DatabaseHealthGateMiddleware`/degraded-startup UI already own that state, unchanged).

### New `StartupWaitMiddleware`

Registered **after** `UseRequestLocalization()` (so `IApiLocalizer`/`CultureInfo.CurrentUICulture` are
already resolved from `Accept-Language` when it builds its response) and before `UseRateLimiter()` (a
polling wait page must never burn the caller's rate-limit budget). While `!StartupPhaseState.IsComplete`,
every request except `/api/v1/health` and `/api/v1/version` (exact prefix match, same `IsExempt` shape
as `DatabaseHealthGateMiddleware`) gets a `200` self-contained HTML page (`<meta http-equiv="refresh"
content="2">`, no external assets, no Blazor circuit — matching the existing precedent of the
language-selector's static-SSR form working without one) instead of reaching routing.

### `Program.cs` restructure

```
var app = builder.Build();
... existing startupLog.LogStarting(), DatabaseHealthState resolution, etc. (unchanged position) ...
... all existing app.Use.../app.Map... middleware and endpoint registration (unchanged) ...
app.UseMiddleware<StartupWaitMiddleware>();   // new — positioned per above

await app.StartAsync();                        // was: (nothing here — Run() did this internally later)

try { await dbInitializer.InitialiseAsync(); } catch (...) { ... unchanged catch blocks ... }
... existing #279 NotificationSeeding block (unchanged) ...
startupPhase.MarkComplete();

var addresses = (app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()?.Addresses ?? []).ToList();
startupLog.LogReady(addresses);                 // moved out of the ApplicationStarted event hook —
                                                 // "ready" now means "truly ready," not just "Kestrel bound"

await app.WaitForShutdownAsync();               // was: app.Run()
```

The existing `lifetime.ApplicationStarted.Register(...)` hook that logged the ready banner is removed —
`ApplicationStarted` now fires as soon as `StartAsync()` returns, which is *before* initialisation even
begins under the new model, so logging "ready" there would be actively wrong. The ready banner is
logged directly, once, right after `MarkComplete()`.

### `GET /api/v1/health` / `/version`

Both handlers gain a `StartupPhaseState startupPhase` parameter and check `!startupPhase.IsComplete`
first, before their existing `DatabaseHealthState`-based logic:
- `/health`: `503` `{"status":"starting"}`.
- `/version`: `200` `{"status":"starting","version":vs.Version}` (no `database` object).

### Localisation

New `i18ntext/UI.*.json` keys for the wait page's heading/body text (e.g.
`StartupWaitHeading`/`StartupWaitBody`), added to all three locale files in the same commit per the
project's localisation policy. Rendered via `IApiLocalizer` inside `StartupWaitMiddleware` (a plain
middleware, not a Blazor component — the same server-side localisation path `ApiMessages` already uses,
not `II18nText`).

---

## Steps

### 1. Spike: confirm `StartAsync`/`WaitForShutdownAsync` is compatible with `WebApplicationFactory`
**Status:** ✅ Done — all 664 existing Api.Tests pass unchanged with the tail swapped

### 2. `StartupPhaseState` + `StartupWaitMiddleware`
**Status:** ✅ Done

### 3. `Program.cs` restructure (`StartAsync`/init/`MarkComplete`/`WaitForShutdownAsync`, ready-banner move)
**Status:** ✅ Done

### 4. `/health`/`/version` "starting" state
**Status:** ✅ Done — `/version`'s normal (post-startup) response also gained a `status: "ready"`
field for consistency with the "starting" shape; purely additive, no existing field removed or
renamed.

### 5. Localisation (`i18ntext/UI.*.json`, all 3 locales)
**Status:** ✅ Done — `StartupWaitHeading`/`StartupWaitBody` added to `ApiMessages.cs` (its class
summary widened from "error messages" to "API-surface text," since this is the first non-error use)
and all three locale files.

### 6. Full verification (T1, T2)
**Status:** ✅ Done

**T1 confirmed (2026-08-10):** developer's own Visual Studio run against a real reseed — observed
the browser tab title flicker (the wait page mounting and unmounting as `StartupPhaseState.IsComplete`
flips) during the reseed window, directly confirming the mechanism fires correctly outside Docker too,
not only in the isolated T2 environment.

**T2 confirmed (2026-08-10):** `docker build` succeeded; live against a real container with a
persistent volume (fresh, unseeded):
- Immediately after start: `GET /` returned the self-contained wait page (200, correct localized
  English text, auto-refresh meta tag); `GET /api/v1/health` returned `503 {"status":"starting"}`;
  `GET /api/v1/version` returned `200 {"status":"starting","version":"1.8.2"}` with no
  `environment`/`database` fields.
- After seeding completed: `/health` returned `200 {"status":"healthy"}`; `/version` returned the
  full `{"status":"ready", ..., "database": {...}}` shape with real counts (799 quotes, etc.).
- Log ordering: `Microsoft.Hosting.Lifetime`'s own `Now listening on` line (framework-level, proves
  Kestrel genuinely bound and started accepting connections) appeared at `05:42:02`; the app's own
  `[Server] listening on ...`/`Quotinator ready` banner appeared at `05:42:22` — 20 seconds later,
  confirming Kestrel was live and serving the wait page for the entire initialisation window, not
  just reachable after the fact.

---

## Verification

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `StartAsync`/`WaitForShutdownAsync` split is compatible with `WebApplicationFactory` | Unit test | Full `Quotinator.Api.Tests` suite green against the spiked tail (664/664) |
| 2 | ✅ | A request during initialisation receives the wait page, not a hang or error | Unit test + Live | `StartupWaitMiddlewareTests.Invoke_InitialisationInProgress_ServesWaitPage`; Docker: `GET /` returned the wait page immediately after container start |
| 3 | ✅ | A request after initialisation completes passes through normally | Unit test | `StartupWaitMiddlewareTests.Invoke_InitialisationComplete_PassesThroughToNextMiddleware` |
| 4 | ✅ | `/health` and `/version` are exempt from the wait gate and report a distinct `"starting"` state | Unit test + Live | `StartupWaitMiddlewareTests.Invoke_HealthEndpoint_ExemptFromWaitGate` (both routes); Docker: `503 {"status":"starting"}` / `200 {"status":"starting","version":...}` confirmed live. No `WebApplicationFactory`-level endpoint test — `Program.cs`'s own startup sequence runs to completion (`MarkComplete()` fires) before `CreateClient()` returns a usable client, so the transient state isn't observable that way; the middleware-level unit test plus the live Docker check cover it instead. |
| 5 | ✅ | The ready banner logs only once initialisation is truly complete, not merely once Kestrel is bound | Live | Docker log ordering confirmed: `Microsoft.Hosting.Lifetime`'s own `Now listening on` at 05:42:02, app's own `ready` banner at 05:42:22 — 20s later |
| 6 | ✅ | Wait page text is fully localised, no hardcoded English | Test + Live | `TranslationCompletenessTests` covers the new keys in all 3 locales (green); live Docker check confirmed the English render resolves correctly via `IApiLocalizer` |
| 7 | ✅ | Full build clean | Build | `dotnet build --configuration Release` — 0 Warning(s), 0 Error(s) |
| 8 | ✅ | Full test suite green | Build | `dotnet test --configuration Release` — 1074 Data.Tests + 1445 Core.Tests + 668 Api.Tests, 0 failures |
| 9 | ✅ | T1 (developer's own Visual Studio run) | Live | Observed the browser tab title flicker during a real reseed — the wait page mounting/unmounting live |
| 10 | ✅ | T2 (Docker smoke tests) | Live | 2026-08-10 — see Step 6 for the full scenario list |

---

## Relationship to existing issues

- **#278** — must land first so `StartupPhaseState`'s sibling `DatabaseHealthState`/notification
  infrastructure exists as precedent; done.
- **#269** — this issue was discovered live during #269's own T1 verification.
- **#263** — built `DatabaseHealthGateMiddleware`/`DatabaseHealthState`, the direct precedent
  `StartupWaitMiddleware`/`StartupPhaseState` mirrors.
