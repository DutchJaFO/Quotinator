# #327: Smoke tests that prove startup problems degrade rather than crash

**Status:** In progress (step 20, the developer's T1)
**GitHub issue:** #327
**Tiers required:** T1, T2
**Depends on:** #326, #339, #350

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
same environment. Each document is also run against a build from before this issue, so it is shown able
to fail.

**The goal is narrow, and that is deliberate** (developer direction, 2026-10-24): identify each startup
problem and prove it yields a degraded application that *says* it is degraded, instead of a crash.
Telling the operator why is the ideal, not the requirement. Whether an offered remedy gets the
application back out, and how well each surface renders while degraded, are not this issue's to prove:
where the exact answer is not known, that is what the Knowledgebase is for.

**Only a crash is fixed here.** A finding where the application does not know it is degraded, so the
operator meets failures with nothing telling them why, is a crash in effect and is fixed before this issue
closes. Every other finding, a remedy that does not work, a page that fails while `/health` reports the
fault, a test elsewhere that turned out incomplete, gets an issue of its own and may wait for another
milestone.

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
| 9 | The `keys/` directory exists but cannot be written | Configuration, a write probe after creating `keys/` (step 14) | Data directory not writable | `13` |

Step 1 confirms rows 1 to 8 against the code. Row 9 has no branch to find, which is why step 1 missed it;
it was measured on 2026-10-23 and is step 14's subject.

---

## The contract every scenario asserts

**Negative, the fault:**

1. The fault is confirmed reached, not inferred from the recipe: the sabotage read back.
2. The process is alive: `docker inspect -f '{{.State.Running}}'` reads `true`.
3. The application says it is degraded: `/health` settles at `503` `unhealthy` with a non-empty reason.
4. `/openapi/v1.json` is `200`, so the surface an operator recovers through is still there.
5. Every endpoint that reads the database answers `503` (index: *A degraded application is not
   expected to answer from its database*).

**Positive, the control:** the document removes its own sabotage, and the same container reaches
`/health` `200`. This is what keeps a build that degrades on everything from passing.

**Recorded, not asserted:** what the reason says and whether it names the right cause, how the pages
render, what the recovery routes offer and whether they succeed. Each document's `Observed effect`
carries them, and a shortfall is filed as its own issue.

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
recorded in `11`'s Observed effect and filed as a finding (step 13).

### 3. Write the document for scenario 1, the data directory cannot be created

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `10` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`: every step passed.

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

**Step 4's pages are a finding, not a #327 failure.** With no `keys/` directory, every page request
fails reading the DataProtection key ring (`ErrorOccurredWhileReadingKeyRing`, once per page request),
while `/health` correctly reports the fault. This step first recorded it as waiting on #332, on the strength
of a code comment (`Program.cs:282`) naming that issue; #332 is not in this milestone and was never a
dependency. Step 13 removes the page assertion and files the finding.

**A second defect, not owned by any issue.** The refusal's second remedy, *Retry with
`allowNoBackup=true`*, answers an unhandled `500` in this state: the reset still has to write.
Searched first (`allowNoBackup`, and reset/500/read-only): no issue covers it, and #453 narrows the
gate's exemption to the reset without changing what the reset does. Recorded in `10`'s Observed effect,
not asserted, and to be filed as its own bug.

Added to the index's category table and smoke table, and to `Quotinator.slnx`.

### 4. Write the document for scenario 2, the `keys/` directory cannot be created

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `11` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`: every step passed.

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

All three readings trace to one decision, #326 recording an uncreatable `keys/` in the database health
state with the data-directory reason. None of them is a crash: the application says it is degraded. Step
13 removes the three assertions and files them as findings; the `Fully green after: #332` header this
step first wrote goes with them.

**The first start has to wait for `unhealthy`, not for a `503`.** Measured: `--wait-listening` returned
on the startup wait page while the fresh database was still seeding, and every endpoint, the reset
included, answered the wait page's `503`. Both `10` and `11` now poll `/health`'s `status` field, the
technique `01` established; `10` was re-run after the change, with the same readings.

Added to the index's category table and smoke table, and to `Quotinator.slnx`.

### 5. Bring `05` to the contract, scenario 3

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `05` run step by step in its single-command form (step 15), with its own
sentinel quote, against `quotinator:local` built from the tree at `83e27671`: every step passed, the
sentinel stored on `1.8.2` and surviving the migration.

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

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `08` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`: every step passed.

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

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `09` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`: every step passed.

`09-a-truncated-database-degrades-instead-of-crashing.md`. As step 6, with a real seeded database cut in
half and both sizes recorded so the truncation point is pinned. Edit, then run.

**Result.** The same wait, reason and Canary changes as `08`, and its pointer to `08`'s unasserted admin
endpoints said three where `08` and the index record six. Run end to end against `quotinator:local` at
`eb8c1a19`: seeded to 795 quotes, truncated 4,730,880 to 2,365,440 bytes, then every step reading as
`08`'s did.

### 8. Bring `01` to the contract, scenario 6

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `01` run step by step in its single-command form (step 15), counting
exceptions since each read, against `quotinator:local` built from the tree at `83e27671`: every step
passed.

`01-seeding-backup-degraded-startup-and-reset-recovery.md`. The schema broken by dropping a domain table
on the host while the container is stopped. Here a Reset can succeed, and the document's own Reset is its
positive control. Edit what it lacks of the contract, then run.

**Result.** It already pinned the settled `unhealthy` wait, asserted the container `Up` and recovered
through its Reset; it lacked the reason check, six of the seven gated reads, the spec and the pages, now
in its step 4. Run end to end against `quotinator:local` at `eb8c1a19`, every step passing, and its
Observed effect now carries that reading as a measurement, which takes it off the index's awaiting list.

### 9. Write the document for scenario 7, a migration refused for want of a backup

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `12` run step by step in its single-command form (step 15), with its own
sentinel quote, against `quotinator:local` built from the tree at `83e27671`: every step passed, the
sentinel stored on `1.8.3` and surviving the migration.

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

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `06` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`: every step passed except the two *nothing thrown
since Cancel* reads, which are #445's and carry the document's `Fully green after: #445` header.

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

**Status:** ✅ Done 2026-10-27

**Evidence, 2026-10-27.** `04` was converted before running, per the index's *A document is reviewed
every time it is used*: single commands, and its own sentinel quote in place of a quote count predicted
from the bundled seed's duplicates. Run step by step against `quotinator:local` built from the tree at
`83e27671`: the sentinel stored on `1.8.2`, then under `--read-only` `healthy`, the sentinel intact, one
migration applied and no SQLite error.

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

### 13. Narrow every document to the contract, and file each finding as its own issue

**Status:** ✅ Done 2026-10-24

Steps 3 to 10 asserted more than #327 owns: a reason naming a workable remedy, the degraded pages
rendering, what each recovery route offers, and in `10` and `11` the remedy proven through the API. Under
the contract above, each document asserts the five negative items and the positive control, and moves
the rest into its `Observed effect` as recorded readings. The `**Fully green after:** #332` headers in
`10` and `11` go, since nothing they still assert waits on another issue. Each document is run again
after the change.

The findings recorded so far, each to be drafted as its own issue for the developer's approval, none
holding #327 open:

| Finding | Where measured |
|---|---|
| With `keys/` uncreatable, or the whole data directory uncreatable, every page answers `500` while `/health` reports the fault | `10`, `11` |
| An uncreatable `keys/` is reported as an unwritable data directory, and a Reset then turns `/health` healthy while the fault remains | `11` |
| On an unwritable data directory the degraded pages still offer a Reset, and the refusal still offers the `allowNoBackup` override, which answers a bare `500` | `05`, `10` |
| The `allowNoBackup` override refuses a corrupt or truncated database; its tests exercised only `BudgetExceeded` against a real database | `08`, `09`, `backup/01` |
| The generic initialisation reason misdiagnoses a corrupt or truncated file as a schema-version mismatch | `08`, `09` |

The fourth is #348's own feature not working where it is offered, so it is the evidence for reopening
#348 once #327 is finished (developer decision, 2026-10-24), rather than a new issue. The other four are
drafted as issues of their own.

**Documents narrowed, 2026-10-24.** `08`, `09`, `10`, `11` and `12`, the five #327 wrote, now assert the
contract and nothing else: the reason checks became `reasonGiven`, the pages and recovery-route steps
moved into *Observed effect*, `10` and `11` lost their `Fully green after: #332` headers, and `12`'s
positive control removes the filler on the host rather than through the backup API. `01`, `05` and `06`
are left as they are: each asserts what its own originating issue promised (#254's Reset recovery,
#293's degraded pages, #350's reason and repair), and all three pass. The index's smoke-set description
states the narrowed contract. All five were run as written against `quotinator:local` at `eb8c1a19`,
every step passing, and the 52 `RepositoryStructureTests` guards are green.

**Findings filed, 2026-10-24**, each in the *Startup and degraded experience* milestone, none holding
#327 open, and each given Knowledgebase coverage so an operator can still resolve the condition today:

| Finding | Issue | Knowledgebase |
|---|---|---|
| Pages `500` when `keys/` or the data directory cannot be created | [#454](https://github.com/DutchJaFO/Quotinator/issues/454) | `every-page-fails-when-the-keys-directory-cannot-be-created.md` (new) |
| Uncreatable `keys/` reported as the data directory; a Reset clears it | [#455](https://github.com/DutchJaFO/Quotinator/issues/455) | the same entry |
| Unwritable data directory still offers a Reset and the override | [#456](https://github.com/DutchJaFO/Quotinator/issues/456) | `no-backup-could-be-taken.md`, its `DestinationDirectoryNotWritable` section corrected |
| The override refuses an unreadable database | #348, to be reopened after #327 | `the-database-file-cannot-be-read.md` (new) |
| The generic reason misdiagnoses an unreadable file | [#457](https://github.com/DutchJaFO/Quotinator/issues/457) | the same new entry |

### 14. Fix the undetected read-only `keys/` directory, red first, and write its document

**Status:** ✅ Done 2026-10-24

**Evidence, 2026-10-24.** `13` run step by step in its single-command form (step 15) against
`quotinator:local` built from the tree at `83e27671`, which carries the fix: every step passed.

Scenario 9 is the one finding that is a crash in effect: the application does not know it is degraded.
With `/data` writable and an existing `keys/` that cannot be written, `/health` reports **`healthy`**
while `/`, `/stats` and `/notifications` answer **`500`**, with 41 `[Runtime - Exception]` lines and no
key file written (measured 2026-10-23, direct `docker run` against `quotinator:local` at `eb8c1a19`). So
it is fixed here, per the bug-fix order in `testing-policy.md`:

1. **Red first.** An in-process test in `StartupResilienceTests` reaching the state (a `keys/` directory
   that exists and cannot be written), asserting startup completes and `/health` answers `503`, run red
   against current code. Its positive control is `Startup_FreshDataDirectory_ReportsHealthy`.
2. **The document.** New, in `startup-and-degradation/`, asserting the contract, with the read-only
   `keys/` mount. `test-env.csx` has no option for a separate read-only mount at `keys/`; one is added, or
   the document states its own `docker run`. Run red against the current build.
3. **The fix.** Startup detects that the key ring location cannot be written and marks the application
   degraded with a reason saying so. How it detects it is settled against the code when this step starts;
   no ephemeral-key fallback is introduced, per `CLAUDE.md`'s DataProtection rule.
4. **Green.** The test and the document pass, and the document's canary is the build before the fix.

**Result.** Red first, both tiers. `StartupResilienceTests.Startup_KeysDirectoryNotWritable_HealthReportsUnhealthyRatherThanHealthy`
read `200` against current code, reaching the state with an explicit deny ACE on Windows and owner
read-execute elsewhere (lifted in cleanup); its sibling `…_EntersDegradedStateInsteadOfCrashing` passed
throughout, as a never-crash guard. [`13`](../../automated-testing/startup-and-degradation/13-a-keys-directory-that-cannot-be-written-degrades-instead-of-crashing.md),
port 18413, its own `docker run` with `keys/` mounted `:ro`, read `status=healthy` and `200` from every
database read on the pre-fix build.

**The fix reuses a check the codebase already had.** #348's backup pre-flight writes a zero-byte probe
because `CreateDirectory` proves nothing on an existing read-only directory. A second consumer in another
project, so it moved rather than being copied, per `CLAUDE.md`: `Quotinator.Data.Paths.DirectoryWriteProbe`,
with `DirectoryWriteProbeTests` (four, red against a `NotImplementedException` stub first), and the
backup pre-flight and `BackupFileNames.ProbeFileName` now delegate to it. `Program.cs` probes `keys/`
after creating it and records the failure with #326's reason; #455 owns naming `keys/` in it.

**Green.** Build 0 warnings; 112 `Quotinator.Data.Tests` probe and backup tests and 200
`Quotinator.Api.Tests` startup and backup tests pass. Rebuilt `quotinator:local`, `13` reads `unhealthy`
with a reason, every read gated, and `200` with a key file once writable.

**A defect in the documents, found by `13`'s first run.** Every degrading document listed
`/api/v1/stats` among the seven database reads, and no such endpoint exists: it answered `503` only
because the gate refuses any path while degraded, and `404` when healthy. Replaced with
`/api/v1/conversations` in all nine, and each positive control now reads the same seven paths and
expects `200` once healthy, so a gated path that is not a real endpoint can no longer pass unnoticed.
All nine were re-run as written: zero expectation failures.

### 15. Convert this issue's documents to ADR 010

**Status:** ✅ Done 2026-10-24

ADR 010 allows PowerShell only to invoke a single command; any logic beyond that is a `.csx` under
`scripts/testing/`. The documents this issue wrote or brought to its contract, `01`, `05`, `06` and `08`
to `13`, carry that logic in their steps instead: a `Health` function with polling loops, `foreach`
loops over the gated endpoints, regex counts over `docker logs`, file counts and string tests on the
reason text. That deviation is mine, and it is why the T2 run had to be stopped (developer, 2026-10-24).

Each step becomes single command invocations; a step keeps PowerShell only where a C# tool genuinely
cannot do the job, and says so in the document.

**Design (approved 2026-10-24), held to DRY and SOLID.** All new test logic lives in
[`tools/Quotinator.Tools.TestProbe`](../../../tools/Quotinator.Tools.TestProbe/README.md), and the
documents invoke it as they invoke `DbInspector`. ADR 010 sends logic that *"grows beyond a single-file
script"* to a `tools/` project; no repository document describes sharing code between `.csx` files, so
`#load` is not introduced. The existing `.csx` scripts are not extended.

| Logic now in a step | Becomes |
|---|---|
| Polling `/health` until `status` is `unhealthy` or `healthy` | `TestProbe wait-field`, piped into `json` |
| `status=unhealthy`, a non-empty `reason`, a seeded count, a reset's `backupObstacle` and `remedies` | `http.csx` piped into `TestProbe json` |
| `foreach` over the seven gated endpoints | Seven `http.csx --expect` commands, one per endpoint, no logic |
| `docker inspect -f '{{.State.Running}}'` | Unchanged: a single CLI command, which ADR 010 allows |
| Sabotage set-up and file checks | `TestProbe file` |
| Regex counts over `docker logs` | `TestProbe log` |
| Assertions on a `capture-page.csx` page script's return value | `capture-page.csx` piped into `TestProbe json` |

- **Single responsibility.** Each command is its own class; `CommandDispatcher` only selects one and maps
  its result to an exit code.
- **Verified by the existing strategy.** `tests/Quotinator.Tools.TestProbe.Tests` holds 104 tests, red
  before green, with a holding and a failing case for every expectation.
- **Existing duplication, found while designing this.** The argument helpers `Value` and `Flag` are
  copied into seven `scripts/testing/` scripts. Surfaced for the developer, not swept here.

**Progress.**

- [x] TestProbe built, test-first, and checked live against `08`'s scenario on `quotinator:local`: every
  command's positive case exited `0` and every negative control exited `1`, naming the expectation. That
  check found that PowerShell 5.1 prefixes a BOM when it pipes into a native process; `json` now strips
  it, under its own red test.
- [x] Convert `01`, `05`, `06`, `08`, `09`, `10`, `11`, `12` and `13`. TestProbe gained
  `--create-directory`, `--expect-absent` and `--expect-match-count`, and `--write-text`/`--sparse` create
  a missing parent, each under its own red test.
- [x] Remove the shell constructs the first conversion kept. Data now passes between steps through files:
  `http.csx --body-out`, `capture-page.csx --result-out`, `TestProbe wait-field --out`, read by
  `TestProbe json --file`; page scripts are committed files under `scripts/testing/page-scripts/`, passed
  with `--script-file`. No pipe, here-string or variable remains.
- [x] State in the suite index that a document is reviewed against ADR 010 and the own-data rule each
  time it is used, and rewrite its shell section, count rule, waits and template to the single-command
  form.
- [x] `08` re-run in the file form on `quotinator:local`: every step passed. The `capture-page.csx` file
  options checked separately, with a negative control.
- [x] Replace the assertions on bundled content. `08` and `09` assert `healthy` before the sabotage
  instead of a quote count; `05` and `12` import their own
  [sentinel quote](../../automated-testing/startup-and-degradation/fixtures/sentinel-quote.json) before it
  and read it back by id after the remedy. `01` and `06` assert only zero after a Reset, which no
  bundled content decides.
- [x] Re-run `05`, `08`, `09`, `10`, `11`, `12` and `13` in the file form: every step passed, the
  sentinel stored on `1.8.2` and `1.8.3` and surviving each migration.
- [x] Re-run `06`: every step passed except its two exact `[Runtime - Exception]` counts after the
  repair. The log gained `TaskCanceledException`s when the headless browser closed, and kept gaining
  them with no command running (2, then 5, then 9).
- [x] Re-run `05` and `12` with the sentinel fixture as it now stands (its explicit `"character": null`
  and `"author": null` removed under `CLAUDE.md`'s import-file minimalism rule): every step passed.
- [x] Assert exceptions the documented way (developer direction, 2026-10-24: an exception is an issue
  unless expected, and an expected one must match its expectation). A document that stops its container
  counts only what was logged since its previous read, per the index's *Read the log before the
  application stops*; `TestProbe log --mark`/`--since-mark` does that from the daemon's own timestamps.
  Expected types are asserted by name: in `01`, the degraded start's `SqliteException` and the stop's two
  documented `SocketException`s, with any other type at zero.
- [x] `01` re-run: every step passed.
- [x] `06` re-run: every step passed except *nothing thrown since Cancel*, which read 4 and then 7
  `TaskCanceledException`s after the in-page Reset, on two independent runs. No document expects them
  and no Knowledgebase entry records that form, so it is an issue.
- [x] Record it against the issues that already own it rather than filing a new one: a second form of
  #445's defect (a circuit the client closed logged at `Error`), and a form #446's Knowledgebase gap must
  also cover. Measurements posted to both on 2026-10-27; `06` carries `**Fully green after:** #445`.
### 16. Run each document against a build from before this issue

**Status:** ✅ Done 2026-10-31, from the documents' own steps (third run, below)

Per `process.md` → Implementation, step 1. The parent of the first `[#327]` commit
(`git log --reverse --format=%H --grep "\[#327\]" | Select-Object -First 1`) is added as a worktree,
built as `quotinator:canary-327`, and every scenario document is run against it. Each must fail at a
named step. A document whose `Traces to` names an earlier fix (`05` → #293, `01` → #254, `06` → #350)
is also run against the commit before that fix. The reading goes in each document's `Canary`, naming
the commit and tag. Container, image and worktree are removed afterwards.

**First run, 2026-10-23, before step 13 narrowed the documents.** `quotinator:canary-327` was built from
`e4078444`, the commit before `50434b8b`, and every one of the eight documents failed on it at a named
step:

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

Several of those readings land on steps step 13 removes, so each narrowed document is run against the
same builds again and only the readings that still apply are written into its `Canary`. Worktrees and
images were removed after the first run.

**Second run, 2026-10-24, against the narrowed documents**, every image rebuilt from the same commits.
Each of the nine fails at a named step on its canary build, and each reading is in its own `Canary`:

| Document | Build | Failing step |
|---|---|---|
| `01` | `canary-327` | step 4: `/api/v1/version` `200` |
| `01` | `canary-254` | step 3: `healthy`, the dropped table not detected |
| `05` | `canary-327` | step 3: `/version` `200`; step 4: reset `500` |
| `05` | `canary-293` | step 5: all three pages `500` |
| `06` | `canary-327` | step 3: `healthy`, no overshoot reason |
| `08`, `09` | `canary-327` | step 4: `/version` `200` |
| `10`, `11` | `canary-326` | step 1: the process gone, `status=down` |
| `10`, `11` | `canary-327` | step 2: `/version` `200` |
| `12` | `canary-327` | step 2: `healthy`, the migration ran without its backup |
| `13` | `canary-327` and `eb8c1a19` | step 1: `healthy`, no reason |

**The runner's first pass over `01` measured nothing.** `01`'s `create` names no `--image`, so the
image substitution left it on `test-env.csx`'s default `quotinator:local` and both of its canary runs read
the current build, passing throughout. Found by its baseline log naming the current schema; the runner
now supplies `--image` where a document leaves it to the default, and `01` was run again against both
builds. Worktrees and images were removed afterwards.

**Third run, 2026-10-31, from the documents' own steps, after log-wording assertions were replaced with
responses and every zero count was given a positive control.** Each document's current build passes, and
each fails on its canary build:

| Document | Build | Failing step |
|---|---|---|
| `01` | `canary-327` | step 4: `/api/v1/version` `200` |
| `01` | `canary-254` | step 3: the container exits with code `139`, nothing answers |
| `04` | `canary-04` (`2f4abbd6`, `useMemoryTempStore: false`) | step 2: `unhealthy` |
| `05` | `canary-293` | step 5: all three pages `500` |
| `05` | `canary-327` | step 3: `/version` `200`; step 4: reset `500` |
| `06` | `canary-327` | step 3: `healthy`, no overshoot reason |
| `08`, `09` | `canary-327` | step 4: `/version` `200` |
| `10`, `11` | `canary-326` | step 1: the container exits with code `139`, nothing answers |
| `10`, `11` | `canary-327` | step 2: `/version` `200` |
| `12` | `canary-327` | step 2: `healthy`, the migration ran without its backup |
| `13` | `canary-327-keys` (`72b39c02`) and `canary-327` | step 1: `healthy`, no reason |

Worktrees and images were removed afterwards.

**The `01` / `canary-254` reading in both tables above is false.** That commit's quote table is `Quotes`,
so step 3's `DROP TABLE Quotinator_Quote` broke nothing and `healthy` said nothing about #254. Dropping
the table that build has takes the container down, which is the defect #254 fixed.

**Two more findings from this run.** `05` claimed a read-only `/data` with no migration pending stays
healthy, last measured 2026-08-27. Step 14's write probe made that false, since `keys/` is read-only with
it: the state now reads `503`, which is what #336 describes and sets out to improve. And neither
`canary-293` nor `canary-327` has the exception channel, so a `thrown:` count of `0` on those builds says
nothing, and a zero count on any build means something only with a positive control in the same run.

**`06` is not fully green until #445:** its *nothing thrown since Cancel* count read 9, then 11, every line
`TaskCanceledException`.

**Fourth run, 2026-11-01, for the documents step 18 converted.** Each ran as its `Canary` directs, every command in
order, on the commit before the issue it traces to, with the image the only change. Each fails at a named step, and
each full reading is in the document's own `Canary`:

| Document | Build | Failing steps |
|---|---|---|
| `api-surface/02` | `canary-195` (`639f1209`) | 3 to 6 |
| `import-and-staged-actions/01` | `canary-154` (`d113d6f5`) | 2 to 11 |
| `import-and-staged-actions/14` | `canary-181` (`bcace5f6`) | 2 and 3 |
| `import-and-staged-actions/19` | `canary-221` (`22d45db9`) | 2 to 8 |
| `database-lifecycle/03` | `canary-156` (`3ae1c3d5`) | 2 to 4 |
| `startup-and-degradation/02` | `canary-277` (`ed00ef2c`) | 2 and 3 |
| `startup-and-degradation/03` | `canary-280` (`c755e5b0`) | 1 and 3 |
| `startup-and-degradation/03` | `canary-419` (`ea08cac3`) | 1 and 2 |
| `startup-and-degradation/07` | `canary-424` (`29b55805`) | 1 |
| `notifications-and-changelog/01` | `canary-278` (`6dd617ef`) | 1, 2, 5, 6 and 8 |
| `notifications-and-changelog/01` | `canary-339` (`7a25bd38`) | 4 |
| `notifications-and-changelog/07` | `canary-309` (`13b8562e`) | 2 |

`api-surface/01` has no pre-fix build. Its canary is the presence and absence assertions in its own run.

**A first pass was withdrawn** (developer direction, 2026-11-01). It read only some of the documents' commands, ran
steps out of order, inserted commands of its own and rewrote two procedures to match what it had left out, so nothing
it read is used here. The suite index now carries the rule, *A document is executed exactly as written*.

**Where a reading differed from a prediction, the difference was tested on its own before either was believed.**

- **Three predictions were wrong, and each document now states what was measured.** `api-surface/02` said an oversized
  page size was rejected on all three endpoints before #195, and two clamp it to `200`; it also expected `pageSize=0` to
  return no rows, and those two return one. `notifications-and-changelog/01` said step 2 passes on both builds, and it
  reads `404` where the route does not exist.
- **One prediction was right, and the test wrong.** `import-and-staged-actions/14` said step 4 would find duplicate
  Sources. Step 4 passes, while the list it read holds `Matrix` and `The Matrix` among others. Filed as #479.
- **`database-lifecycle/03`'s step 2 fails where that build has no seasons or notifications**, and reads every other
  count correctly, so it still serves as the control.

**Also found by this run, and filed as #480:** `test-env.csx destroy --bind` with no directory, as four documents write it,
attempts to remove a volume the container never had. It costs nothing.

### 17. Run the template and suite guards

**Status:** ✅ Done 2026-10-24, 52 green on `47d2e9c9`

`dotnet test tests/Quotinator.Api.Tests --configuration Release --filter "FullyQualifiedName~RepositoryStructureTests"`:
template sections and fields, the smoke index matching the documents, index links, cross-references,
unique ports per document, and every document listed in `Quotinator.slnx`.

### 18. Run the end-of-issue T2 scope

**Status:** ✅ Done 2026-11-01 for the smoke set and `startup-and-degradation/`. Every one of those documents is
converted and has run from its own steps; `startup-and-degradation/` on 2026-10-31, the rest of the smoke set by
2026-11-01. Next action: step 19.

**The other 60 documents moved to #473** (developer decision, 2026-11-01). This step's scope below includes every
document invoking `http.csx`, and most of those are among the 60 that still carry PowerShell, so the index forbids
running them until they are converted, which is a body of work of its own.

**Stated gaps.** `http.csx` and TestProbe gained options while the smoke set was being converted, so:

- `startup-and-degradation/` ran before the last of them and was not rerun. They added options and changed no
  existing path, which is why this was accepted, but it was reasoned rather than measured.
- The 60 documents have not run against the changed `http.csx`. #473 runs each of them.

**`06` was run five times exactly as written on 2026-11-01, one at a time, on `e77e2828`'s text and `quotinator:local`
built from `00a9dfe2`.** Every step passed. *Nothing thrown since Cancel* read `0` each time, where the document's header
expects `TaskCanceledException` lines until #445. The malformed upload's `JsonReaderException` read `1` each time, and the
cleanup count read `0`. The three recorded earlier runs read 2, 5 and 9 and read 4 and 7 (2026-10-24), and read 9 and 11
(2026-10-31). `06`, its page scripts, `capture-page.csx`, `test-env.csx`, `execute-sql.csx`, TestProbe's `log` command,
`src` and the image are unchanged since `55760c8d`, which recorded that last reading. `http.csx` gained options `06` does
not use. The cause of the difference is not established, and this does not show #445 fixed.

**A pass that edited `05` and `08` to `13` was discarded**, without the developer's authorisation and without any step
having failed. Those edits were removed and those runs are not evidence.

**Readings the suite carries forward as known.** Each document names its issue:

| Document | Step | Issue |
|---|---|---|
| `database-lifecycle/03` | 8: `/version` counts stale after an API import | #468 |
| `import-and-staged-actions/01` | 8: applying an unknown batch answers `200` and dismisses a live notification | #469 |
| `import-and-staged-actions/14` | 5: no command, the sentinel has nothing to run | #400, #441 |
| `notifications-and-changelog/01` | 9: Reset from a notification action leaves no reseed recommendation | #472 |
| `startup-and-degradation/07` | 2: depends on GitHub answering | #462 |

#470 (a dated quote's date lost when its Source is declared dateless in the same file) came out of this step too,
and #471 records the research it raised. #438's second item is covered by `import-and-staged-actions/19`.

**Also found and filed, 2026-11-01:** #474 (an unknown `status` or `entityType` on `/import/actions` answers `200`),
#475 (one exception logged as thrown once per awaited frame it crosses, which may also be `07`'s unidentified four
lines above), #476 (notification payload field names, and an enum stored as a number), #477 (the notification Reset
writes records no app version), #478 (a reseed recommendation cannot say what a reseed would load, and resolves
itself when it loads nothing).

**Log wording.** `notifications-and-changelog/07` no longer reads it, for the same reason as the startup documents:
a file on disk is what the in-memory regression cannot leave, and which source served a read is proven by
`ChangelogReaderTests`.

**Findings from `02`, `03` and `07`.** `03` is no longer held back by #438's first item: its command and
`Accept` were already corrected, and it now passes, so its `Fully green after` line is gone. #438 stays open
for its second item, which is `import-and-staged-actions/19`'s. Separate commands could not reach `03`'s
startup window, so `scripts/testing/startup-window.csx` sends its requests together. `07` logged four
exception lines on one run and none on the next; the four were not identified before the container was
removed, so its step 2 now counts by type. `03` has no canary reading, and neither `02`, `03` nor `07` was run
against a pre-fix build after this conversion; `07`'s earlier reading against `29b55805` was of the removed
log assertions, so it was dropped rather than carried forward.

**`07` still depends on GitHub, which is #462's to remove.** Its step 2 fails whenever GitHub does not answer,
so a red reading there is not by itself a defect in the application. Its header says so with a
`Fully green after: #462` line. **#462 does not block this issue or the milestone:** no application behaviour
depends on it, and this issue closes with `07` still carrying that line.

**First attempt, 2026-10-24, discarded.** All 67 documents were run by extracting their PowerShell
blocks into one script per document with a scratch runner outside the repository, not by following
their steps. That breaks ADR 010 and repeatability, skipped every browser step, ignored each step's
stop-on-failure, and garbled non-ASCII text, so none of its readings is evidence. The same method was
used for every document run and canary reading recorded in steps 3 to 11, 14 and 15, which are reopened
for the same reason. Findings it surfaced, still to be confirmed by a proper run: `import/17` concludes
from a log line that does not exist and leans on bundled aliases; `import/30` reads
`ruleOnNothing=Add/Applied` where it expects `Add/Pending`; `import/22` step 3 prints no `series`
value; `api-surface/05` step 4 printed only the last exception line, so it could not show the first type
its expectation names. Every document is now re-run by following its steps in order with the
repository's own tools.
Per the index's *When to run what*: the designated smoke set, every document in
`startup-and-degradation/`, and every document this issue changed. This issue changed
`scripts/testing/http.csx`, so per `testing-policy.md` → *A changed test is executed* every document
invoking it runs too. Each document's `Observed effect` records the reading, opening
`**Measured YYYY-MM-DD**`.

### 19. Build and full test run

**Status:** ✅ Done 2026-11-01 on `c0f199b0`: 0 warnings, 0 errors, and 4,833 tests green across 12 projects. Only
documents have changed since. Next action: step 20.

`dotnet build --configuration Release` at 0 warnings, 0 errors, and
`dotnet test --configuration Release --verbosity normal -m:1` all green.

### 20. T1

**Status:** ⬜ Not started

The developer starts the application in Visual Studio and confirms it starts without error.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every degraded startup path in the code has a scenario and a document | Live | Step 1: each of the five `MarkFailed` sites maps to one or more rows of *The scenarios*, table in step 1 |
| 2 | ✅ | The `--read-only`-forces-a-failure technique is gone, with the note on why placed beside the surviving `--read-only` discussion | Live | `05`'s *Determinism* carries the note, beside its `--read-only-data` and `04` discussion; `05` forces its failure with `--read-only-data`, and `04` uses `--read-only` only to assert success (step 5) |
| 3 | ❌ | #294's test keeps `--read-only` and asserts success | Live (Docker) | `startup-and-degradation/04` run unchanged in step 11, `/health` `200`, content accounted for |
| 4 | ✅ | Each scenario is its own document in `startup-and-degradation/` | Unit test | Nine documents present; `RepositoryStructureTests.EveryAutomatedTestingDocument_IsLinkedFromTheIndex` green |
| 5 | ❌ | Scenario 1, data directory cannot be created: the contract and its positive control | Live (Docker) | `10` run after step 13 narrows it |
| 6 | ❌ | Scenario 2, `keys/` cannot be created: the contract and its positive control | Live (Docker) | `11` run after step 13 narrows it |
| 7 | ❌ | Scenario 3, data directory not writable: negative contract and positive remedy, with `:ro` mount, pinned sidecar state and `SQLite Error 14` confirmed | Live (Docker) | `05` run in step 5, all six steps passing; reason matches `Restore write access` |
| 8 | ❌ | Scenario 4, corrupt database: negative contract and positive remedy | Live (Docker) | `08` run in step 6, all seven steps passing; reset `409`; reason matches `deleting the database file` |
| 9 | ❌ | Scenario 5, truncated database: negative contract and positive remedy | Live (Docker) | `09` run in step 7, all seven steps passing |
| 10 | ❌ | Scenario 6, broken schema: negative contract and positive remedy | Live (Docker) | `01` run in step 8, all seven steps passing; Reset recovers to `200` |
| 11 | ❌ | Scenario 7, migration refused: negative contract and positive remedy | Live (Docker) | `12` run in step 9, all six steps passing; reason names `BudgetExceeded` and the in-app remedy, applied through the API |
| 12 | ❌ | Scenario 8, schema version ahead: negative contract and positive remedy | Live (Docker) | `06` run in step 10, all seven steps passing unattended; reason names Reset and restore; Confirm returns `/health` to `200` |
| 13 | ❌ | Scenarios are independent: each creates, seeds and destroys its own volume and port | Unit test + Live | `RepositoryStructureTests.EveryAutomatedTestingDocument_PublishesThePortsItUses_AndSharesNoneWithAnother` green; each document run alone in step 17 |
| 14 | ❌ | No scenario asserts a migration number or schema version | Live | Read of all nine documents in steps 13 and 14 |
| 15 | ✅ | Each document carries the template fields, including Determinism, and is in the smoke set | Unit test | Step 16: `RepositoryStructureTests` template guards and `SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` green |
| 16 | ✅ | In-process: every deterministic, cross-platform scenario has negative tests and a positive control | Unit test | Step 2: the `StartupResilienceTests` and `StartupBackupRefusalTests` methods tabled there, 43 green, every new one shown able to fail by mutations M1 to M7 |
| 17 | ✅ | Each document is shown able to fail | Live (Docker) | Step 16: every scenario document against `quotinator:canary-327` and the earlier-fix canaries (third run), and the documents step 18 converted against their own pre-fix builds (fourth run), the failing steps recorded in each `Canary` |
| 18 | ✅ | End-of-issue T2 scope passes | Live (Docker) | Step 18: smoke set and `startup-and-degradation/` run from their own steps, the known readings and gaps tabled there; the remaining `http.csx` users are #473's |
| 19 | ✅ | Build clean and no regression | Build + Unit test | Step 19: 0 warnings, 0 errors; full suite green with `-m:1`, 4,833 tests |
| 20 | ❌ | The application still starts | Manual | Step 19, confirmed by the developer in Visual Studio |
| 21 | ❌ | Scenario 9, `keys/` exists but cannot be written: the application reports itself degraded, and the contract holds | Unit test + Live (Docker) | Step 14: the new `StartupResilienceTests` case red then green, and the new document red on the current build, green after the fix |
| 22 | ✅ | Each finding outside the contract is recorded in its document and drafted as an issue | Live | Step 13: every finding recorded in the named documents' `Observed effect`; filed as #454, #455, #456 and #457, the override gap held for #348, each with Knowledgebase coverage |
