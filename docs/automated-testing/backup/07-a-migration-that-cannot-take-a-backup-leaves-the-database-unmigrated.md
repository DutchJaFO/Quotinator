# A migration that cannot take a backup leaves the database unmigrated, says why, and can be resumed

**Smoke:** no
**Environment:** Upgraded + Constrained
**Deterministic:** yes
**Traces to:** #348, #490

## Preconditions

**Beyond the profile.** One container of this test's own, `qt-backup-07`, publishing `18387`, on a bind
directory. The prior image is the published `ghcr.io/dutchjafo/quotinator:1.8.3`, the release users
upgrade from, so the current build has migrations to apply. Before the current build starts, the backup
folder is filled to the budget, so the backup every migration takes first cannot be taken.

A migration changes the schema in a way only a backup can undo. Until #348 a failed pre-migration backup
was ignored and every pending migration ran unprotected. This proves the migration is refused instead,
that the application degrades and says which obstacle stopped it and where to read about it, and that the
schema is left exactly as the prior build wrote it.

**With no room there can be no backup, so the operator is told and asked to confirm (#490 step 17,
developer, 2026-11-16).** The refused migration is resumed from inside the application, by
`POST /api/v1/admin/database/migrate` or by the degraded page's repair control, which run the same code:
with the backup once there is room, or without one once the operator confirms. This document proves each
route on the same refused database, and keeps the restart as the control that freeing room still works.

## Determinism

- **The prior version is a published tag, not a milestone snapshot.** 1.8.3 is what users run today, and
  its schema is behind any build of this milestone, so pending migrations are guaranteed without editing
  a version row.
- **The obstacle is a full budget, made by one sparse file the size of the whole 1 GB default**, for the
  reason `backup/06` records: the backup taken before a migration is measured against the absolute
  ceiling, not the 90% operating quota.
- **The recorded schema version is read from the file, with no container running.** DbInspector reads
  `System_ConsumerSchemaVersion` read-only and writes it as JSON (`--json-out`). The reading after the
  refusal is compared with the reading before it (`--equals-json`), and each reading after a migration,
  taken from `/api/v1/version` since that application is healthy, with the same earlier reading
  (`--greater-than-json`), so no migration number appears in this document.
- **The refused database is saved once and restored for each route.** Step 5 stops the container cleanly
  and copies it, after confirming the stop left no `-wal` beside it, so the file alone is the whole
  database; steps 7 and 8 copy it back. Each route therefore starts from the same unmigrated file.
- **The degraded start is waited on by its `status` field**, since the startup wait page answers `503`
  too.
- **The log is read before each stop.**

## Explicitly not covered here

- **The startup contract for this state**: the process alive, every database read gated, the OpenAPI
  surface and the degraded pages answering. [`startup-and-degradation/12`](../startup-and-degradation/12-a-migration-refused-for-want-of-a-backup-degrades-instead-of-crashing.md)
  provokes the same refusal to assert that. This document asserts the backup contract.
- **The audit row a confirmed migration writes**: the proof here is the data and the responses, as in
  `backup/10`. The row is held by `AdminMigrateEndpointTests.Migrate_WithOverride_MigratesWithoutABackupAndRecordsIt`.
- **A content load refused for want of its own backup**: the confirmation covers the migration's backup
  only, held by `DatabaseInitializerTests.InitialiseAsync_ContentLoadWithNoBackupPossible_WithOverride_IsStillRefused`.

## Steps

### 1. Let the prior release create its database, and record its version

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07 --delete C:\repos\Quotinator\.claude\temp\qt-backup-07-out --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-07\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-07-out
dotnet script scripts/testing/test-env.csx -- create --name qt-backup-07 --port 18387 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db --sql "SELECT MAX(Version) AS Recorded FROM System_ConsumerSchemaVersion" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json --greater-than 0.Recorded=0
```

**Expected:** the prior release reports healthy, nothing is thrown before the stop, and the `json` command
exits `0`: the release recorded a version above `0`.

### 2. Fill the budget, and start the current build

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --sparse C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db 1073741824 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db 1073741824
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18387/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health.json --equals status=unhealthy --contains "reason=Database migration refused: no backup could be taken (BudgetExceeded)." --contains "reason=POST /api/v1/admin/database/migrate?allowNoBackup=true" --contains reason=https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md
```

**Expected:** every command exits `0`: the filler is in place, and the start degrades with a reason naming
the refused migration and its obstacle, offering the confirmation on the migrate endpoint, and linking the
Knowledgebase entry.

**On failure:** `healthy` means the migration ran without its backup, the defect this document exists to
catch. A reason without the migrate endpoint means the operator is not told how to confirm.

### 3. Confirm the degraded page links the Knowledgebase entry and offers the migration

