# #327: Smoke tests that prove startup problems degrade rather than crash

**Status:** In progress (step 13)
**GitHub issue:** #327
**Tiers required:** T1, T2
**Depends on:** #326, #332, #339, #348, #350

---

## Description

The application must never crash. The worst acceptable outcome of a startup problem is a degraded UX
plus an OpenAPI surface that still allows recovery. That is a feature, and this issue proves it for
every degraded startup scenario the application has: in the environmental tier, with a real container,
a real volume and a real mount, one document per scenario in
`docs/automated-testing/startup-and-degradation/`, and in-process in `StartupResilienceTests` wherever
the sabotage is deterministic and cross-platform.

These scenarios are critical to the application, so every one of them is in the designated smoke set:
a degraded startup that crashes is a failure that cannot be allowed through any run.

Every scenario proves both directions, per `testing-policy.md`: the negative, the fault provoked and the
degraded contract holding, and the positive, the sabotage removed and the application healthy in the
same environment, which also proves the remedy the failure reason names actually works. Each document is
also run against a build from before this issue, so it is shown able to fail.

Nothing recorded before this plan is taken as verified. Every verification row starts ❌ and turns ✅
only on a run made under this plan.

---

## The scenarios

Every branch in `Program.cs` that marks the database unhealthy during startup, each a different failure
at a different stage, plus one startup problem the application does not detect at all:

| # | Scenario | Stage | Reason given | Document |
|---|---|---|---|---|
| 1 | The data directory cannot be created | Configuration, before Kestrel binds | Data directory not writable | `10` |
| 2 | The `keys/` directory cannot be created | Configuration, before Kestrel binds | Data directory not writable | `11` |
| 3 | The data directory cannot be written while a migration is pending | Initialisation, SQLite error 14 or 8 | Data directory not writable | `05` |
| 4 | The database file is not a database | Initialisation, generic failure | Generic initialisation failure | `08` |
| 5 | The database file is truncated | Initialisation, generic failure | Generic initialisation failure | `09` |
| 6 | The schema is broken (a domain table is missing) | Initialisation, generic failure | Generic initialisation failure | `01` |
| 7 | A migration is refused because no backup can be taken | Initialisation, refused step | Migration refused, naming the backup obstacle | `12` |
| 8 | The recorded schema version is ahead of the build | Initialisation, overshoot detected | Schema version ahead, naming Reset and restore | `06` |
| 9 | The `keys/` directory exists but cannot be written | Not detected: fails on each page request | None: `/health` reports healthy | to be written |

