# #326 — Startup crashes instead of degrading when the data directory is read-only and a migration is pending

**Status:** In progress (step 2)
**GitHub issue:** #326 (open)
**Depends on:** none

> **Next action: execute step 2 — write the five failing tests and confirm each is genuinely red.**
> Step 1 measured what the fix has to classify: `SQLITE_CANTOPEN` (14) for an unwritable directory,
> `SQLITE_READONLY` (8) for a read-only file, and a faithful, ACL-free in-process reproduction for
> both. It also corrected this plan's own trigger hypothesis — see the Trigger finding — which #327
> needs before it designs the same scenario.

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
  Accepted deliberately (see step 5): the underlying cause is one directory, the operator-facing effect
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
   *directory*; `8` (`SQLITE_READONLY`) is a writable directory holding a read-only *file*. Step 4's
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

**Status:** ⬜ Not started

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

**Status:** ⬜ Not started

Gate `GetLastActiveAsync` on `dbHealth.IsHealthy` and wrap it in its own `try`/`catch`, mirroring the
`RecordCurrentAsync` guard immediately below it — same shape, same non-fatal warning idiom. A failure
leaves `lastActiveVersion` null, which the #81 what's-new producer already treats as "nothing to catch up
on", and that producer is itself gated on `dbHealth.IsHealthy` anyway.

Targeted guard only (developer decision, 2026-08-20). An outer backstop around the whole post-`StartAsync`
block was considered and rejected: a ~200-line refactor of Program.cs's top-level statements whose broad
catch would report a genuine future startup bug as "degraded" rather than failing loudly.

The `#81` comment block above this call explains why the read must happen *after* migrations; the guard
must not disturb that ordering, and the comment needs a sentence on why the call is now gated.

### 4. Classify an unwritable data directory into its own failure reason

**Status:** ⬜ Not started

Add a `catch (SqliteException ex) when (…)` beside the existing `DatabaseBackupWriteException` catch,
matching the two codes step 1 measured — `SQLITE_CANTOPEN` (14) for an unwritable directory and
`SQLITE_READONLY` (8) for a read-only file — on `SqliteErrorCode`, never `SqliteExtendedErrorCode`
(step 1 finding 2: the in-process reproduction's extended code is `526`, the live one's is `14`). Setting
one shared reason const naming the actual remedy — the data directory cannot be written, the mount is
read-only or the container user lacks write permission, restore write access and restart — instead of the
generic "run a Reset" text, which cannot work against a read-only mount.

Inner exceptions must be walked, not just the top-level type: the restore-on-failure path can rethrow with
the SQLite failure nested.

The same const is reused by step 5, so the operator sees one message for one cause regardless of which
statement noticed it first.

### 5. Stop the two pre-Kestrel directory creations from terminating the process

**Status:** ⬜ Not started

Wrap `Directory.CreateDirectory(dataDir)` and `Directory.CreateDirectory(keysDir)` so neither can
terminate the process, capturing the failure in a local. After `dbHealth` is resolved (`Program.cs:620`)
and before `app.StartAsync()`, call `dbHealth.MarkFailed(<the step 4 const>)` when that local is set.
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

### 6. Full suite and regression check

**Status:** ⬜ Not started

`dotnet build --configuration Release` (0 warnings) and
`dotnet test --configuration Release --verbosity normal -m:1`. Particular attention to the large body of
`Quotinator.Api.Tests` that assert `{"status":"healthy"}` — step 5 introduces a new path to
`MarkFailed`, and a test whose temp data directory is unexpectedly unusable would now degrade instead of
throwing.

### 7. T1 — Visual Studio pass

**Status:** ⬜ Not started

Developer-run. Program.cs changes only, no Razor, but T1 is required for every code-touching issue
(`docs/release-verification.md`). Against a database that is *not* freshly created, per that document's
own warning about dev-database staleness.

### 8. T2 — Docker pass

**Status:** ⬜ Not started

The issue's own repro, plus its control as a negative control (see verification rows 10 and 11). Run per
`docs/smoke-tests.md`, on a dedicated volume, never a dev or shared database.

### 9. Changelog entries

**Status:** ⬜ Not started

At `Waiting for release`, not at close. `data/changelog/changelog.en.json` `unreleased`, with `nl.json`
and `de.json` in lockstep in the same commit, `326` in `unreleased.issues`. This is user-facing — a
crash-to-degraded change with a real operator-visible message — so it earns a `highlights` entry in plain
English, not `fixed` alone.

### 10. Plan doc, overview and issue updates

**Status:** ⬜ Not started

Overview row updated (`T1 ⬜ T2 ⬜`, plan doc linked — the row currently declares `T2 ⬜` alone, which
`docs/release-verification.md` states is not a valid declaration for any issue that touches code). Plan
doc added to `Quotinator.slnx`. Doc updates commit separately from the code, per process.md.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Startup does not terminate the process when the database cannot be opened | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_EntersDegradedStateInsteadOfCrashing` |
| 2 | ❌ | `/health` reports unhealthy rather than being unreachable | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_HealthReportsUnhealthyRatherThanBeingUnreachable` — 503 with `"status":"unhealthy"` |
| 3 | ❌ | The stated reason names the data directory and its remedy, not a database Reset | Unit test | Same test as row 2 — asserts the reason text, not merely the status code |
| 4 | ❌ | The OpenAPI surface stays reachable while degraded | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_OpenApiRemainsReachableForRecovery` — `GET /openapi/v1.json` → 200 |
| 5 | ❌ | The documented recovery route stays reachable while degraded | Unit test | Same test as row 4 — the admin surface is not answered with the gate's `unavailable` payload |
| 6 | ❌ | Blazor pages render degraded UI rather than 500 | Unit test | `StartupResilienceTests.Startup_DataDirectoryNotWritable_BlazorPageRendersDegradedUiRatherThan500` — `GET /` → 200 |
| 7 | ❌ | An uncreatable `keys/` directory degrades instead of crashing before Kestrel binds | Unit test | `StartupResilienceTests.Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds` |
| 8 | ❌ | Every test above is genuinely red before the fix | Live | Run the new test class against unmodified `Program.cs`; each fails during host construction with the unhandled `SqliteException`, not as an assertion failure |
| 9 | ❌ | No regression | Live | `dotnet build --configuration Release` → `0 Warning(s) 0 Error(s)`; `dotnet test --configuration Release --verbosity normal -m:1` → all pass |
| 10 | ❌ | T2 — the issue's repro degrades instead of crashing | Live | Issue #326's repro commands → `running exit=0`; `curl -s -o /dev/null -w "%{http_code}" …/api/v1/health` → `503`; `…/openapi/v1.json` → `200`; `docker logs … \| grep -c "Unhandled exception"` → `0`; and the initialisation-failure log line is present, proving the failure state was actually reached |
| 11 | ❌ | T2 negative control — a read-only mount that works today still works | Live | Issue #326's control setup → `running exit=0` and `/api/v1/health` → `200` `{"status":"healthy"}`, proving the fix did not degrade a healthy shape |
| 12 | ❌ | T1 — Visual Studio pass | Live | Developer-run against a non-fresh dev database; app starts, Blazor UI loads, `/api/v1/health` → 200 healthy |
