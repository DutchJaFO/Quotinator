# #326 — Startup crashes instead of degrading when the data directory is read-only and a migration is pending

**Status:** Waiting for release
**GitHub issue:** #326
**Tiers required:** T1, T2
**Depends on:** none

> **Next action: step 10 — the changelog entries.** Every verification row is ✅: 9 of 9 in this
> issue's own class, 3,475 of 3,475 across the solution at 0 warnings, T1 live on a database with 8
> pending migrations, and T2 live as a controlled pair proving both that the degraded path works and
> that a working read-only mount was not broken by the fix. What remains is release paperwork — the
> changelog entries in all three languages, and step 11's tick of the issue's own Definition of done.

---

## Background

The application must never crash. The worst acceptable outcome of a startup problem is a degraded UX plus
an OpenAPI surface that still allows recovery. A read-only data directory kills the process instead: exit
139, `/health` unreachable, nothing served, and the operator's documented remedy
(`POST /api/v1/admin/database/reset`) unreachable along with everything else.

This is the degradation path #263/#293 built, never reached because the process dies before
`StartupPhaseState.MarkComplete()`.

## Verified against the code before planning

Every claim below was read out of the current code on this branch, not taken from the issue.

- **The crashing call site is real, and it is the only one.**
  `await appVersionTracker.GetLastActiveAsync()` (`src/Quotinator.Api/Program.cs:877`, line 869 when the
  issue was filed) sits after the database-init `try`/`catch` and is neither gated on
  `dbHealth.IsHealthy` nor wrapped. Every other post-init statement — `RecordCurrentAsync`, the #279,
  #289 and #81 notification producers — is both gated and wrapped. So exactly one statement in the whole
  post-`StartAsync` sequence can terminate the process, and it is the one the issue names.
- **`AppVersionTracker` deliberately catches one narrow case only.**
  `GetLastActiveAsync` catches `SqliteErrorCode == 1` with message `no such table: System_AppVersion`
  (`src/Quotinator.Data/Repositories/AppVersionTracker.cs:25`). `SQLITE_CANTOPEN` (14) is not that case
  and propagates, as it should — the tracker is not the right place to decide the process's fate.
- **The degraded surfaces the issue asks for already exist.**
  `DatabaseHealthGateMiddleware.ExemptPrefixes` already covers `/api/v1/health`, `/api/v1/version`,
  `/api/v1/admin`, `/openapi`, `/scalar`, `/_blazor`, `/rest-api`, `/about`, `/stats`, `/notifications`,
  the fingerprinted stylesheets, and `/` as an exact match. Nothing is missing from the degraded state;
  only the crash prevents reaching it.
- **The failure reason an operator would be shown is actively wrong for this cause.**
  The generic catch sets *"…often means the database's recorded schema version doesn't match its actual
  on-disk schema… Resolve with an explicit database Reset"* (`Program.cs:847-851`). On a read-only mount
  a Reset cannot work either — it writes. The issue's expectation 3 is unmet even once the crash is gone.
- **No ADR governs startup degradation.** `docs/architecture-decisions/` has nothing on the never-crash
  rule; it exists only as prose in #326 and #327. Not resolved here — see Raised findings.

## Trigger finding — the issue's stated precondition cannot be the mechanism

The issue attributes the failure to *"read-only **and** a migration is pending"*, and its control
measurement (read-only, no pending migration → `running exit=0`, health `200`) is offered as proof.

`DatabaseInitializer.InitialiseAsync` runs in this order (`DatabaseInitializer.cs:391-401`):

```
MigrateFilenameIfNeeded();
connection.OpenAsync();
EnableWal(connection);          <- the stack trace's throw site
ApplyMigrationsAsync(...)       <- first statement that consults schema versions at all
```

`EnableWal` throws before anything has looked at a schema version, so whether a migration is pending
cannot be what decides its outcome. Both the failing run and the control run execute the identical
statement against a directory with identical permissions, and `journal_mode=WAL` has been in place since
the SQLite backend was introduced (`git log -S`), so neither run is switching journal mode — it is a
no-op read in both.

