# #419: A reset in the first moments after startup races the what's-new notification's write

**Status:** Planning
**GitHub issue:** #419
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

**Step 4: the live document.** Steps 1 to 3 are done and the full solution is green across two
consecutive `-m:1` runs. What remains is the T2 document, the Knowledgebase entry, and the close-out.

---

## Description

The issue reports a reset landing between the what's-new notification's version capture and its write,
removing the `System_AppVersion` row the write points at, so the write fails on a foreign key and the
notification is lost for that boot.

That is a symptom. The rule it breaks is the one the developer stated (2026-10-03): **no external write
reaches the database until the application's own startup tasks have finished.** The application already
applies that rule to concurrent admin calls through the concurrency-1 `admin` rate-limit policy, and to
the database's own creation and migration by doing them before serving. Startup's remaining tasks are
simply not inside the same gate.

---

## What the cross-check found

**The issue's described mechanism no longer exists.** It names "a detached task (`Program.cs`, the
`Task.Run` beside `WhatsNewNotification.SeedAsync`)". #424 replaced that with the
`StartupBackgroundWork` hosted service, so the host's shutdown now waits for the work. That addressed
shutdown, not this: the version id is still captured before the work starts, and the write still carries
it as a foreign key.

**The gate already exists, and opens one line too early.** `StartupWaitMiddleware` holds every request
except `/api/v1/health` and `/api/v1/version` while `StartupPhaseState.IsComplete` is false.
`MarkComplete()` is called at `Program.cs:1264`, after `startupBackgroundWork.Start(...)` has launched
the changelog content import and the what's-new write and returned immediately. The gate therefore lifts
while two startup tasks are still running, which is the whole of the reported race.

**Both databases are already inside the gate.** `ChangelogDatabaseInitializer.InitialiseAsync()` is
awaited at `Program.cs:942` and the application database's migrations earlier, both before
`MarkComplete()`. The two steps the developer named, created and migrated, are satisfied for both
databases today. What is outside the gate is only the two background tasks.

**A write during the gate is answered `200 OK` with an HTML wait page.** Measured 2026-10-03 against
`quotinator:local`:

```
POST /api/v1/admin/database/reset?allowNoBackup=true   ->  200 OK
content-type: text/html; charset=utf-8
<!DOCTYPE html> ... <meta http-equiv="refresh" content="2"> ...
```

`StartupWaitMiddleware` sets `Status200OK` for every non-exempt request whatever its method. An API
caller is told its reset succeeded, is handed a web page, and nothing was reset. This is deterministic
inside a startup window of roughly a minute, rather than a race, and it is a worse fault than the one the
issue was filed for.

**The reported race itself did not reproduce here, 0 of 3**, against the reporter's 5 of 5: first `200`
at about 66 s each time, a reset about 1.5 s later, and no foreign-key failure. The window depends on how
long the background work takes relative to `MarkComplete()`, so a machine where the work finishes first
never sees it. The mechanism is unchanged, so this is a narrower window rather than a fixed defect, and
it is why the primary evidence below is a deterministic test rather than a live reproduction.

**A first attempt measured nothing at all.** Windows PowerShell 5.1's `Invoke-WebRequest` throws for
parsing reasons without `-UseBasicParsing`, so the readiness poll never observed its `200`, ran its full
180 s bound, and reset two minutes after the application was ready. Only the suspiciously round "healthy
after 180s" gave it away. The runs above use `HttpClient`.

---

## Decisions

- **The gate holds until every startup task has finished, background ones included** (developer,
  2026-10-03). `MarkComplete()` waits for `StartupBackgroundWork`. One change, at the point the rule is
  actually expressed, rather than a guard inside the what's-new producer: the changelog content import
  is in exactly the same position and gets the same protection without being named.
- **Nothing is reseeded after a reset** (developer, 2026-10-03). An automatic reimport would breach
  `CLAUDE.md`'s endpoint side-effect policy, which is why the fix is a gate rather than a recovery.
- **`GET /api/v1/health` is the readiness contract** (developer, 2026-10-03). It is what an external
  caller polls. No other endpoint is expected to answer meaningfully while health does not report ready.
- **While the gate is closed, every other endpoint answers `503`, never `200`** (developer, 2026-10-03).
  A `200` carrying content the caller did not ask for breaks the caller's expectations whatever its
  method, so this applies to GET as much as to POST. The wait page survives as the *body* a browser
  receives, negotiated on `Accept`, with problem-details JSON for everyone else. A `503` still renders
  its body, so the auto-refreshing page keeps working.