```
dotnet script scripts/testing/capture-page.csx -- --url "http://localhost:18387/#no-backup-could-be-taken" --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.png --height 1100 --script-file scripts/testing/page-scripts/knowledgebase-link.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\degraded-home.json --greater-than innerHeight=0 --equals href=https://github.com/DutchJaFO/Quotinator/blob/main/docs/knowledgebase/no-backup-could-be-taken.md --equals "text=How to resolve this (Knowledgebase)"
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18387/ --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\repair-controls.png --height 1100 --script-file scripts/testing/page-scripts/migration-repair-controls.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\repair-controls.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\repair-controls.json --greater-than resumeButtons=0 --greater-than withoutBackupButtons=0
```

**Expected:** both `json` commands exit `0`: a non-zero viewport and a link to the entry reading *How to
resolve this (Knowledgebase)*; then at least one *Resume the migration* and one *Migrate without a backup*
button. `degraded-home.png` shows the startup error popup with the reason and the link, and
`repair-controls.png` the two buttons beside *Reset the database*.

### 4. Resume without confirming while there is still no room, which must refuse

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18387/api/v1/admin/database/migrate --expect 409 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\migrate-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\migrate-refused.json --equals backupObstacle=BudgetExceeded --contains remedies=allowNoBackup
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/health --expect 503 --status
```

**Expected:** the call answers `409` naming `BudgetExceeded`, with `allowNoBackup` among its remedies, and
`/health` still answers `503`.

**On failure:** a `200` means the migration ran with no backup and no confirmation. A `404` means the
endpoint does not exist on this build (the #490 canary below).

### 5. Confirm nothing was migrated, and save the refused database

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
docker stop -t 15 qt-backup-07
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db-wal --copy C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db C:\repos\Quotinator\.claude\temp\qt-backup-07-out\refused.db
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db --sql "SELECT MAX(Version) AS Recorded FROM System_ConsumerSchemaVersion" --json-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-refused.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-refused.json --equals-json 0.Recorded=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
```

**Expected:** nothing thrown before the stop; the `docker stop` prints the container's name; the file
command exits `0`: no `-wal` beside the database, which is copied to `refused.db`; and the `json` command
exits `0`: the version recorded after the refused start and the refused resume is the version the release
recorded.

**The stop is `docker stop`, before `destroy`.** `destroy` removes the container with `docker rm -f`, a
SIGKILL, so SQLite never checkpoints and the `-wal` survives. A clean stop lets the application close the
database, and SQLite then folds the `-wal` back into the main file and removes it, so the file alone is the
whole database.

**The copy is taken before the version is read.** DbInspector opens the file read-only and leaves an empty
`-wal` and a `-shm` beside it (measured 2026-11-17). An empty `-wal` holds nothing, but read first it would
fail the check that the application left none, which is the only state the check is about.