The real trigger is the **WAL sidecar state the previous container left behind** — measured in step 1,
and the direction is the opposite of what this section originally guessed. SQLite cannot open a WAL
database from an unwritable directory when it must *create* the `-shm` wal-index, which is exactly the
state a clean shutdown leaves: `-wal` and `-shm` checkpointed away. When they are still present and
readable, the same database opens and reads fine. So a volume whose previous container stopped
*cleanly* fails, and one whose sidecars survived succeeds — which correlates with how the seeding
container stopped, not with pending migrations.

**What the control run's sidecar state actually was is unknown and was not recorded** — its recipe does
not state how the seeding container was stopped. Sidecar state is now measured to be the variable that
decides the outcome, so the control is uncontrolled with respect to it; that is a gap in the
measurement, not evidence for a particular explanation. Reproducing it with the stop method pinned
either way is #327's job, and is why #327 must control that variable rather than inherit "migration
pending" as the precondition.

**This does not change the fix.** Whatever makes SQLite fail, the crash is the unguarded
`GetLastActiveAsync`, and the guard is the same. It changes two things only:

1. **T2 setup (verification row 10).** The repro must be shown to actually reach the failure state, not
   assumed to from the recipe. Assert the failure is present (a logged initialisation failure) rather
   than inferring it from the pending-migration framing.
2. **#327's scenario design.** #327 enumerates "data directory not writable with a migration pending" as
   a named scenario. The real precondition is sidecar state, so that scenario as written does not
   reproduce reliably — it would pass or fail depending on how the container that seeded the volume
   happened to stop. #327 must control that variable explicitly. Carry this finding into #327's plan
   doc rather than letting it re-derive it.

## Scope changes

**Folded in (developer decision, 2026-08-20): the pre-Kestrel `Directory.CreateDirectory` crashes.**
`Directory.CreateDirectory(keysDir)` (`Program.cs:233`) and `Directory.CreateDirectory(dataDir)`
(`Program.cs:172`) both run *before* `app.StartAsync()`. On a data directory that cannot be written and
has no existing `keys/` subfolder, the first throws before Kestrel binds — strictly worse than the crash
#326 reports, because there is no wait page, no `/health`, no OpenAPI and no admin surface at all. The
issue's own repro never hits it (its `keys/` already exists from the seeding run), which is why it went
unnoticed.

This widened #326 beyond its filed body and Definition of done, so the issue body was edited to match:
it now carries the folded requirement as expected behaviour 4, a "Scope added during planning" section,
and the two extra tests in its Failing tests table.

