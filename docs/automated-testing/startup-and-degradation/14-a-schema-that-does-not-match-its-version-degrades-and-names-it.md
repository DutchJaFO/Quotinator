# A schema that does not match its version degrades and names what is wrong

**Smoke:** yes
**Environment:** Fresh
**Deterministic:** yes
**Traces to:** #305

## Preconditions

**Beyond the profile.** The database is the one the Fresh profile's own startup creates, fully migrated,
then changed outside any migration while the container is stopped: first a table removed, then, after a
Reset, a column removed. The container binds its data directory to a host path (`--bind`) so the change
can be made from the host with `scripts/testing/execute-sql.csx`.

**The table is `System_Notification`, the one from the live incident behind #305**: missing behind a
current schema version while startup logged `schema is up to date` and `Quotinator ready`, and found only
when the next query touching it threw. Before #305, `/health` read `healthy` over that state.

**Foreign keys are off for the sabotage connection only.** The first boot writes notification
translation rows that reference `System_Notification`, so with enforcement on the drop is refused and
nothing is removed. The point is a missing object, not a refused drop.

## Determinism

- **The sabotage is made while the container is stopped, and stopped cleanly.** Writing to the database
  under a running container races the application's own connections, and `docker stop -t 15` rather than
  `rm -f` leaves no `-wal`/`-shm` sidecars for the next start to replay beside the change.
- **Each restart waits for *listening*, then for `unhealthy`, never for a status code.** The startup wait
  page answers `503` with `status=starting`, so a wait on `503` can return before the degraded state is
  settled. `wait-field` polls the `status` field, as
  [`06`](06-schema-version-ahead-of-the-application.md) does.
- **The reason is asserted for the object it names and the remedies, not for its exact wording.** The
  object name is the schema's own, so it is stable; the surrounding text is the application's to change.
- **The differences are read from the integrity endpoint by position**: the main database is always
  reported first, and a removed table or column is the only difference, so it is the first and only one.
  A table that is missing is one difference, never one per column it held.
- **Never assert a migration number or schema version.** Neither appears in any assertion below.
- **The column removed is `DateDeleted`**, which no index, constraint or foreign key mentions, so
  `ALTER TABLE ... DROP COLUMN` removes exactly one measured object.

## Explicitly not covered here

- **That the content load does not run and nothing is written** while the schema differs: not
  observable from outside the container, and proven by
  `DatabaseIntegrityCheckTests.Difference_ContentLoadDoesNotRunAndNothingIsWritten`.
- **Every other aspect the check compares** (foreign keys, indexes, views, triggers, declared types):
  proven by `SchemaIntegrityComparerTests`, one test per aspect. This document proves the startup contract
  for the two shapes an operator is most likely to meet.
- **An extra table or column**, which is reported and does not degrade:
  [`database-lifecycle/04`](../database-lifecycle/04-a-database-is-complete-for-its-schema-version.md).

## Steps

### 1. Create a healthy, fully migrated database

A folder left by an earlier run holds a database that is already changed or already repaired, and step 1
must start from none, so it is deleted first.

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-14-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-14-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-14-data --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-14-out
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-14 --port 18414 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-14-data
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-fresh.json --equals databases.0.database=main --equals databases.0.isIntact=true --length-equals databases.0.differences=0
```

**Expected:** the environment reports healthy, and the `json` command exits `0`: the main database is
intact with no difference at all.

**On failure:** a fresh database that is not intact means the check disagrees with the schema this build
itself creates, and every degraded reading below would be about that instead of the sabotage. Stop.

### 2. Stop the container cleanly and remove the table

```
docker stop -t 15 qt-startup-14
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-14-data\quotinatordata.db --sql "PRAGMA foreign_keys=OFF; DROP TABLE System_Notification;"
```

**Expected:** the stop echoes the container name, then `execute-sql.csx` reports success.

**On failure:** a SQL error means the table is still there, and step 3 would read an ordinary healthy
start. Stop.

### 3. Restart, and confirm the application degrades and names the table

```
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-14 --port 18414 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-14-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18414/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\health-table.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\health-table.json --equals status=unhealthy --contains reason=System_Notification --contains "reason=database Reset" --contains reason=restore
docker inspect -f '{{.State.Running}}' qt-startup-14
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/quotes --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url "http://localhost:18414/api/v1/quotes/search?q=the" --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/conversations --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/notifications --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/masterdata/sources --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/version --expect 503 --status
```

**Expected:** the `json` command exits `0`: `status = unhealthy`, the reason naming `System_Notification`,
a database Reset and restoring a backup. `docker inspect` prints `true`, and every one of the seven
database reads answers `503`.

**`true` and `unhealthy` together are the never-crash contract**, and the named table is #305's: an
operator reads what is missing instead of a generic failure.

**On failure:** `status=healthy` is the defect #305 exists to remove, a version that vouches for a schema
it does not have. A reason without the table name leaves the operator guessing what to repair. A container
that is not running means the missing table killed the process. `starting` or `down` means the wait gave
up after 180 seconds.

### 4. Confirm the degraded surface, and that the endpoint names the same difference

```
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/ --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/stats --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/notifications --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/openapi/v1.json --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-table.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-table.json --equals databases.0.isIntact=false --length-equals databases.0.differences=1 --equals databases.0.differences.0.kind=missing --equals databases.0.differences.0.objectKind=table --equals databases.0.differences.0.objectName=System_Notification
```

**Expected:** `200` from the three pages and the spec, and the `json` command exits `0`: the main database
is not intact, with one difference, `System_Notification` missing as a table.

**The endpoint answers while degraded, which is when it is needed:** it lists every difference, where the
health reason has room only to name them.

**On failure:** a `500` from a page means the degraded UI is rendering against a query it should not run.
A `503` from the integrity endpoint means the admin exemption from the health gate no longer covers it.

### 5. Reset, and confirm the remedy the reason named works

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18414/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/quotes/random --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-after-table-reset.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-after-table-reset.json --equals databases.0.isIntact=true --length-equals databases.0.differences=0
```

