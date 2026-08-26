# Seeding takes a backup, a broken schema degrades rather than crashes, and Reset recovers

**Smoke:** no
**Environment:** Fresh + Constrained
**Traces to:** #254

## Preconditions

**Beyond the profile.** The data directory is a **bind mount** instead of the profile's named volume,
so the host can manipulate the SQLite file directly — the whole test turns on breaking the schema from
outside the container. It runs its own container (`qt-startup-01`, stopped and started around a host
edit), and the Constrained defect is a `DROP TABLE Quotinator_Quote` applied by the host while the
container is stopped.

`Quotinator.Tools.DbInspector` cannot be used here: it opens read-only (`Mode=ReadOnly`, see its
`README.md`) and cannot run the `DROP TABLE` this needs. `scripts/testing/execute-sql.csx` is the writable
counterpart and opens a normal connection.

Three separate startup states are exercised in sequence, and each depends on the previous one having
reached its own end state:

1. a fresh baseline seed (no backup expected)
2. an ordinary restart (backup expected)
3. a deliberately broken schema (degraded, backup taken, restore attempted)

**This document asserted the opposite of
[`02-startup-backup-gating-and-storage-budget.md`](02-startup-backup-gating-and-storage-budget.md)
until 2026-08-23** — that an ordinary restart *does* take a backup, and that this was a deliberate
tradeoff rather than a defect. It was neither a contradiction nor a difference of setup: #277 gated
backups on each action's own real-work signal, and this document went on describing the behaviour from
before that. Its own justification named the missing gate that #277 supplied.

## Determinism

- **The bind path is an absolute Windows path, built from `$PWD`.** One directory, on one filesystem,
  with nothing translating it on the way to `docker`. The POSIX-style path this document used before
  depended on Git Bash rewriting it, and when that rewriting misfired the container silently bound
  `\Program Files\Git\data` and wrote nothing to the intended directory at all — after which the test
  reads an empty directory and reports nonsense.
- **The third start waits for *listening*, not for healthy.** That container is degraded by design and
  `/health` returns 503, so polling for a 200 would spend the whole timeout before failing for the
  wrong reason. The first two waits poll for healthy, because they are not degraded.
- **The container must be stopped before the host writes to the database file**, and started again
  afterwards. Editing a SQLite file underneath a running process is a different test with a different
  outcome.
- **The backup count is compared before and after**, not asserted as an absolute.
- **A backup protects a specific risky action, not a startup.** One is taken before a migration, so a
  partial failure still leaves a working database, and before a reseed, for the same reason. They exist
  so a user can recover — which is why Reset is what the app offers after a failed migration. A startup
  with no migration pending and nothing to seed puts nothing at risk, so it takes none.

## Steps

### 1. Seed a fresh container against a bind-mounted data directory

```powershell
$dataDir = "$PWD\.claude\temp\qt-startup-01-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-01 --port 18401 --bind $dataDir

docker logs qt-startup-01 2>&1 | Select-String -SimpleMatch '[Database - Init]'
"backups=$(@(Get-ChildItem "$dataDir\backups\*.db" -ErrorAction SilentlyContinue).Count)"
```

**Expected:** the init log shows `schema created at baseline` (fresh database, baseline path), and
`backups=0` — the directory does not exist yet. A baseline run has nothing to lose, so no backup is
taken.

**On failure:** an empty host directory, or an init log that never mentions the baseline, means the
bind mount did not take effect. Stop: every step below reads and writes that directory, and against the
wrong one they report nonsense.

### 2. Restart unchanged — nothing is at risk, so nothing is backed up

```powershell
docker restart qt-startup-01
dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/health" --wait-for 200 --status

docker logs qt-startup-01 2>&1 | Select-String -SimpleMatch '[Database - Init]' | Select-Object -Last 3
"backups=$(@(Get-ChildItem "$dataDir\backups\*.db" -ErrorAction SilentlyContinue).Count)"
```

**Expected:** `schema is up to date`, and `backups=0` still. No migration is pending and the content
already exists, so neither risky action runs and there is nothing to protect against.

### 3. Break the schema on the host side, then restart

The container stays the one step 1 created — it is already bound to this directory, so nothing needs
re-running. It is stopped only so the host can write to the database file safely:

```powershell
docker stop qt-startup-01
dotnet script scripts/testing/execute-sql.csx -- `
  --db "$dataDir\quotinatordata.db" `
  --sql "PRAGMA foreign_keys=OFF; DROP TABLE Quotinator_Quote;"
docker start qt-startup-01
dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/health" --wait-for 503 --status

docker logs qt-startup-01 2>&1 | Select-Object -Last 20
"backups=$(@(Get-ChildItem "$dataDir\backups\*.db" -ErrorAction SilentlyContinue).Count)"
docker ps -a --filter name=qt-startup-01 --format "{{.Status}}"
```

