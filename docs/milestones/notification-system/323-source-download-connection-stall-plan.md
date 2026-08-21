# #323 — Source download: a stalled connection attempt outlives its request and fails every other source on the same host

**Status:** Waiting for release
**GitHub issue:** #323 (open)
**Depends on:** none

> **Next action: the issue's own Definition-of-done checkboxes, then the commit.** Every verification row
> below is ✅ and the changelog entries are in. The issue body still carries the over-strong claim
> corrected under "Finding" below — that edit and the DoD ticks are the two remaining `gh issue edit`
> actions, both pending developer approval per `CLAUDE.md`'s draft-then-act rule.

---

## Background

Found live on 2026-08-17 while reading a normal development startup log. Two bundled sources each timed
out after 30 s, adding ~70 s to startup before Kestrel began listening. The fallback behaved correctly —
both sources resolved to their local copies and all 799 quotes loaded — so the only visible symptom was
the delay.

The stack trace is what makes this a bug rather than an unreachable-network report. Both failures bottom
out at `HttpConnectionWaiter.WaitForConnectionWithTelemetryAsync`, meaning neither request ever obtained a
connection; no request bytes were sent. The named client
(`SourceCacheUpdater.HttpClientName`, registered in `src/Quotinator.Api/Program.cs`) set only
`HttpClient.Timeout` and never configured a primary handler, so
`SocketsHttpHandler.ConnectTimeout` kept its default of `Timeout.InfiniteTimeSpan`.

## Verified against the code before planning

- **One registration site.** `AddHttpClient` appears exactly once in the solution
  (`src/Quotinator.Api/Program.cs`), so there is no second client to keep in sync.
- **No ADR governs HTTP client configuration.** `docs/architecture-decisions/` has nothing on
  `HttpClient`, `SocketsHttpHandler`, or connection pooling — this plan is not deviating from a recorded
  decision.
- **`Quotinator:SourceRefreshTimeoutSeconds` is not an HA add-on option.** `addon/config.yaml` exposes
  `auto_update_sources`, `source_update_interval_hours`, `unicode_aware_search` and the two
  `auto_purge_*` keys only. A sibling connect-timeout key therefore needs no `addon/`/`addon-beta/`
  mirroring, matching the existing precedent rather than creating an exception to it.
- **The bundled sources share one host only by chance** (developer, 2026-08-17).
  `data/sources/manifest.json`'s `downloadUrl` entries all happen to be `raw.githubusercontent.com` today,
  so one `HttpConnectionPool` entry currently serves every source — but that is a property of the current
  manifest, not a design invariant, and a manifest with distinct hosts would get one pool entry each. The
  cross-source interference described below is therefore conditional on today's data; the unbounded connect
  budget this issue fixes is not, and applies per source regardless of host topology.

## Measured root cause

A loopback listener that accepts TCP but never completes the TLS handshake, hit twice through one
`HttpClient` with a 5 s timeout:

```
ConnectTimeout             : -00:00:00.0010000   <- Timeout.InfiniteTimeSpan
PooledConnectionLifetime   : -00:00:00.0010000   <- infinite
PooledConnectionIdleTimeout: 00:01:00

    [listener] accepted socket #1
request 1: TaskCanceledException after 5.01s
request 2: TaskCanceledException after 5.01s

RESULT: 2 requests -> 1 TCP connection attempt accepted by the listener.
```

Two requests, one socket: request 1's connection attempt was not cancelled when request 1 timed out, and
request 2 attached to it instead of dialling.

**This specific socket-sharing outcome is doubly conditional** — timing-dependent (see "Finding" below),
and dependent on two sources resolving to the same host, which today's manifest does only by coincidence.
The unconditional defect, and the one the fix addresses, is that `ConnectTimeout` was infinite: a stalled
connect had no budget of its own and was bounded only by whichever request happened to be waiting on it.
That holds per source, on any host topology.

## Design

Bound the connection attempt itself, at the primary handler.