**Expected:** `200` from the Reset, from `/health` and from `/quotes/random`, and the `json` command exits
`0`: the rebuilt database is intact. This is the positive control for steps 3 and 4: the same container,
repaired by the remedy the reason named.

**On failure:** a `409` from the Reset means it refused for want of a backup, a different fault with its
own documents in `backup/`. `/health` still `503` after a successful Reset means the rebuilt database was
not checked, or not marked healthy once it was.

### 6. Remove a column instead, and confirm the reason names it

```
docker stop -t 15 qt-startup-14
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-startup-14-data\quotinatordata.db --sql "ALTER TABLE System_Notification DROP COLUMN DateDeleted;"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-14 --port 18414 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-14-data --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18414/api/v1/health --field status --in unhealthy,healthy --out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\health-column.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\health-column.json --equals status=unhealthy --contains reason=System_Notification.DateDeleted --contains "reason=database Reset"
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/quotes/random --expect 503 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-column.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-14-out\integrity-column.json --equals databases.0.isIntact=false --length-equals databases.0.differences=1 --equals databases.0.differences.0.kind=missing --equals databases.0.differences.0.objectKind=column --equals databases.0.differences.0.objectName=System_Notification.DateDeleted
```

**Expected:** the column is removed, the restart reads `unhealthy` with a reason naming
`System_Notification.DateDeleted` and the Reset, `/quotes/random` answers `503`, and the endpoint reports
that one column missing.

**A column is a separate scenario from a table, deliberately:** a check that compared only table names, the
scope #305 was filed with, passes step 3 and reads `healthy` here.

**On failure:** `healthy` here with step 3 passing means the check sees tables but not columns.

### 7. Reset again

```
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:18414/api/v1/admin/database/reset --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18414/api/v1/health --expect 200 --status
```

**Expected:** `200` from both: the column's positive control.

## Observed effect

Not yet measured: the first run against a build where startup enforces the check is #305's step 13, and
its reading replaces this paragraph. Listed under the index's *Observed effects awaiting measurement*.

## Canary

Build the commit before [#305](https://github.com/DutchJaFO/Quotinator/issues/305)'s first code change,
under a distinct tag, and run steps 1 to 3 against it.

Step 1 must fail on its integrity call with a `404`: the endpoint does not exist there. Run step 3 anyway,
without step 1's last two commands, because it is the reading that matters: with `System_Notification`
removed, `/health` must read `healthy`. That is #305's own premise, measured by #327 on 2026-11-01.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-11-08.** Against `95093abc`, the commit before #305's first code
change, as `quotinator:canary-305`: step 1's integrity call answered `404`. With `System_Notification`
dropped, step 3's `wait-field` read `{"status":"healthy"}` with no reason, `docker inspect` printed `true`,
and `/api/v1/quotes/random` answered `200`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-14 --bind C:\repos\Quotinator\.claude\temp\qt-startup-14-data
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-14-data --delete C:\repos\Quotinator\.claude\temp\qt-startup-14-out
```

**Expected:** the container and both directories are removed.