- **`/api/v1/version` is gated too; `/api/v1/health` alone stays exempt** (developer, 2026-10-03). It
  cannot report complete version information or a ready state until startup has finished, and a caller
  could reasonably reach for it as a status endpoint instead of `/health`, which is the misuse this
  gating prevents. It is also the same violation as the wait page in a subtler form: today it answers
  `200` with `{"status":"starting","version":...}`, a structurally different object from the one its own
  description promises, which admits that "the environment/database fields don't exist yet".

  Nothing depends on it answering during startup: every call site in the suite reads `.database.quotes`,
  `.database.schemaVersion` or `.version` after the application is ready, and `test-env.csx` waits on
  `/health`. Gating it therefore removes code rather than adding any. The handler's `"starting"` branch
  becomes unreachable, so that branch, its `StartupPhaseState` parameter and the caveat in its
  `WithDescription` all go, leaving `/version` with one shape.

  `/health` already answers `503` with `{"status":"starting"}`, so the readiness contract itself needs no
  change: this makes `/version` consistent with it rather than an alternative to it.

---

## Known hazard

**Delaying `MarkComplete()` has broken the test suite once before.** `Program.cs:1214` records that
awaiting the changelog document inline "delayed `StartupPhaseState.MarkComplete()` enough to reintroduce
the exact race #309's Step 6 fix already solved once, this time affecting far more of the test suite
since every `WebApplicationFactory`-based test spins up its own full startup sequence".

`QuotinatorWebApplicationFactory` disables source auto-update and the default sources, but **not** the
changelog import or the what's-new write, so every API test host will now wait for both. Step 1 measures
the suite's wall-clock before and after and watches for that race specifically; if it returns, the gate
waits for the work with a bounded timeout rather than unconditionally, and the bound is recorded here
with what it was derived from.

---

## Steps

### 1. Write the tests, and update the startup tests that assert the old behaviour

**Status:** ✅ Done

Each new test runs against the current build before steps 2 and 3 land, and fails for its own reason. This is first deliberately: the two behaviour changes below are what turn it green.

**Existing tests that assert what this issue changes**, all of which move with it rather than being
discovered failing:

| Test | What it asserts today | After |
|---|---|---|
| `StartupWaitMiddlewareTests.Invoke_InitialisationInProgress_ServesWaitPage` | `Status200OK` while initialising | `503`, with the page still its body; split so the status and the body are separate statements |
| `StartupWaitMiddlewareTests.Invoke_HealthEndpoint_ExemptFromWaitGate` | `/api/v1/health` and `/api/v1/version` both exempt | its `/version` row moves to the gated test |
| `StartupBackgroundWorkTests` | only `StopAsync_WaitsForWorkStillRunning` | gains the gate's own case: work still running means startup is not complete |
| `StartupReadinessTests.CreateClient_ReturnsOnlyAfterStartupIsComplete` | a client is handed back only once startup reports complete | unchanged in wording, stronger in fact, since complete now includes the background work. Asserted explicitly rather than left implied |

**Two neighbours that look related and are not**, named here so they are not changed by mistake:

- `VersionEndpointTests` calls `/version` through a client that already waited for startup, so it reads
  the ready shape and is unaffected. It is the negative control for row 7: it is what proves gating the
  endpoint did not reduce the answer it gives once the application is ready.
- `DatabaseHealthGateMiddlewareTests` exempts `/version` from a **different** gate, the one that answers
  while the database is degraded *after* startup. That gate is not this issue's, and `/version` stays
  exempt from it: an application that has started can report its version whatever the database is doing.

### 2. Hold the gate until the background work has finished

**Status:** ✅ Done

`StartupBackgroundWork` gains a way to await everything it started, and `Program.cs` awaits it
immediately before `MarkComplete()`. Measure the full suite's duration before and after, per the hazard
above.

### 3. Answer a gated request with `503` and the body its caller asked for

**Status:** ✅ Done

`StartupWaitMiddleware` stops setting `200`. It sets `503`, adds `Retry-After`, and chooses its body from
the request's `Accept`: the existing HTML wait page for a browser, problem details otherwise, carrying
the same reason. `/api/v1/health` becomes the only exempt path.

`/version`'s own handler loses its now unreachable `"starting"` branch, the `StartupPhaseState` parameter
that selected it, and the sentence in its `WithDescription` describing the reduced shape.

### 4. Extend the live documents

**Status:** ⬜ Not started

A T2 document that posts a write during the startup window and asserts `503` with a JSON body, and asks
for a page and asserts `503` with the HTML one. Red against a build from the commit before step 2, then
green.

`startup-and-degradation/03-startup-wait-page.md` changes with the behaviour: it asserts today that
`/version` returns `200`, `status=starting`, `hasDatabase=False`, and that `/` returns `200`. Both become
`503`. #280's plan doc records the old behaviour as its own history and is not rewritten.

### 5. Resolve the Knowledgebase entry