Step 1 confirms rows 1 to 8 against the code. Row 9 has no branch to find, which is why step 1 missed it;
it was measured during step 13 (see step 13's result).

---

## The contract every scenario asserts

**Negative, the fault:**

1. The fault is confirmed reached, not inferred from the recipe: the sabotage read back, and `/health`
   answering `503` before any other assertion.
2. The process is alive: `docker inspect -f '{{.State.Running}}'` reads `true`.
3. `/health` is `503` with `unhealthy`, and its reason names the fault.
4. `/`, `/stats` and `/notifications` render `200` with the degraded text and no stack trace.
5. `/openapi/v1.json` is `200`.
6. Every endpoint that reads the database answers `503` (index: *A degraded application is not
   expected to answer from its database*).
7. The recovery route reachable while degraded is stated, and whether it can succeed. Where it cannot,
   the `/health` reason is asserted to name a remedy that can.

**Positive, the remedy:** the document removes its own sabotage using the remedy the reason names, and
the same container reaches `/health` `200`.

**No document asserts a migration number or schema version.**

---

## Steps

### 1. Confirm the dependencies and the scenario list

**Status:** ✅ Done 2026-10-23

Confirm #326, #339, #348 and #350 have reached `Waiting for release` in `overview.md`. Then grep
`src/` for every call to `DatabaseHealthState.MarkFailed` and every startup path that records a
degraded state, and confirm each maps to exactly one row of *The scenarios*. A path with no row gets one,
and a document, before step 2 runs.

**Result.** All four dependencies read `Waiting for release`. `DatabaseHealthState` has two writers,
`MarkFailed` and `MarkHealthy`; the latter is called only by the repair routes (`AdminEndpoints.cs:264`,
`NotificationActionExecutor.cs:154`). `MarkFailed` is called at five sites in `Program.cs`, and every
scenario maps to one of them:

| Site | Branch | Scenarios |
|---|---|---|
| `Program.cs:775` | A directory could not be created at configuration time (`Program.cs:217` for the data directory, `:290` for `keys/`) | 1, 2 |
| `Program.cs:1016` | `InitialiseAsync` refused a migration (`RefusedStep: Migration`) | 7 |
| `Program.cs:1031` | `SchemaVersionOvershootDetected` | 8 |
| `Program.cs:1045` | Initialisation threw SQLite error 14 or 8 | 3 |
| `Program.cs:1056` | Initialisation threw anything else | 4, 5, 6 |

No other path degrades. A refused *seed* does not: only `BackupGuardedStep.Migration` is tested at
`Program.cs:1012`, so a startup that cannot back up before seeding loads nothing and stays healthy,
which is `backup/06`'s subject and not a degraded startup. Scenario 6 reaches `:1056` through the seed:
`01`'s dropped `Quotinator_Quote` makes seeding throw `no such table`, logged as *Database
initialisation failed*.

### 2. Write the in-process tests, red first, each with its positive control

**Status:** ✅ Done 2026-10-23

Requirement 9: in `StartupResilienceTests`, every scenario whose sabotage is deterministic and
cross-platform gets the negative tests (startup completes, `/health` `503` naming the fault) and a
positive control beside them (the same factory without the sabotage answers `200`). Read the class
first and list what already exists against the eight scenarios.

New tests start as signatures and are run red before any production change (`testing-policy.md` →
*Red first means signatures first*). A test whose behaviour already existed when it was written cannot go
red against current code, so it gets the recovery the policy names: mutate the code it guards in the way
it exists to catch, confirm it fails while its siblings pass, revert, and record the mutation in this
step. For the initialisation scenarios, rethrowing from the matching `catch` in `Program.cs` kills
startup, and marking health healthy instead of failed makes the `503` assertion fail.

A scenario that cannot be reached through `QuotinatorWebApplicationFactory` is recorded here with what
was tried, and is T2-only.

**Result.** Every scenario is reachable in-process. 43 tests across `StartupResilienceTests` and
`StartupBackupRefusalTests`, all green against current code:

| Scenario | Negative tests | Positive control |
|---|---|---|
| 1 | `Startup_DataDirectoryCannotBeCreated_EntersDegradedStateInsteadOfCrashing`, `…_HealthReportsUnhealthyRatherThanBeingUnreachable`, `…_HealthReasonNamesTheDataDirectory` (new) | `Startup_FreshDataDirectory_ReportsHealthy` (new) |
| 2 | `Startup_KeysDirectoryCannotBeCreated_StartsDegradedInsteadOfCrashingBeforeKestrelBinds` (existing), `…_EntersDegradedStateInsteadOfCrashing` (new) | `Startup_FreshDataDirectory_ReportsHealthy` |
| 3 | `Startup_DataDirectoryNotWritable_*`, four methods, nine cases (existing, #326) | `Startup_FreshDataDirectory_ReportsHealthy` |
| 4 | `Startup_DatabaseFileCorrupt_*`, four methods (existing), `…_HealthReasonNamesARemedyThatCanWork` (new) | `Startup_FreshDataDirectory_ReportsHealthy` |
| 5 | `Startup_DatabaseFileTruncated_EntersDegradedStateInsteadOfCrashing`, `…_HealthReportsUnhealthyRatherThanBeingUnreachable`, `…_HealthReasonNamesARemedyThatCanWork` (new) | `Startup_RestartOverAnIntactDatabase_ReportsHealthy` (new) |
| 6 | `Startup_DomainTableMissing_EntersDegradedStateInsteadOfCrashing`, `…_HealthReportsUnhealthyRatherThanBeingUnreachable`, `…_HealthReasonNamesTheReset` (new) | `Startup_RestartOverAnIntactDatabase_ReportsHealthy` |
| 7 | `Startup_MigrationRefusedForBackup_*`, four methods (existing, #348), `…_EntersDegradedStateInsteadOfCrashing` (new) | `Startup_NoMigrationPendingWithAZeroQuota_ReportsHealthy` (new) |
| 8 | `Startup_SchemaVersionAheadOfApplication_*`, six methods, eleven cases (existing, #350) | `Startup_RestartOverAnIntactDatabase_ReportsHealthy` |

Truncation needed `SqliteConnection.ClearAllPools()` after the first run, since a pooled connection
holds the file open on Windows and blocks the cut.

**Scenario 7's control removes the sabotage rather than raising the quota.** Raising the quota was
written first and failed: forgetting the newest migration's version row leaves its schema change in
place, so letting it run again fails on its own DDL (SQLite Error 1, *there is already another table or
index with this name: Quotinator_Universe*). A genuinely pending migration needs a database from a prior
release, which step 9's container document builds; raising the quota is proven to resolve the refusal
there. Recorded on the test's own remarks.

**None of the new tests could go red against current code**, since #326, #348 and #350 had already
delivered the behaviour. Each was shown able to fail by mutating `Program.cs`, one mutation per build,
reverted with `git checkout` after each run (`Program.cs` confirmed clean afterwards):

| Mutation | Tests that failed |
|---|---|
| M1: the configuration-time directory catch rethrows | all of scenarios 1 and 2 |
| M2: the generic initialisation catch rethrows | all eleven of scenarios 4, 5 and 6 |
| M3: the generic reason drops *database Reset* and *deleting the database file* | the three `…HealthReasonNames…` tests of scenarios 4, 5 and 6 |
| M4: the unwritable-at-initialisation catch rethrows | all of scenario 3, and all three of scenario 1 |
| M5: the refused migration throws, and the generic catch rethrows | all five of scenario 7, and every test of M2 |
| M6: the overshoot is not marked failed | `…DegradesRatherThanServing`, `…HealthReasonNamesTheOvershoot`, `…HealthReasonNamesBothRemedies` |
| M7: every startup is marked failed | all three positive controls |

M4 also failing scenario 1 is a reading, not noise: with the data directory unobtainable, the
initializer's open fails too, so scenario 1 depends on both the configuration-time guard and the
initialisation guard. Three of #350's scenario-8 tests (`…BlazorPagesRenderDegradedUiRatherThan500`,
`…OpenApiRemainsReachableForRecovery`, `…SurfacesTheNotificationWhileDegraded`) pass under every
mutation above: each asserts something that holds healthy or degraded alike, and none of these
mutations breaks a page, the spec, or the notification producer. They are #350's, not added here.

**Scenario 2 has no in-process reason test, deliberately.** One was written here,
`…KeysDirectoryCannotBeCreated_HealthReasonNamesTheDataDirectory`, and removed during step 4: the
container run showed the data-directory reason misdiagnoses this fault (the directory is writable and a
Reset runs), so the test asserted a defect as the contract. The reason requirement for scenario 2 is
asserted by `11`'s step 1, red until #332.

### 3. Write the document for scenario 1, the data directory cannot be created

**Status:** ✅ Done 2026-10-23, step 4 of the document red until #332

New document in `startup-and-degradation/`, from the index template, with its own container, bind
directory and port. The configured data directory sits under a read-only mount, so `CreateDirectory`
fails before Kestrel binds. Asserts the full contract; the remedy is a writable location, after which
the container reaches `200`.

**Result.** [`10`](../../automated-testing/startup-and-degradation/10-a-data-directory-that-cannot-be-created-degrades-instead-of-crashing.md),
port 18410, `/data` mounted `:ro` with `Quotinator__DataDir=/data/quotinator`. A file where the
directory belongs was measured first and rejected for the container tier: it degrades the same way, but
the reason's remedy, *restore write access*, is not what clears a stray file, so the read-only parent is
the form whose remedy can be proven. Run end to end against `quotinator:local` at `eb8c1a19`:

- step 1: `docker inspect` `true`, `/health` `503` with the data-directory reason, nothing written;
- step 2: `/openapi/v1.json` `200`, all seven database reads `503`;
- step 3: Reset reaches its handler, `409 DestinationDirectoryNotWritable`, naming *restore write access*;
- step 4: **`/`, `/stats` and `/notifications` all `500`**;
- step 5: remounted writable, `/health` and all three pages `200`, database created.

**Step 4 is a defect in the never-crash contract, and #332 owns its fix.** With no `keys/` directory,
every page request fails reading the DataProtection key ring (`ErrorOccurredWhileReadingKeyRing`, once
per page request). `Program.cs:282` defers a keys fallback to #332, whose requirement 10 is that the UI
keeps working when the preferred keys location is not writable. The document asserts the contract as
specified and carries `**Fully green after:** #332`, the index's mechanism for a step another open issue
owns, and #327 depends on #332 for its last green row.

**A second defect, not owned by any issue.** The refusal's second remedy, *Retry with
`allowNoBackup=true`*, answers an unhandled `500` in this state: the reset still has to write.
Searched first (`allowNoBackup`, and reset/500/read-only): no issue covers it, and #453 narrows the
gate's exemption to the reset without changing what the reset does. Recorded in `10`'s Observed effect,
not asserted, and to be filed as its own bug.

Added to the index's category table and smoke table, and to `Quotinator.slnx`.

### 4. Write the document for scenario 2, the `keys/` directory cannot be created

**Status:** ✅ Done 2026-10-23, steps 1, 3 and 4 of the document red until #332

New document. A file is placed where `keys/` belongs on a writable data directory, the same sabotage
`StartupResilienceTests` uses. Asserts the full contract; the remedy is removing the file, after which
the container reaches `200`.

**Result.** [`11`](../../automated-testing/startup-and-degradation/11-a-keys-directory-that-cannot-be-created-degrades-instead-of-crashing.md),
port 18411, `--own-keys` so the shared ring is not mounted over the file. Run end to end against
`quotinator:local` at `eb8c1a19`:

- step 1: `docker inspect` `true`, `status=unhealthy`, **`reasonNamesTheKeysDirectory=False`**: the
  reason is the data-directory one, though the data directory is writable and the database was seeded;
- step 2: `/openapi/v1.json` `200`, all seven database reads `503`;
- step 3: Reset `200`, **`stillUnhealthyAfterReset=False`**: `MarkHealthy` clears the keys failure along
  with any database one, so `/health` reads `200` while `/` still answers `500`;
- step 4: **`/`, `/stats` and `/notifications` all `500`**, on the key ring as in `10`;
- step 5: file removed, restarted, `/health` and all three pages `200`, `keys/` created.

All three red readings trace to one decision, #326 recording an uncreatable `keys/` in the database
health state with the data-directory reason, which #332's requirement 10 replaces with a fallback chain.
The document carries `**Fully green after:** #332`, naming the three steps and stating that #332
rewrites it, since the fallback changes what this sabotage produces.

**The first start has to wait for `unhealthy`, not for a `503`.** Measured: `--wait-listening` returned
on the startup wait page while the fresh database was still seeding, and every endpoint, the reset
included, answered the wait page's `503`. Both `10` and `11` now poll `/health`'s `status` field, the
technique `01` established; `10` was re-run after the change, with the same readings.

Added to the index's category table and smoke table, and to `Quotinator.slnx`.

### 5. Bring `05` to the contract, scenario 3

**Status:** ✅ Done 2026-10-23

`05-degraded-pages-survive-a-migration-failure.md`. Provoked with `--read-only-data`, a `:ro` mount on
`/data`, never `--read-only` on the root filesystem, with the WAL sidecar state pinned by how the
seeding container is stopped and `SQLite Error 14` confirmed in the log. Requirement 1's note on why
`--read-only` stopped forcing a failure stays here, beside the surviving `--read-only` discussion. A
Reset cannot succeed, so the reason must name *restore write access*; the positive control remounts
`/data` writable and reaches `200`. Edit what the document lacks of the contract, then run it.

**Result.** The document had none of `docker inspect`, the database-read gating, a recovery-route step,
a positive control or a command for its page reads, and it waited on a `503` that the startup wait page
also answers. It also asserted every `/stats` count as `0`, a value read from a database the application
could not open, and carried a run's verdict inside a step. Rewritten to six steps; the `--read-only`
note (requirement 1) and the measured conditions table stay. Step 5's page reads now run through
`capture-page.csx`, which cannot read the console, so the console assertion became what a page script
can read: each request's status from the resource timings, and Blazor's own error banner.

Run end to end against `quotinator:local` at `eb8c1a19`, upgrading a `1.8.2` volume of 799 quotes:
`docker inspect` `true`, `status=unhealthy` with the data-directory reason and remedy, fourteen
`SQLite Error 14`; spec `200`, seven reads `503`; reset `409 DestinationDirectoryNotWritable` naming
*restore write access*; each page `200` with the reason, no stack trace, no error banner, and only `503`
failures; remounted writable, `/health` and pages `200`, the migration applied, 795 quotes.

**Two readings recorded, not asserted.** Home and `/stats` render every count as `0` while degraded,
#428's open subject; and every page offers *Reset the database*, which cannot succeed on this mount.

### 6. Bring `08` to the contract, scenario 4

**Status:** ✅ Done 2026-10-23

`08-a-database-that-is-not-a-database-degrades-instead-of-crashing.md`. The file replaced with
non-database bytes, sidecars deleted, container stopped first, size read back. The reset reaches its
handler (`409`) and cannot succeed, so the reason must name deleting the database file; the positive
control deletes it and reaches `200`. Edit, then run.

**Result.** Four changes. Step 3 waited on a `503` the startup wait page also answers, and now polls for
`unhealthy`; it never read the reason, and now asserts requirement 4, that it names deleting the database
file; three passages called the remedy step "step 6", which is the refusal, where the remedy is step 7;
and its Canary claimed no pre-issue build applies, where the commit before #327 predates both the
`/version` gate and #348's refusal. Run end to end against `quotinator:local` at `eb8c1a19`: seeded to 795
quotes, replaced with 34 bytes, `docker inspect` `true`, `unhealthy` naming the file-deletion remedy, the
three pages and the spec `200` with the degraded text and no stack trace, seven reads `503`, reset `409`
naming a file remedy, and `200` after deleting the file.

### 7. Bring `09` to the contract, scenario 5

**Status:** ✅ Done 2026-10-23

`09-a-truncated-database-degrades-instead-of-crashing.md`. As step 6, with a real seeded database cut in
half and both sizes recorded so the truncation point is pinned. Edit, then run.

**Result.** The same wait, reason and Canary changes as `08`, and its pointer to `08`'s unasserted admin
endpoints said three where `08` and the index record six. Run end to end against `quotinator:local` at
`eb8c1a19`: seeded to 795 quotes, truncated 4,730,880 to 2,365,440 bytes, then every step reading as
`08`'s did.

### 8. Bring `01` to the contract, scenario 6

**Status:** ✅ Done 2026-10-23

`01-seeding-backup-degraded-startup-and-reset-recovery.md`. The schema broken by dropping a domain table
on the host while the container is stopped. Here a Reset can succeed, and the document's own Reset is its
positive control. Edit what it lacks of the contract, then run.

**Result.** It already pinned the settled `unhealthy` wait, asserted the container `Up` and recovered
through its Reset; it lacked the reason check, six of the seven gated reads, the spec and the pages, now
in its step 4. Run end to end against `quotinator:local` at `eb8c1a19`, every step passing, and its
Observed effect now carries that reading as a measurement, which takes it off the index's awaiting list.

### 9. Write the document for scenario 7, a migration refused for want of a backup

**Status:** ✅ Done 2026-10-23

New document in `startup-and-degradation/`. Same provocation as
[`backup/07`](../../automated-testing/backup/07-a-migration-that-cannot-take-a-backup-leaves-the-database-unmigrated.md),
which asserts the backup contract; this one asserts the startup contract, and each names the other under
*Explicitly not covered here*. The reason must name the backup obstacle's remedy; the positive control
applies it and reaches `200`.

**Result.** [`12`](../../automated-testing/startup-and-degradation/12-a-migration-refused-for-want-of-a-backup-degrades-instead-of-crashing.md),
port 18412, upgrading `1.8.3` with a 1 GB sparse filler in `backups/`. The reason names an in-app remedy,
`DELETE /api/v1/admin/backups/{name}`, so the positive control applies it through the API rather than
on the volume, which proves the route as well as the remedy. Run end to end against `quotinator:local`
at `eb8c1a19`: `docker inspect` `true`, `unhealthy` naming `BudgetExceeded` and the in-app remedy, spec
`200`, seven reads `503`, reset `409 BudgetExceeded` naming the same remedy, the filler listed by
`GET /api/v1/admin/backups`, pages `200` degraded with no stack trace; then `DELETE` `204`, restart,
`/health` and pages `200`, one migration applied, one backup taken, 795 quotes. `backup/07` gained the
*Explicitly not covered here* pointer back. Added to the index's two tables and `Quotinator.slnx`.

### 10. Bring `06` to the contract, scenario 8

**Status:** ✅ Done 2026-10-23

`06-schema-version-ahead-of-the-application.md`. The version recorded as `MAX(Version) + 1`, never a
literal. The reason must name both Reset and restore; the positive control is the repair it already
drives. Edit what it lacks of the contract, then run.

**Result.** Four changes. Step 3 waited on a `503` the startup wait page also answers and never checked
the process was running; it now polls for `unhealthy`, reads `docker inspect`, and gates all seven reads
rather than one. Step 4's recovery probe was `GET /api/v1/admin/database/seed/preview`, which reads the
untrusted database and which #453 gates; it is now `GET /api/v1/admin/backups`, which is how an operator
finds the backup the reason's second remedy names. Step 7 told a person to open the page and run its
Cancel script, then click Reset and Confirm by hand; both now run through `capture-page.csx`, so the
document runs unattended, and its stale "quote count separates Cancel from Confirm" paragraph is gone,
since `/health` is what separates them. Run end to end against `quotinator:local` at `eb8c1a19`, every
step passing; readings in its Observed effect.

### 11. Confirm #294's test keeps `--read-only` and asserts success

**Status:** ✅ Done 2026-10-23

Run `04-migration-replay-under-restricted-write.md` unchanged: a restricted-write root filesystem, a
healthy upgrade.

**Result.** Run unchanged against `quotinator:local` at `eb8c1a19`: the `1.8.2` seed held 799 quotes
with four duplicate rows, and under `--read-only` the upgrade reported `healthy`, 795 quotes
(`accountedFor=True`), one migration applied and no SQLite error.

### 12. Put every scenario document in the smoke set

**Status:** ✅ Done 2026-10-23

Set `Smoke: yes` in all eight documents, and add each to the index's *designated smoke set* table and
to the `startup-and-degradation/` table. Update the smoke set's description in the index to record why:
a degraded startup that crashes is a failure no run may let through (developer direction, 2026-10-22).

**Result.** `01`, `05`, `06`, `08`, `09`, `10`, `11` and `12` carry `Smoke: yes`, each set as its own
step finished, and each is in both index tables. The set grew from nine to seventeen, and its
description now names the second reason a test belongs there beside the first.

### 13. Write the document for scenario 9, a `keys/` directory that exists but cannot be written

**Status:** ⬜ Not started

New document in `startup-and-degradation/`. `/data` writable, and an empty `keys/` mounted read-only
over it, so `CreateDirectory` succeeds and the failure moves to the first key write. The application does
not detect this state, so the document asserts the contract as specified: the process alive, `/health`
`503` with a reason naming the keys directory, the pages rendering their degraded form, the spec
reachable and every database read gated; and it ends by remounting `keys/` writable and reading `200`.
`test-env.csx` has no option for a separate read-only mount at `keys/`, so the document needs one added,
or states its own `docker run`.

**Measured before writing, 2026-10-23**, with a direct `docker run` against `quotinator:local` at
`eb8c1a19`: the container stays up and `/health` reports **`healthy`**, `/api/v1/quotes/random` and
`/openapi/v1.json` answer `200`, and `/`, `/stats` and `/notifications` answer **`500`**, with 41
`[Runtime - Exception]` lines and no key file written. Most of the document is therefore red until the
fix, which #332's requirement 10 owns (the UI keeps working when the keys location is not writable);
the document carries `**Fully green after:** #332`.

### 14. Run each document against a build from before this issue

**Status:** 🚧 In progress, the readings for `08` and `09` to be re-taken once #348's override is fixed

Per `process.md` → Implementation, step 1. The parent of the first `[#327]` commit
(`git log --reverse --format=%H --grep "\[#327\]" | Select-Object -First 1`) is added as a worktree,
built as `quotinator:canary-327`, and every scenario document is run against it. Each must fail at a
named step. A document whose `Traces to` names an earlier fix (`05` → #293, `01` → #254, `06` → #350)
is also run against the commit before that fix. The reading goes in each document's `Canary`, naming
the commit and tag. Container, image and worktree are removed afterwards.

**Result so far.** `quotinator:canary-327` was built from `e4078444`, the commit before `50434b8b`, and
every one of the eight documents fails on it at a named step:

| Document | Failing step on `canary-327` |
|---|---|
| `01` | step 4: `/api/v1/version` `200` while degraded |
| `05` | step 3: `/version` `200`; step 4: reset `500` |
| `06` | step 3: `healthy`, no overshoot reason (before #350) |
| `08`, `09` | step 5: `/version` `200`; step 6: reset `500` (before #348) |
| `10` | step 2: `/version` `200`; step 3: reset `500`; step 4: pages `500` |
| `11` | step 1: reason does not name keys; step 3: `healthy` after the reset; step 4: pages `500` |
| `12` | step 2: `healthy`, the migration ran without its backup (before #348) |

The earlier-fix canaries, each built from the commit before that issue's first commit:

| Document | Build | Failing step |
|---|---|---|
| `10`, `11` | `canary-326` (`c8322590`) | step 1: the container is not running |
| `05` | `canary-293` (`64297b7e`) | step 5: all three pages `500` |
| `01` | `canary-254` (`210d6830`) | step 3: `healthy`, the dropped table not detected |

The `canary-327` readings are in `06`, `08`, `09` and `12`'s `Canary` sections; the earlier-fix readings
and those for `01`, `05`, `10` and `11` are still to be written into theirs. Worktrees and images were
removed after each run.

**Blocked on #348 for `08`, `09` and `backup/01`.** Measuring the recovery routes showed the
`allowNoBackup` override, #348's feature, does not work where it is offered: it refuses a corrupt or
truncated database (`backup/01` step 5 codifies that refusal as expected) and answers an unhandled `500`
on an unwritable data directory, and only `BudgetExceeded` is exercised against a real database. The
developer decided (2026-10-23) that #348 returns to `In progress` to close those gaps first. Once it has,
`08` and `09`'s recovery step asserts the override recovering the database, and their canary readings
are taken again against that contract.

### 15. Run the template and suite guards

**Status:** ⬜ Not started

`dotnet test tests/Quotinator.Api.Tests --configuration Release --filter "FullyQualifiedName~RepositoryStructureTests"`:
template sections and fields, the smoke index matching the documents, index links, cross-references,
unique ports per document, and every document listed in `Quotinator.slnx`.

### 16. Run the end-of-issue T2 scope

**Status:** ⬜ Not started

Per the index's *When to run what*: the designated smoke set, every document in
`startup-and-degradation/`, and every document this issue changed. This issue changed
`scripts/testing/http.csx`, so per `testing-policy.md` → *A changed test is executed* every document
invoking it runs too. Each document's `Observed effect` records the reading, opening
`**Measured YYYY-MM-DD**`.

### 17. Build and full test run

**Status:** ⬜ Not started

`dotnet build --configuration Release` at 0 warnings, 0 errors, and
`dotnet test --configuration Release --verbosity normal -m:1` all green.

### 18. T1

**Status:** ⬜ Not started

The developer starts the application in Visual Studio and confirms it starts without error.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every degraded startup path in the code has a scenario and a document | Live | Step 1: each of the five `MarkFailed` sites maps to one or more rows of *The scenarios*, table in step 1 |
| 2 | ✅ | The `--read-only`-forces-a-failure technique is gone, with the note on why placed beside the surviving `--read-only` discussion | Live | `05`'s *Determinism* carries the note, beside its `--read-only-data` and `04` discussion; `05` forces its failure with `--read-only-data`, and `04` uses `--read-only` only to assert success (step 5) |
| 3 | ✅ | #294's test keeps `--read-only` and asserts success | Live (Docker) | `startup-and-degradation/04` run unchanged in step 11, `/health` `200`, content accounted for |
| 4 | ❌ | Each scenario is its own document in `startup-and-degradation/` | Unit test | Nine documents present; `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex` green |
| 5 | ❌ | Scenario 1, data directory cannot be created: negative contract and positive remedy | Live (Docker) | `10` run in step 3: every step but step 4 passes; step 4's pages read `500` until #332 |
| 6 | ❌ | Scenario 2, `keys/` cannot be created: negative contract and positive remedy | Live (Docker) | `11` run in step 4: steps 2 and 5 pass; steps 1, 3 and 4 read as its header states until #332 |
| 7 | ✅ | Scenario 3, data directory not writable: negative contract and positive remedy, with `:ro` mount, pinned sidecar state and `SQLite Error 14` confirmed | Live (Docker) | `05` run in step 5, all six steps passing; reason matches `Restore write access` |
| 8 | ✅ | Scenario 4, corrupt database: negative contract and positive remedy | Live (Docker) | `08` run in step 6, all seven steps passing; reset `409`; reason matches `deleting the database file` |
| 9 | ✅ | Scenario 5, truncated database: negative contract and positive remedy | Live (Docker) | `09` run in step 7, all seven steps passing |
| 10 | ✅ | Scenario 6, broken schema: negative contract and positive remedy | Live (Docker) | `01` run in step 8, all seven steps passing; Reset recovers to `200` |
| 11 | ✅ | Scenario 7, migration refused: negative contract and positive remedy | Live (Docker) | `12` run in step 9, all six steps passing; reason names `BudgetExceeded` and the in-app remedy, applied through the API |
| 12 | ✅ | Scenario 8, schema version ahead: negative contract and positive remedy | Live (Docker) | `06` run in step 10, all seven steps passing unattended; reason names Reset and restore; Confirm returns `/health` to `200` |
| 13 | ❌ | Scenarios are independent: each creates, seeds and destroys its own volume and port | Unit test + Live | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` green; each document run alone in step 16 |
| 14 | ❌ | No scenario asserts a migration number or schema version | Live | Read of all nine documents in steps 3 to 10 and 13 |
| 15 | ❌ | Each document carries the template fields, including Determinism, and is in the smoke set | Unit test | Step 15: `RepositoryStructureTests` template guards and `SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` green |
| 16 | ✅ | In-process: every deterministic, cross-platform scenario has negative tests and a positive control | Unit test | Step 2: the `StartupResilienceTests` and `StartupBackupRefusalTests` methods tabled there, 43 green, every new one shown able to fail by mutations M1 to M7 |
| 17 | ❌ | Each document is shown able to fail | Live (Docker) | Step 14: every scenario document against `quotinator:canary-327`, and the earlier-fix canaries, the failing step recorded in each `Canary` |
| 18 | ❌ | End-of-issue T2 scope passes | Live (Docker) | Step 16: smoke set, `startup-and-degradation/`, changed documents and every `http.csx` user, each green |
| 19 | ❌ | Build clean and no regression | Build + Unit test | Step 17: 0 warnings, 0 errors; full suite green with `-m:1` |
| 20 | ❌ | The application still starts | Manual | Step 18, confirmed by the developer in Visual Studio |
| 21 | ❌ | Scenario 9, `keys/` exists but cannot be written: negative contract and positive remedy | Live (Docker) | Step 13's document run, as row 5; red until #332 |