**Explicitly not in scope.** Rewriting the smoke-test suite around the never-crash guarantee is #327's
whole subject; this issue touches `docs/smoke-tests.md` only if a T2 pass here surfaces a new command
worth recording (which that document's own living-checklist rule would require anyway).

## Raised findings

- **The never-crash rule has no ADR.** #327 calls it "a **feature**", and it now governs decisions in at
  least two issues, but it is written down only in issue bodies. An ADR ("startup never terminates the
  process; the worst outcome is a degraded state with a recovery surface") would give it the same
  standing as the rules it sits beside. Not written here — raising it, per the "ask before adopting
  precedent as standard" rule. If wanted, it is its own `docs` commit, not part of this fix.
- **`DatabaseHealthState` is named for the database but will now also carry a data-directory failure.**
  Accepted deliberately (see step 6): the underlying cause is one directory, the operator-facing effect
  is identical, and introducing a second degradation state for one extra cause is scope this issue does
  not need. Noted so a later reader sees it was a decision, not an oversight.

---

## Steps

### 1. Reproduce the failure in-process and confirm the SQLite error codes

**Status:** ✅ Done

Measured 2026-08-20 with a throwaway `dotnet-script` probe against Microsoft.Data.Sqlite 10.0.10,
mirroring `SqliteConnectionFactory.CreateConnection` (including `temp_store=MEMORY`) and
`InitialiseAsync`'s opening `PRAGMA journal_mode=WAL`. Windows, directory write blocked via an `icacls`
deny ACE:

| Case | `journal_mode=WAL` | `SELECT` | `INSERT` |
|---|---|---|---|
| db path is a **directory** | `14` (ext `526`) | — | — |
| WAL db, sidecars **absent**, directory unwritable | `14` | `14` | `14` |
| WAL db, sidecars **present**, directory unwritable | OK | OK | — |
| DELETE-mode db, directory unwritable | `14` | **OK** | — |
| Writable directory, **file** read-only | `8` | **OK** | `8` |

Four findings, each of which changes something downstream:

1. **Both codes are real and mean different things.** `14` (`SQLITE_CANTOPEN`) is an unwritable
   *directory*; `8` (`SQLITE_READONLY`) is a writable directory holding a read-only *file*. Step 5's
   classification covers both, as planned — this confirms it rather than narrowing it.
2. **The in-process reproduction is faithful on the primary code.** A directory at the database path
   produces `14` at the same throw site, with no ACL manipulation and identical behaviour on Windows
   and Linux. Its extended code differs (`526`, `CANTOPEN_ISDIR`, versus `14` live), so the
   classification must key on `SqliteErrorCode`, never `SqliteExtendedErrorCode`.
3. **The trigger is sidecar state, and the opposite way round from this plan's first guess.** Sidecars
   *absent* fails; sidecars *present* works. See the corrected Trigger finding above.
4. **A non-WAL database on unwritable storage reads perfectly well.** Only the `journal_mode=WAL`
   switch fails. This answers the question #332 would otherwise have had to research — read-only
   operation needs no WAL — and it is why #335's requirement that its generated database not be left
   in WAL mode is load-bearing rather than housekeeping.

Also confirmed: `Directory.CreateDirectory` over a **file** of the same name throws `IOException`
deterministically, which is the portable sabotage step 2's `keys/` test needs.

### 2. Write the five failing tests and confirm each is genuinely red

**Status:** ✅ Done

All five fail against unmodified `Program.cs`, and each fails during host construction rather than as
an assertion — the shape this step existed to confirm. The two failure modes differ, and both are the
ones the plan predicted:

- **The `keys/` test** throws `IOException` straight out of `Program.cs:233`, before `app.StartAsync()`,
  with `WebApplicationFactory.CreateHost` at the top of the stack. Nothing binds; there is no wait page,
  no `/health`, no OpenAPI. This is the folded scope, reproduced.
- **The four data-directory tests** fail as #313's `TimeoutException` after 30 s: startup never reaches
  `StartupPhaseState.MarkComplete`, so the factory refuses to hand out a client. The unhandled
  exception itself is not logged, because it propagates out of `Main` and the host thread dies
  silently under `WebApplicationFactory`.

The log up to that point is the positive evidence, and it establishes two things:

1. **The guarded catch works.** `InitialiseAsync` fails with `SQLite Error 14` — at `OpenAsync`
   (`DatabaseInitializer.cs:396`) rather than `EnableWal`, since a directory at the database path fails
   the open itself — and is caught and logged. `dbHealth.MarkFailed` therefore ran.
2. **The reason it logs is the wrong advice, observed rather than argued.** The live line is
   *"…Resolve with an explicit database Reset (POST /api/v1/admin/database/reset)…"*, which writes and
   so cannot work against unwritable storage. Step 5 is justified by this observation.

After that line the log stops: no `Ready` banner, no `MarkComplete`. Between the catch at
`Program.cs:854` and `MarkComplete` at `Program.cs:1038` there is exactly one unguarded statement — the
`GetLastActiveAsync` at line 877 that the live stack trace named. Its identity is proved by step 3
rather than by this step: guarding that one call, and changing nothing else, is what must turn these
tests green.

New file `tests/Quotinator.Api.Tests/Startup/StartupResilienceTests.cs`, following
`ProgramNotificationSeedingRegressionTests`' `QuotinatorWebApplicationFactory` +
`WithWebHostBuilder` pattern, with `Quotinator:DataDir` pointed at a per-test temp directory sabotaged
as described in step 1.

Deliberately **not** using throwing fakes for `IDatabaseInitializer`/`IAppVersionTracker`. A hand-thrown
exception would prove only that Program.cs tolerates whatever the fake throws; a real
`SqliteConnectionFactory` against an unopenable path exercises the real initializer, the real tracker and
the real error code — the thing that actually failed live.

Red is expected to manifest as the factory itself throwing during host construction, not as an assertion
failure: today the unhandled exception kills startup before any request is possible. Confirm that shape
explicitly rather than accepting "the test failed" as evidence.

The three tests named in the issue, plus one for the Blazor requirement (expectation 2, which the issue's
test list omits) and one for the folded `keys/` scope:

| Test method | Asserts |
|---|---|
| `Startup_DataDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing` | The host starts, `MarkComplete` runs, and the process is serving |
| `Startup_DataDirectoryNotWritable_HealthReportsUnhealthyRatherThanBeingUnreachable` | `GET /api/v1/health` → 503 `{"status":"unhealthy", …}`, and the reason names the data directory rather than a Reset |
| `Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery` | `GET /openapi/v1.json` → 200, and the admin surface is not gated to the middleware's `unavailable` payload |
| `Startup_DataDirectoryNotWritable_BlazorPageRendersDegradedUiRatherThan500` | `GET /` → 200 |
| `Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds` | With a **file** named `keys` in the data directory (portable, deterministic), the host still starts and `/health` reports the same reason |

Add the new file to `.editorconfig`'s path-scoped `IDE0008` list in the same commit (`Program.cs` and
`AppVersionTracker.cs` are already listed).

### 3. Guard the one crashing call site

**Status:** ✅ Done

**This is what identified the crashing statement by demonstration.** Guarding `GetLastActiveAsync` and
changing nothing else took the class from `Failed: 5, Passed: 0` in 2 minutes to `Failed: 3, Passed: 2`
in 1 second. The two minutes were four tests each waiting out #313's 30-second startup timeout; their
disappearance is the evidence that startup now completes, and that line 877 is what had been stopping
it. The three still-red tests are the ones steps 4, 5 and 6 own.

Gate `GetLastActiveAsync` on `dbHealth.IsHealthy` and wrap it in its own `try`/`catch`, mirroring the
`RecordCurrentAsync` guard immediately below it — same shape, same non-fatal warning idiom. A failure
leaves `lastActiveVersion` null, which the #81 what's-new producer already treats as "nothing to catch up
on", and that producer is itself gated on `dbHealth.IsHealthy` anyway.

Targeted guard only (developer decision, 2026-08-20). An outer backstop around the whole post-`StartAsync`
block was considered and rejected: a ~200-line refactor of Program.cs's top-level statements whose broad
catch would report a genuine future startup bug as "degraded" rather than failing loudly.

The `#81` comment block above this call explains why the read must happen *after* migrations; the guard
must not disturb that ordering, and the comment needs a sentence on why the call is now gated.

### 4. Gate the components that query the database while rendering degraded

**Status:** ✅ Done

Found by step 3's re-run, not by planning: with the crash gone, `GET /` returned **500**. Requirement 2
of the issue — Blazor pages render degraded UI rather than 500 — was not met by removing the crash
alone.

`NotificationSummary.OnInitializedAsync` calls `NotificationReader.GetActiveNotificationsAsync()`
unconditionally, and that reader tolerates only a missing `System_Notification` table — the identical
narrow-catch blind spot `AppVersionTracker` has, so `SQLITE_CANTOPEN` goes straight past it and takes
the page with it. The component is embedded in `StartupErrorModal`, the modal whose entire purpose is
explaining a failed startup, so the ungated query crashed the very page it exists to render.

**The single-route test would have hidden the rest.** Widening it to every Blazor route
`DatabaseHealthGateMiddleware` exempts — those are by construction reachable exactly when the database
is not — measured which actually fail:

| Route | Result | Why |
|---|---|---|
| `/` | ❌ 500 | `NotificationSummary` |
| `/notifications` | ❌ 500 | its own `LoadAsync` |
| `/stats` | ✅ | already gated by #293 |
| `/about` | ✅ | `IChangelogReader` falls back to the JSON service (ADR 018) |
| `/rest-api` | ✅ | queries nothing |

Both failures are gated the way #293 gated `DatabaseStatsSummary`: check `DatabaseHealthState`, render
the empty result, skip the query. Not by widening the reader's catch — CLAUDE.md's "no exception-based
recovery" rule is exactly about not inferring state from thrown exceptions.

`QuoteCard` also queries during render and is also ungated, but `/` passes once the two above are
fixed, so there is no reproduced failure there. Left alone deliberately: this project documents and
fixes what it can reproduce.

### 5. Classify an unwritable data directory into its own failure reason

**Status:** ✅ Done

`catch (Exception ex) when (IsDataDirectoryNotWritable(ex))` sits ahead of the generic catch, and the
classifier walks the inner-exception chain — `DatabaseInitializer` restores its backup and rethrows on
any migration exception, so the `SqliteException` describing the cause is not reliably the outermost
one. It matches `SqliteErrorCode` 14 or 8, never the extended code (step 1, finding 2), plus
`UnauthorizedAccessException`/`IOException` for step 6's directory failures.

The reason names the cause and the remedy, and heads off the wrong advice explicitly: *"A database
Reset cannot resolve this — it writes too."* That sentence exists because the generic reason recommends
exactly that, and step 2 observed it being logged against storage where it cannot work.

Add a `catch (SqliteException ex) when (…)` beside the existing `DatabaseBackupWriteException` catch,
matching the two codes step 1 measured — `SQLITE_CANTOPEN` (14) for an unwritable directory and
`SQLITE_READONLY` (8) for a read-only file — on `SqliteErrorCode`, never `SqliteExtendedErrorCode`
(step 1 finding 2: the in-process reproduction's extended code is `526`, the live one's is `14`). Setting
one shared reason const naming the actual remedy — the data directory cannot be written, the mount is
read-only or the container user lacks write permission, restore write access and restart — instead of the
generic "run a Reset" text, which cannot work against a read-only mount.

Inner exceptions must be walked, not just the top-level type: the restore-on-failure path can rethrow with
the SQLite failure nested.

The same const is reused by step 6, so the operator sees one message for one cause regardless of which
statement noticed it first.

### 6. Stop the two pre-Kestrel directory creations from terminating the process

**Status:** ✅ Done

Both `Directory.CreateDirectory` calls record the failure into a local instead of throwing, and it is
reported the moment there is somewhere to report it to — immediately after `dbHealth` is resolved and
still before `app.StartAsync()`, so the degraded state is in place from the first request rather than
racing it. `MarkFailed` is first-wins, and both this and step 5 pass the same const, so one cause
produces one message regardless of which noticed it first.

`PersistKeysToFileSystem(keysDir)` stays registered exactly as it was. No ephemeral-key fallback is
introduced here; a keys-location fallback chain is #332's scope.

Wrap `Directory.CreateDirectory(dataDir)` and `Directory.CreateDirectory(keysDir)` so neither can
terminate the process, capturing the failure in a local. After `dbHealth` is resolved (`Program.cs:620`)
and before `app.StartAsync()`, call `dbHealth.MarkFailed(<the step 5 const>)` when that local is set.
`MarkFailed` is first-wins idempotent, so a later database-init failure keeps the same message.

`PersistKeysToFileSystem(keysDir)` stays registered exactly as it is, and no key-location fallback is
built here. Not because the alternative is ruled out — CLAUDE.md's DataProtection rule is an unexamined
default, not a settled decision, and now says so — but because a fallback chain is a different feature
with its own design questions, and this issue's job is to stop the process dying. The consequence is
that DataProtection failures surface per-request rather than at startup, which is the degraded behaviour
this issue asks for, not a gap.

#332 introduces the fallback chain that supersedes this for the case where a writable location exists,
and #333 retrofits a diagnostic code onto this issue's failure reason. This issue ships first and is
not blocked by either.

Note the asymmetry deliberately: an unusable `keys/` degrades the whole app even when the database itself
happens to be readable. That is correct — without persisted keys, antiforgery tokens and Blazor circuits
break across restarts — but it does newly degrade a shape that runs today, so it belongs in the commit
message's *why*.

### 7. Full suite and regression check

**Status:** ✅ Done

`dotnet build --configuration Release` → `0 Warning(s) 0 Error(s)`.
`dotnet test --configuration Release --verbosity normal -m:1` → **3,475 passed, 0 failed**, across all
ten test projects (re-run 2026-08-20 after #325's revert; 3,473 at the time this step was first done,
plus the two tests that revert added).

**A later change to this issue's own test class, made under #325's revert.** Raising the source-refresh
connect budget from 10 s to 60 s (`00c35dd`) broke
`Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds`: its data
directory is otherwise valid, so unlike the other four cases it reaches the real source refresh, and a
slow upstream then held startup past #313's 30 s harness limit. The test had been network-dependent all
along and the smaller budget hid it. `Quotinator:AutoUpdateSources` is now `false` for the whole class
(`6356c4c`) — these tests are about what startup does when a directory cannot be written, and nothing in
them concerns downloading. Worth carrying forward: 60 s is now the worst case any full-startup test can
wait per source, so a future test that spins up a real initializer with auto-update left on will hit
the same wall.

Two warnings had to be cleared to get there, neither deferred:

- **`CA1873` in `ChangelogReader.cs:71`** — pre-existing, from #309 on this branch, and exactly the case
  `docs/logging.md` documents: `[LoggerMessage]`'s own `IsEnabled` check happens inside the generated
  method, so a `Distinct().Count()` over every joined row is evaluated at the call site regardless.
  Guarded with an explicit `IsEnabled`, and committed separately attributed to #309 rather than folded
  into this issue's diff.
- **`MSTEST0046`/`MSTEST0044` in this issue's own new test file** — `StringAssert.Contains` and
  `[DataTestMethod]` are both superseded. Fixed in place.

The large `Quotinator.Api.Tests` body that asserts `{"status":"healthy"}` is unaffected: step 6's new
path to `MarkFailed` only fires when a directory genuinely cannot be created.

`dotnet build --configuration Release` (0 warnings) and
`dotnet test --configuration Release --verbosity normal -m:1`. Particular attention to the large body of
`Quotinator.Api.Tests` that assert `{"status":"healthy"}` — step 6 introduces a new path to
`MarkFailed`, and a test whose temp data directory is unexpectedly unusable would now degrade instead of
throwing.

### 8. T1 — Visual Studio pass

**Status:** ✅ Done

Developer-run, 2026-08-21, and against a genuinely non-fresh database — the startup log shows
`applying 8 pending Data migration(s) (version 3 → 11)`, which is exactly what
`docs/release-verification.md` warns must not be substituted with a freshly created one. Three things
confirmed, none inferred from the absence of an error:

- **Startup healthy**, ready banner reached, 799 quotes, no exceptions in the log.
- **`GET /api/v1/health` → `200 OK`, `{"status": "healthy"}`** — observed in the API client, not
  assumed from the app being up.
- **The Blazor UI renders correctly** — navigation, a quote, the language selector, styling intact.
  Screenshot rather than text extraction, which would not have shown whether the CSS loaded (#263's
  own failure was styling while the markup was fine).

This exercises the **healthy** path — the point being that the guards added in steps 3–6 changed
nothing for a working installation. The degraded path is step 9's (T2), and cannot be reached from a
normal Visual Studio run.

Four earlier runs the same evening also fed this issue and #325: they surfaced the connect-cancellation
noise, the intermittency of the download failures, and the seed report's `new` counts not meaning net
new quotes (unresolved, not chased — see step 11).

### 9. T2 — Docker pass

**Status:** ✅ Done

Run 2026-08-21 on dedicated throwaway volumes, removed afterward — never a dev or shared database.

**The setup deviates from the issue's recipe deliberately, and this is the point of the step.** The
issue seeds with `1.8.2` to create a pending migration and never states how the seeding container is
stopped. Step 1 measured that the pending migration is irrelevant and the *sidecar state* decides the
outcome, so running the recipe verbatim would have produced whichever result the stop method happened
to cause. Both runs here therefore use the same image, with no migration pending in either, and differ
in exactly one variable:

| | Seeding container stopped | `-wal`/`-shm` in volume | Result |
|---|---|---|---|
| Row 10 | `docker stop -t 30` (clean, checkpoints) | absent | **degraded** |
| Row 11 | `docker kill` (abrupt) | present | **healthy** |

That is a controlled pair, and it reproduces step 1's in-process finding in a real container: sidecar
presence decides whether SQLite can open the database from a read-only mount. #327 must pin this
variable rather than inherit "migration pending" as the precondition.

Two things worth carrying beyond the row assertions:

- **The reason served is the classified one**, not the generic "run a Reset" text — step 5 proven
  against a real read-only mount rather than only in-process.
- **The control's only warnings are the changelog import failing its write and falling back to JSON.**
  That is #309's designed behaviour, and by `CLAUDE.md`'s triage rule it is information rather than a
  fault: health and quotes both answer `200`.

### 10. Changelog entries

**Status:** ⬜ Not started

At `Waiting for release`, not at close. `data/changelog/changelog.en.json` `unreleased`, with `nl.json`
and `de.json` in lockstep in the same commit, `326` in `unreleased.issues`. This is user-facing — a
crash-to-degraded change with a real operator-visible message — so it earns a `highlights` entry in plain
English, not `fixed` alone.

### 11. Plan doc, overview and issue updates

**Status:** ⬜ Not started

Overview row updated (`T1 ⬜ T2 ⬜`, plan doc linked — the row currently declares `T2 ⬜` alone, which
`docs/release-verification.md` states is not a valid declaration for any issue that touches code). Plan
doc added to `Quotinator.slnx`. Doc updates commit separately from the code, per process.md.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Startup does not terminate the process when the database cannot be opened | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing` |
| 2 | ✅ | `/health` reports unhealthy rather than being unreachable | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_HealthReportsUnhealthyRatherThanBeingUnreachable` — 503 with `"status":"unhealthy"` |
| 3 | ✅ | The stated reason names the data directory and its remedy, not a database Reset | Unit test | Same test as row 2 — asserts the reason text, not merely the status code |
| 4 | ✅ | The OpenAPI surface stays reachable while degraded | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery` — `GET /openapi/v1.json` → 200 |
| 5 | ✅ | The documented recovery route stays reachable while degraded | Unit test | Same test as row 4 — the admin surface is not answered with the gate's `unavailable` payload |
| 6 | ✅ | Blazor pages render degraded UI rather than 500 | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_BlazorPageRendersDegradedUiRatherThan500`, one case per gate-exempt route — `/`, `/about`, `/stats`, `/notifications`, `/rest-api` all → 200 |
| 7 | ✅ | An uncreatable `keys/` directory degrades instead of crashing before Kestrel binds | Unit test | `StartupResilienceTests.Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds` |
| 8 | ✅ | Every test above is genuinely red before the fix | Live | `dotnet test tests/Quotinator.Api.Tests --configuration Release --filter "FullyQualifiedName~StartupResilienceTests"` against unmodified `Program.cs` → `Failed: 5, Passed: 0`, each failing during host construction (`IOException` at `Program.cs:233` for the `keys/` case; #313's `TimeoutException` for the other four), never as an assertion failure |
| 9 | ✅ | No regression | Live | `dotnet build --configuration Release` → `0 Warning(s) 0 Error(s)`; `dotnet test --configuration Release --verbosity normal -m:1` → `3,475 passed, 0 failed` across all ten test projects (re-run 2026-08-20 after #325's revert) |
| 10 | ✅ | T2 — an unwritable data directory degrades instead of crashing | Live | 2026-08-21, volume seeded then stopped with `docker stop -t 30` (sidecars checkpointed away, verified absent), remounted `:ro` → `running exit=0`; `/api/v1/health` → `503` with the data-directory reason; `/openapi/v1.json` → `200`; `/`, `/stats`, `/notifications` → `200`; `/api/v1/quotes/random` → `503` (correctly gated); `grep -c "Unhandled exception"` → `0`; the `LogStartupDatabaseInitFailed` line and the ready banner both present |
| 11 | ✅ | T2 negative control — a read-only mount that works still works | Live | 2026-08-21, same image, volume stopped with `docker kill` (sidecars verified present), remounted `:ro` → `running exit=0`, `/api/v1/health` → `200` `{"status":"healthy"}`, `/api/v1/quotes/random` → `200`, `grep -c "Unhandled exception"` → `0` |
| 12 | ✅ | T1 — Visual Studio pass | Live | Developer-run 2026-08-21 against a non-fresh dev database (`applying 8 pending Data migration(s) (version 3 → 11)`); app starts and reaches the ready banner, `GET /api/v1/health` → `200 OK` `{"status":"healthy"}`, Blazor UI renders with styling intact (screenshot) |
