# Seeding takes a backup, a broken schema degrades rather than crashes, and Reset recovers

**Smoke:** no
**Environment:** Fresh + Constrained
**Traces to:** #254

## Preconditions

**Beyond the profile.** The data directory is a **bind mount** instead of the profile's named volume,
so the host can manipulate the SQLite file directly — the whole test turns on breaking the schema from
outside the container. It runs its own container (`qt-startup-01`, started three times against that one
directory), and the Constrained defect is a `DROP TABLE Quotinator_Quote` applied
by the host while the container is stopped.

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

- **`MSYS_NO_PATHCONV=1` and an explicit Windows-style source path are required under Git Bash.**
  Without them Git Bash's POSIX-to-Windows path conversion mangles the `-v` argument — confirmed live:
  `$(pwd)/...:/data` silently became a bind mount to `\Program Files\Git\data`, and the container wrote
  nothing to the intended host directory at all. The test then reads an empty directory and reports
  nonsense.
- **The third start waits for *listening*, not for healthy.** That container is degraded by design and
  `/health` returns 503, so polling for a 200 would loop forever. The first two waits poll for healthy,
  because they are not degraded.
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

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-01 --port 18401 \
  --bind .claude/temp/qt-startup-01-data
docker logs qt-startup-01 2>&1 | grep "\[Database - Init\]"
ls .claude/temp/qt-startup-01-data/backups/ 2>/dev/null
```

**Expected:** the init log shows `schema created at baseline` (fresh database, baseline path), and
`backups/` does not exist or is empty. A baseline run has nothing to lose, so no backup is taken.

**On failure:** an empty host directory, or an init log that never mentions the baseline, means the
bind mount did not take effect — see the `MSYS_NO_PATHCONV` note in Determinism. Stop: every step
below reads and writes that directory, and against the wrong one they report nonsense.

### 2. Restart unchanged — nothing is at risk, so nothing is backed up

```bash
docker restart qt-startup-01
until curl -sf http://localhost:18401/api/v1/health > /dev/null; do sleep 1; done
docker logs qt-startup-01 2>&1 | grep "\[Database - Init\]" | tail -3
ls .claude/temp/qt-startup-01-data/backups/*.db 2>/dev/null | wc -l
```

**Expected:** `schema is up to date`, and the backup count is still `0`. No migration is pending and
the content already exists, so neither risky action runs and there is nothing to protect against.

### 3. Break the schema on the host side, then restart

This start is a raw `docker run` rather than a `create`, because it must mount the directory the
earlier steps already seeded — `create` always starts from a clean one:

```bash
docker rm -f qt-startup-01
MSYS_NO_PATHCONV=1 docker run -d --name qt-startup-01 -p 18401:8080 \
  -v "C:/repos/Quotinator/.claude/temp/qt-startup-01-data:/data" \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest quotinator:local
until curl -sf http://localhost:18401/api/v1/health > /dev/null; do sleep 1; done
docker stop qt-startup-01
dotnet script scripts/testing/execute-sql.csx -- \
  --db .claude/temp/qt-startup-01-data/quotinatordata.db \
  --sql "PRAGMA foreign_keys=OFF; DROP TABLE Quotinator_Quote;"
docker start qt-startup-01
until curl -s -o /dev/null http://localhost:18401/api/v1/health; do sleep 1; done
docker logs qt-startup-01 2>&1 | tail -20
ls .claude/temp/qt-startup-01-data/backups/*.db 2>/dev/null | wc -l
docker ps -a --filter name=qt-startup-01 --format "{{.Status}}"
```

**Expected:** the log shows, in order: `[Database - Backup] backup complete`;
`[Database - Init] seeding failed — restoring pre-seed backup, database left unchanged...` (ERR);
`[Database - Init] pre-seed backup restored.` (INF); then
`[Server] Database initialisation failed...` (CRIT/FTL) with the underlying
`SqliteException: ... no such table: Quotinator_Quote` attached as the log event's exception — **not**
a bare .NET unhandled-exception runtime dump.

The backup count reads `1` — the first backup this test has produced, because step 2 correctly took
none. This is the case a backup exists for: seeding was about to run against a database it could not
repair, and the backup is what let it restore rather than leave a broken one behind. One file per
`CreateBackup` call; its `-shm`/`-wal` sidecars are not separate backups.

`docker ps -a` shows the container as `Up …`, **not** `Exited` — the app degrades, it does not crash.

**On failure:** an `Exited` container means the app crashed instead of degrading, which is the defect
this test exists to catch — and there is then no server left to answer the degraded-surface and Reset
steps below. Stop and record the exit rather than running them against nothing.

### 4. Confirm the degraded surface

```bash
curl -s -w " [%{http_code}]\n" http://localhost:18401/api/v1/health
curl -s -w " [%{http_code}]\n" http://localhost:18401/api/v1/quotes/random
```

**Expected:** `/health` returns `503` with `{"status":"unhealthy","reason":"..."}`, not a bare `200`.
`/quotes/random` returns `503` with `{"status":"unavailable","reason":"..."}`, never a raw exception.

### 5. Call Reset with a wrong or missing key while still degraded

Call Reset with a wrong or missing `X-Api-Key` while still degraded.

**Expected:** `401`, not `503`, confirming the health gate exempts `/api/v1/admin/*` from the 503 gate
entirely rather than blocking the route and only letting an authenticated call through.

### 6. Reset the database while degraded

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  http://localhost:18401/api/v1/admin/database/reset -o /dev/null
```

**Expected:** `200` with a row-count summary of **all zeros**. It performs its own independent schema
rebuild, unaffected by the degraded state, and no longer reimports bundled or user quote content
afterwards (#156).

### 7. Confirm the app recovers without a restart

```bash
curl -s -w " [%{http_code}]\n" http://localhost:18401/api/v1/health
curl -s -w " [%{http_code}]\n" http://localhost:18401/api/v1/quotes/random
```

**Expected:** `/health` returns `200` with `{"status":"healthy"}`, proving
`DatabaseHealthState.MarkHealthy()` clears the degraded state rather than requiring a process restart.
`/quotes/random` returns `200` with `{"status":"NoResults", ...}` and an empty `items` array — not
`503`, and not real quote data, because the database is genuinely empty after a Reset.

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

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-01 \
  --bind .claude/temp/qt-startup-01-data
```

This test's data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