**Status:** ⬜ Not started

*The what's-new notification could not be seeded, after a reset*
(`docs/knowledgebase/whats-new-notification-fails-to-seed-after-a-reset.md`, `QTN-KNOWN`). The condition
is development-only, so once the gate removes it the entry is **deleted** rather than retired, per
`docs/knowledgebase.md`'s retention rule; its commits are its history.

### 6. Close out

**Status:** ⬜ Not started

Boyscout pass over the touched files, the changelog `unreleased` entry in all three languages, and the
`Waiting for release` checklist.

---

## Verification checklist

**Every claim here is paired.** A test that only proves the refusal passes against a build that refuses
everything, and a test that only proves the success passes against a build that gates nothing: neither
half means anything alone. Where a row is one half of a pair, the other half is named in it.

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | The gate stays closed while a startup task is still running | Unit test | `StartupBackgroundWorkTests.WhenAllCompleted_IsNotCompleted_WhileWorkIsStillRunning`, shown red by mutating the wait to complete immediately |
| 2 | ✅ | The gate opens once that work finishes | Unit test | `StartupBackgroundWorkTests.WhenAllCompleted_Completes_OnceTheWorkHasFinished`, row 1's pair |
| 3 | ✅ | A write during startup is answered `503`, never `200` | Unit test | `StartupWaitMiddlewareTests.NonGetDuringStartup_Answers503`, red against the current middleware before step 3 |
| 4 | ✅ | The same write after startup reaches its handler | Unit test | `StartupWaitMiddlewareTests.NonGetAfterStartup_ReachesTheHandler`, row 3's pair |
| 5 | ✅ | A GET during startup is answered `503`, never `200` | Unit test | `StartupWaitMiddlewareTests.GetDuringStartup_Answers503` |
| 6 | ✅ | The same GET after startup reaches its handler | Unit test | `StartupWaitMiddlewareTests.GetAfterStartup_ReachesTheHandler`, row 5's pair |
| 7 | ✅ | `/api/v1/health` answers during startup | Unit test | `StartupWaitMiddlewareTests.HealthIsNotGated`, green throughout: the control that stops rows 3 and 5 passing against a middleware that gates everything |
| 8 | ✅ | `/api/v1/version` does not answer `200` during startup | Unit test | `StartupWaitMiddlewareTests.VersionIsGatedDuringStartup`, red before step 3 |
| 9 | ✅ | `/api/v1/version` answers `200` with `environment` and `database` once ready | Live (T2) | `startup-and-degradation/03`, step 3: `status=ready`, `hasDatabase=True`, 795 quotes |
| 10 | ✅ | An API caller gets problem details | Unit test | `StartupWaitMiddlewareTests.BodyIsProblemDetails_WhenJsonIsAccepted`, red before step 3 |
| 11 | ✅ | A browser gets the HTML wait page | Unit test | `StartupWaitMiddlewareTests.BodyIsTheWaitPage_WhenHtmlIsAccepted`, row 10's pair |
| 12 | ✅ | A reset arriving while the gate is closed changes nothing | Unit test | `StartupWaitMiddlewareTests.DuringStartup_TheRequestNeverReachesTheHandler`: the handler is the only thing that could change anything, so never reaching it is the claim |
| 13 | ✅ | A reset arriving after the gate opens does reset | Unit test | `StartupWaitMiddlewareTests.NonGetAfterStartup_ReachesTheHandler`, row 12's pair |
| 14 | ✅ | Startup waits for its own work before opening the gate, which is what keeps the what's-new write safe | Unit test | `RepositoryStructureTests.Program_WaitsForTheBackgroundWorkItStarted` and `..._BeforeMarkingStartupComplete`, shown red by removing the await and by moving it after `MarkComplete()`. `StartupBackgroundWorkTests` proves the helper waits; these prove startup asks it to |
| 15 | ✅ | A real container answers `503` with JSON to a write during startup | Live (T2) | `startup-and-degradation/03`, step 2, green 2026-10-03 |
| 16 | ✅ | That same container accepts the write once ready | Live (T2) | `startup-and-degradation/03`, step 3: the same reset answered `200` after 11.5 s |
| 17 | ✅ | The document would have caught the defect | Live (T2) | The same document against `quotinator:canary419`, built from `d05927f9`: the reset answered `200` with `text/html`, and `/version` `200` |
| 18 | ✅ | The suite's duration is not materially worse | Live | Measured back to back on `Quotinator.Api.Tests`: 4m19s without the gate, 5m19s with it, same 1186 tests |
| 19 | ❌ | The application starts | Live (T1) | The developer starts it in Visual Studio and it reaches `Quotinator ready` |

---
## Observed effect

Not yet established: this section records what the fix produces once step 4 has run.