**The version is read from the file, not from the API.** A degraded start reports no `database` block
(#428), and the claim being made is about what the file holds after a refused migration, so asking the
file is what the claim actually needs. The container is gone first, so nothing holds the file while it is
read.

**On failure:** a changed version with steps 2 and 4 reporting a refusal means part of the migration ran
before it was refused, which is the partial state the refusal exists to prevent. A `-wal` present means
the copy would not be the whole database; stop.

### 6. Confirm migrating without a backup from the degraded page

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18387/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health-again.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health-again.json --equals status=unhealthy
dotnet script scripts/testing/capture-page.csx -- --url http://localhost:18387/ --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\confirmed.png --height 1100 --script-file scripts/testing/page-scripts/migration-confirm-without-backup.js --result-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\confirmed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\confirmed.json --equals reasonGone=true
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-confirmed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-confirmed.json --greater-than-json database.schemaVersion=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-07\backups "*.db" 1 --expect-length C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db 1073741824
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** the restart degrades again on the same refused migration; on the page, *Migrate without a
backup* then *Confirm* clears the reason (`reasonGone=true`); `/health` answers `200`, the recorded version is
above the release's, `backups/` holds only the filler, untouched, and nothing was thrown.

**`backups/` holding only the filler is the evidence no backup was taken**, and the version is the evidence
the migration ran. The two together are what the confirmation means.

**On failure:** `reasonGone=false` with `/health` still `503` means the confirmation did not reach the
migration. A second file in `backups/` means a backup was written to a budget this document had filled.

### 7. Free the room, and resume with the backup from the endpoint

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db-shm --copy C:\repos\Quotinator\.claude\temp\qt-backup-07-out\refused.db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07 --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18387/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health-restored.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\health-restored.json --equals status=unhealthy
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\backups\filler.db
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18387/api/v1/admin/database/migrate --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\migrate-resumed.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\migrate-resumed.json --equals backup.outcome=Succeeded --present backup.name --greater-than migrationsApplied=0 --greater-than-json appSchemaVersion=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/health --expect 200 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-07\backups "*.db" 1
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** the restored refused database degrades again; with the filler gone, the call answers `200`
with `backup.outcome = Succeeded`, the backup's name, at least one migration applied and a version above the
release's; `/health` answers `200`; `backups/` holds exactly one file, the backup the migration took; and
nothing was thrown.

**The same running container, with only the room changed**, is what separates this `200` from step 4's
`409`: no restart, and no confirmation.

**On failure:** `Skipped` means a backup that could be taken was not. A `409` with the filler gone means the
refusal is not caused by the room at all.

### 8. Restart instead, the remedy the reason also names, as the control

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\backups --create-directory C:\repos\Quotinator\.claude\temp\qt-backup-07\backups --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db-wal --delete C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db-shm --copy C:\repos\Quotinator\.claude\temp\qt-backup-07-out\refused.db C:\repos\Quotinator\.claude\temp\qt-backup-07\quotinatordata.db
dotnet script scripts/testing/test-env.csx -- reenter --name qt-backup-07 --port 18387 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet script scripts/testing/http.csx -- --url "http://localhost:18387/api/v1/quotes?pageSize=1" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\quotes.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\quotes.json --greater-than totalCount=0
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-match-count C:\repos\Quotinator\.claude\temp\qt-backup-07\backups "*.db" 1
dotnet script scripts/testing/http.csx -- --url http://localhost:18387/api/v1/version --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-migrated.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-migrated.json --greater-than-json database.schemaVersion=C:\repos\Quotinator\.claude\temp\qt-backup-07-out\version-before.json#0.Recorded
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-backup-07 --pattern "\[Runtime - Exception\]" --exactly 0
```

**Expected:** every command exits `0`: the restart reports healthy, quotes above `0`, exactly one file in
`backups/` (the backup the migration took first), a recorded version above the one the release recorded,
and nothing thrown.

**This step is the positive control**: every step up to 5 asserts a refusal, and a build refusing every
migration would satisfy them all without this one. It is the restart the reason names beside the endpoint.

## Observed effect

**Measured 2026-11-17** against `quotinator:local` built from `272fa048`: 55 commands, every one exiting
`0`. This is the first run of steps 4, 6 and 7, of the endpoint and page halves of steps 2 and 3, and of
step 5's clean stop.

| Step | Observed |
|---|---|
| 1 | 1.8.3 healthy, `0` exceptions; `Recorded = 5` |
| 2 | filler `1073741824` bytes; `status = unhealthy`, the reason naming `BudgetExceeded`, the migrate endpoint with `allowNoBackup=true`, and the entry's link |
| 3 | `innerHeight = 1100`, the link reading *How to resolve this (Knowledgebase)*; `resumeButtons = 1`, `withoutBackupButtons = 1` |
| 4 | `409`, `backupObstacle = BudgetExceeded`, `allowNoBackup` among the remedies; `/health` `503` |
| 5 | `0` exceptions; the stop clean, no `-wal` left, `refused.db` saved; `Recorded = 5`, equal to step 1's reading |
| 6 | degraded again; `reasonGone = true` after *Migrate without a backup* and *Confirm*; `/health` `200`; `schemaVersion = 9`; `backups/` holding only the filler; `0` exceptions |
| 7 | degraded again from `refused.db`; with the filler gone the call answered `200`, `backup.outcome = Succeeded`, `quotinatordata_v5_…Z.db`, `migrationsApplied = 31`, `appSchemaVersion = 9`; `/health` `200`; one file in `backups/`; `0` exceptions |
| 8 | `totalCount = 795`; one backup, `quotinatordata_v5_…Z.db`, named for the version it protects; `schemaVersion = 9`, above step 1's `5`; `0` exceptions |

**The first run of this form failed step 5**, and the fault was the document's: `destroy` is a SIGKILL,
which leaves the `-wal` behind, and DbInspector's read, then placed before the check, leaves an empty one
of its own. Step 5 now stops the container cleanly and copies the file before reading it. Measured
separately: the clean stop exited `0` in under a second with both sidecars gone.

## Canary

**#348's canary.** Run 2026-09-26 against `4cfe1006`, the commit before step 8 of #348's plan, as
`quotinator:canary348`, with this document's earlier PowerShell form, whose steps 1 to 5 are the current
steps 1, 2, 3, 5 and 8:

| Step | Assertion | Canary |
|---|---|---|
| 1 | the prior release records a version | passes: `before=5` |
| 2 | the start degrades, naming the obstacle and the entry | **fails**: the migration ran and the start was healthy |
| 3 | the degraded page links the entry | **fails**: no link |
| 5 | the recorded version is unchanged | **fails**: `refused=9`, migrated with no backup taken |
| 8 | the remedied start takes a backup | **fails**: `backups=0`, nothing was left to migrate |

**#490's canary.** Build `56dfe433`, the commit before step 17's code, as `quotinator:canary-490-migrate`, and
run steps 1 to 4. Step 2's `json` command must fail on the migrate endpoint missing from the reason, step 3's
second `json` command on `resumeButtons`, and step 4 must answer `404`: the refused migration could not be
resumed from inside the application. Remove the container, image and worktree afterwards.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-backup-07 --bind C:\repos\Quotinator\.claude\temp\qt-backup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-backup-07 --delete C:\repos\Quotinator\.claude\temp\qt-backup-07-out
```

**Expected:** the container and both directories are gone.