1. **Two new `const`s on `SourceCacheUpdater`**, alongside the existing `DefaultHttpTimeoutSeconds` so
   every timeout governing this client is discoverable in one place:
   - `DefaultConnectTimeoutSeconds = 10` — **raised to 60 on 2026-08-20 (`00c35dd`, under #325)**, and
     `DefaultHttpTimeoutSeconds` from 30 to 90 alongside it. 10 was only ever a safe finite value
     chosen when the defect was an *infinite* budget; it was never measured as correct, and it turned
     an intermittent path into a failure that need not have happened. What this issue established and
     what still holds is the *relationship* — connect must stay below request, or the request cancels
     first and `ConnectTimeout` never applies, which is this issue's own defect returning. The numbers
     are expected to be tuned again alongside #329's retry.
   - `DefaultPooledConnectionLifetimeMinutes = 2`
2. **`Program.cs` adds `.ConfigurePrimaryHttpMessageHandler(...)`** to the existing `AddHttpClient` call,
   constructing a `SocketsHttpHandler` with both values, and reads an optional
   `Quotinator:SourceRefreshConnectTimeoutSeconds` override exactly as `SourceRefreshTimeoutSeconds` is
   already read.

No new type is introduced — a factory class was considered and rejected as unnecessary indirection for a
single registration site (project priority: simplicity).

**Why every test lives in `Quotinator.Api.Tests`.** The defect is in the registration, so the tests must
exercise the *real* registration rather than a handler the test constructs itself — a test that builds
its own `SocketsHttpHandler` would assert .NET's behaviour and pass whether or not `Program.cs` was ever
fixed. `QuotinatorWebApplicationFactory` gives access to the live DI container, so the tests resolve the
actual named client and the actual `HttpClientFactoryOptions`. This also makes them genuinely red against
unmodified code with no new production type needed to make them compile.

`PooledConnectionLifetime` is included because it is the same registration and the same class of defect
(an unbounded connection lifetime); `HandlerLifetime` recycles the handler but never its pooled
connections, so before this a connection never rotated and a DNS change was never observed.

### Deliberately out of scope

**The per-file download loop stays serial.** Making downloads concurrent would reduce N × timeout to one
timeout, but it is a performance change, not this bug, and widening a bug fix into one is how the scope
of a red-test-first fix gets lost.

**Recorded constraint (developer, 2026-08-17): the import must stay serialised — one file's import can
affect the next one's actions.** Should download concurrency ever be pursued, it applies strictly to the
HTTP fetch inside `ResolveAsync`, never to the downstream import/seeding loop in
`QuotinatorDatabaseInitializer`. This fix touches neither.

---

## Steps

### 1. Add the red tests

**Status:** ✅ Done

New file `tests/Quotinator.Api.Tests/Startup/SourceCacheHttpClientTests.cs`. Six tests written and run
against unmodified code: five red on genuine assertion failures, one green (see "Finding") and therefore
deleted. The behavioural tests stand up a real `TcpListener` on `127.0.0.1:0` that accepts and holds
connections without ever responding (stalling the TLS handshake), then drive the real named client
resolved from the app's own `IHttpClientFactory`. The registration tests resolve
`IOptionsMonitor<HttpClientFactoryOptions>` for that client and apply its
`HttpMessageHandlerBuilderActions` to inspect the resulting primary handler.

### 2. Add the two `const`s to `SourceCacheUpdater`

**Status:** ✅ Done

`DefaultConnectTimeoutSeconds` and `DefaultPooledConnectionLifetimeMinutes`, each with an XML
`<summary>` explaining *why* the value exists (CS1591 is active in `Quotinator.Data`) — mirroring how
`DefaultHttpTimeoutSeconds` documents its own 5 s → 30 s history.

### 3. Configure the primary handler in `Program.cs`

**Status:** ✅ Done

`.ConfigurePrimaryHttpMessageHandler(...)` on the existing `AddHttpClient` call, plus the
`Quotinator:SourceRefreshConnectTimeoutSeconds` read alongside the existing
`SourceRefreshTimeoutSeconds` read.

### 4. Turn the tests green and re-run the full suite

**Status:** ✅ Done

`dotnet test --configuration Release --verbosity normal -m:1` — 3,450 passed, 0 failed. A clean
`dotnet build --configuration Release --no-incremental` reports 0 warnings in any file this issue
touched (see "Pre-existing warnings" below).

### 5. Housekeeping

**Status:** ✅ Done

- ✅ `.editorconfig` scoped `IDE0008` list extended with `src/Quotinator.Data/Import/SourceCacheUpdater.cs`
  and `tests/Quotinator.Api.Tests/Startup/SourceCacheHttpClientTests.cs`; the former's `var` declarations
  converted to explicit types (`Program.cs` was already listed). The two `IDE0305` follow-on warnings this
  exposed were fixed in the same pass.
- ✅ Both plan docs added to `Quotinator.slnx`. The new test file needs no entry — it lives inside a
  project, and `CLAUDE.md` forbids listing project-owned source files as solution items.
- ✅ Changelog entries added to `changelog.en.json` + `nl`/`de` lockstep (one `highlights` line, two
  `fixed` lines, `323` appended to `unreleased.issues`); all three markdown files regenerated with
  `--max-releases 3`. `ChangelogSchema` and `TranslationCompleteness` tests green.
- ⬜ Issue body correction and Definition-of-done ticks (see "Finding") — both `gh issue edit`, pending
  approval.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A stalled connection attempt fails on its own connect budget, not the request budget | Unit test | `SourceCacheHttpClientTests.StalledConnect_FailsWithinConnectTimeoutNotRequestTimeout` |
| 2 | ✅ | The abandoned attempt does not decide the second request's outcome | Unit test | `SourceCacheHttpClientTests.TwoRequestsSameHost_FirstConnectStalls_SecondFailsIndependentlyNotByInheritance` |
| 3 | ✅ | The registered client's primary handler has a finite `ConnectTimeout` | Unit test | `SourceCacheHttpClientTests.SourceCacheClient_PrimaryHandler_HasFiniteConnectTimeout` |
| 4 | ✅ | The registered client's primary handler has a finite `PooledConnectionLifetime` | Unit test | `SourceCacheHttpClientTests.SourceCacheClient_PrimaryHandler_HasFinitePooledConnectionLifetime` |
| 5 | ✅ | `Quotinator:SourceRefreshConnectTimeoutSeconds` overrides the default when set | Unit test | `SourceCacheHttpClientTests.SourceCacheClient_ConnectTimeoutOverride_IsApplied` |
| 6 | ✅ | No regression: a healthy refresh still downloads, converts, validates and caches | Unit test | Full suite `dotnet test --configuration Release -m:1` — 3,450 passed, 0 failed |
| 7 | ✅ | T2 — container starts and refreshes sources with no added delay on a healthy network | Live | `docker build -f docker/Dockerfile -t quotinator:local .` + `docker run`; both sources logged `[Database - SourceRefresh] updated`, zero timeout/cancellation lines, startup 32.1 s wall-clock (dominated by the fresh-database seed of 799 quotes). Baseline smoke section 1 also green: `/health` healthy, `/version` `1.8.3` (not `1.0.0`, so `Directory.Build.props` reached the build context), `/quotes/random` served, `search?q=love` 20 items, `Casablanca&field=source` 9, `Churchill&field=author` 1, `Rick&field=character` 0 + `NoResults` as documented |

---

## Finding — the poisoning claim was stronger than the evidence

The issue body as filed said a stalled attempt *deterministically* fails every remaining source on that
host. Writing the red tests disproved the "deterministically" part.

A sixth test, `TwoRequestsSameHost_FirstConnectStalls_SecondOpensItsOwnConnection`, asserted the listener
accepts only one socket for two sequential requests — the shape the original standalone probe measured
(5 s request timeout, back-to-back calls). Through the real registration at an 8 s timeout it accepted
**two**, i.e. it passed before any fix. Attempt-sharing is real but timing-dependent: whether the second
request inherits the pending attempt or dials its own depends on where the pool's cleanup cadence falls
relative to the first request's timeout.

That test was deleted rather than kept — a test that is green before the fix is not a regression guard
for it, and keeping it would have implied a guarantee the runtime does not make. What *is* unconditional,
and is what rows 1–5 now pin down, is that `ConnectTimeout` was infinite: a stalled connect had no budget
of its own and was bounded only by the request timeout. Row 2 covers the consequence in the form that
holds deterministically — the second request spends its own connect budget rather than inheriting an
outcome.

## Pre-existing warnings found and fixed — originating issue: #309

A clean `dotnet build --configuration Release --no-incremental` surfaced 4 warnings that predate this
issue. None are in files #323 touched; all four are in #309's in-flight files, exposed by that issue's
own `var` → explicit-type conversions:

| File | Warning | Fix |
|---|---|---|
| `src/Quotinator.Data/Import/ChangelogSystemContentImporter.cs` | IDE0028 | `new List<ChangelogLineEntity>()` → `[]` |
| `tests/Quotinator.Data.Tests/Database/ChangelogDatabaseInitializerTests.cs` | IDE0028 | `new List<string>()` → `[]` |
| `tests/Quotinator.Data.Tests/Import/ChangelogSystemContentImporterTests.cs` (×2) | IDE0305 | `(await …).ToList()` → collection expression |

**Fixed here rather than deferred**, per the zero-warning policy (developer direction, 2026-08-17): a
warning is never left active when it can be fixed. The initial instinct to leave them for #309 was wrong
— attribution and ownership are recorded (here, and in the commit message), which is what "link the fix
to the issue that created them" requires; leaving the build dirty is not.

All three files were already in `.editorconfig`'s scoped `IDE0008` list, so no list change was needed.
Clean rebuild after the fixes: **0 warnings, 0 errors**.