**Expected:** the log shows, in order: `[Database - Backup] backup complete`;
`[Database - Init] seeding failed — restoring pre-seed backup, database left unchanged...` (ERR);
`[Database - Init] pre-seed backup restored.` (INF); then
`[Server] Database initialisation failed...` (CRIT/FTL) with the underlying
`SqliteException: ... no such table: Quotinator_Quote` attached as the log event's exception — **not**
a bare .NET unhandled-exception runtime dump.

`backups=1` — the first backup this test has produced, because step 2 correctly took none. This is the
case a backup exists for: seeding was about to run against a database it could not repair, and the
backup is what let it restore rather than leave a broken one behind. One file per `CreateBackup` call;
its `-shm`/`-wal` sidecars are not separate backups.

`docker ps -a` shows the container as `Up …`, **not** `Exited` — the app degrades, it does not crash.

**On failure:** an `Exited` container means the app crashed instead of degrading, which is the defect
this test exists to catch — and there is then no server left to answer the degraded-surface and Reset
steps below. Stop and record the exit rather than running them against nothing. The `--wait-for 503`
above fails within its own timeout in that case, rather than hanging.

### 4. Confirm the degraded surface

```powershell
$health = dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/health" --expect 503 | ConvertFrom-Json
"health status=$($health.status) reason=$($health.reason)"
$random = dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/quotes/random" --expect 503 | ConvertFrom-Json
"random status=$($random.status) reason=$($random.reason)"
```

**Expected:** `/health` returns `503` with `status=unhealthy` and a populated `reason`, not a bare
`200`. `/quotes/random` returns `503` with `status=unavailable` and its own `reason`, never a raw
exception.

### 5. Call Reset with a missing key while still degraded

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18401/api/v1/admin/database/reset" --no-key --expect 401 --status
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18401/api/v1/admin/database/reset" --api-key wrong-key --expect 401 --status
```

**Expected:** `401` both times, not `503` — confirming the health gate exempts `/api/v1/admin/*` from
the 503 gate entirely, rather than blocking the route and only letting an authenticated call through.

Both the missing key and the wrong key are tried: a route that answered `503` to one and `401` to the
other would still be broken, and testing only one could not tell.

### 6. Reset the database while degraded

```powershell
$reset = dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18401/api/v1/admin/database/reset" --expect 200 | ConvertFrom-Json
$reset | ConvertTo-Json -Depth 3
```

**Expected:** `200` with a row-count summary of **all zeros**. It performs its own independent schema
rebuild, unaffected by the degraded state, and no longer reimports bundled or user quote content
afterwards (#156).

### 7. Confirm the app recovers without a restart

```powershell
(dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/health" --expect 200 | ConvertFrom-Json).status
$after = dotnet script scripts/testing/http.csx -- --url "http://localhost:18401/api/v1/quotes/random" --expect 200 | ConvertFrom-Json
"status=$($after.status) items=$(@($after.items).Count)"
```

**Expected:** `/health` returns `200` and `healthy`, proving `DatabaseHealthState.MarkHealthy()` clears
the degraded state rather than requiring a process restart. `/quotes/random` returns `200`,
`status=NoResults` and `items=0` — not `503`, and not real quote data, because the database is
genuinely empty after a Reset.

## Observed effect

Well established here, unusually — the ordered log sequence above *is* the observed effect, and it is
what the assertions are made against.

**The three compounding gaps this came from**, all found during #254's own T1 pass:

1. Migration-version tracking detected a pending migration only by comparing recorded counts. Rewriting
   an unreleased migration's content in place — same slot, same final count — left an already-migrated
   database reading as "up to date" while its on-disk schema no longer matched. Seeding then crashed
   with no exception safety net, unlike the migration phase which already had one.
2. That uncaught exception propagated out of `Main` before Kestrel ever bound. Under IIS Express/ANCM
   it rendered a raw stack-trace page to whoever was looking at the browser. An initial fix caught it
   and exited the process cleanly — which broke the *only* documented remedy, since a fully-exited
   process has no server left to receive a Reset request.
3. Reset while degraded genuinely repairs the schema, but `DatabaseHealthState` is in-memory and does
   not observe that on its own. A first pass left the app reporting unhealthy forever after a
   successful Reset.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-01 --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```

This test's data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
