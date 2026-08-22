# Seeding takes a backup, a broken schema degrades rather than crashes, and Reset recovers

**Smoke:** no
**Environment:** Fresh + Constrained
**Traces to:** #254

## Preconditions

A **bind-mounted** data directory, so the host can manipulate the SQLite file directly. A named volume
will not do — the whole test turns on breaking the schema from outside the container.

`Quotinator.Tools.DbInspector` cannot be used here: it opens read-only (`Mode=ReadOnly`, see its
`README.md`) and cannot run the `DROP TABLE` this needs. `scripts/execute-sql.csx` is the writable
counterpart and opens a normal connection.

Three separate startup states are exercised in sequence, and each depends on the previous one having
reached its own end state:

1. a fresh baseline seed (no backup expected)
2. an ordinary restart (backup expected)
3. a deliberately broken schema (degraded, backup taken, restore attempted)

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
- **The backup count is compared before and after**, not asserted as an absolute — every non-baseline
  startup adds one.

## Steps

**Fresh container, bind-mounted data directory, normal seed:**

```bash
mkdir -p .claude/temp/smoke-254-data
MSYS_NO_PATHCONV=1 docker run -d --name smoke254 -p 8080:8080 \
  -v "C:/repos/Quotinator/.claude/temp/smoke-254-data:/data" \
  -e Quotinator__DataDir=/data quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke254 2>&1 | grep "\[Database - Init\]"
ls .claude/temp/smoke-254-data/backups/ 2>/dev/null
```

**Restart unchanged — an ordinary restart takes a backup too:**

```bash
docker restart smoke254
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke254 2>&1 | grep "\[Database - Init\]" | tail -3
ls .claude/temp/smoke-254-data/backups/*.db 2>/dev/null | wc -l
```

**Break the schema on the host side, then restart.** Start with an admin key this time — the Reset
call below needs it:

```bash
docker rm -f smoke254
MSYS_NO_PATHCONV=1 docker run -d --name smoke254 -p 8080:8080 \
  -v "C:/repos/Quotinator/.claude/temp/smoke-254-data:/data" \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker stop smoke254
dotnet script scripts/execute-sql.csx -- \
  --db .claude/temp/smoke-254-data/quotinatordata.db \
  --sql "PRAGMA foreign_keys=OFF; DROP TABLE Quotinator_Quote;"
docker start smoke254
until curl -s -o /dev/null http://localhost:8080/api/v1/health; do sleep 1; done
docker logs smoke254 2>&1 | tail -20
ls .claude/temp/smoke-254-data/backups/*.db 2>/dev/null | wc -l
docker ps -a --filter name=smoke254 --format "{{.Status}}"
```

**Confirm the degraded surface, then Reset and confirm it recovers:**

```bash
curl -s -w " [%{http_code}]\n" http://localhost:8080/api/v1/health
curl -s -w " [%{http_code}]\n" http://localhost:8080/api/v1/quotes/random
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  http://localhost:8080/api/v1/admin/database/reset -o /dev/null
curl -s -w " [%{http_code}]\n" http://localhost:8080/api/v1/health
curl -s -w " [%{http_code}]\n" http://localhost:8080/api/v1/quotes/random
```

Also call Reset with a wrong or missing `X-Api-Key` while still degraded.

## Expected output

**Fresh seed** — the init log shows `schema created at baseline` (fresh database, baseline path), and
`backups/` does not exist or is empty. A baseline run has nothing to lose, so no backup is taken.

**Restart** — `schema is up to date`, and the backup count is now `1`. This is a deliberately chosen
tradeoff, not a bug: every non-baseline startup backs up before seeding, because seeding has no
cheaper "is there real work to do" signal to gate on the way migrations do. A version-count check
alone is exactly what missed the schema/version mismatch this fix exists to protect against. Only the
first baseline run is skipped, which the previous step confirmed.

**Broken schema** — the log shows, in order: `[Database - Backup] backup complete`;
`[Database - Init] seeding failed — restoring pre-seed backup, database left unchanged...` (ERR);
`[Database - Init] pre-seed backup restored.` (INF); then
`[Server] Database initialisation failed...` (CRIT/FTL) with the underlying
`SqliteException: ... no such table: Quotinator_Quote` attached as the log event's exception — **not**
a bare .NET unhandled-exception runtime dump.

At least one new backup `.db` exists (one per `CreateBackup` call; its `-shm`/`-wal` sidecars are not
separate backups). `docker ps -a` shows the container as `Up …`, **not** `Exited` — the app degrades,
it does not crash.

**Degraded surface** — `/health` returns `503` with `{"status":"unhealthy","reason":"..."}`, not a bare
`200`. `/quotes/random` returns `503` with `{"status":"unavailable","reason":"..."}`, never a raw
exception. A Reset with a wrong or missing key returns `401`, not `503`, confirming the health gate
exempts `/api/v1/admin/*` from the 503 gate entirely rather than blocking the route and only letting an
authenticated call through.

**Reset** — returns `200` with a row-count summary of **all zeros**. It performs its own independent
schema rebuild, unaffected by the degraded state, and no longer reimports bundled or user quote content
afterwards (#156).

**Recovery** — `/health` returns `200` with `{"status":"healthy"}`, proving
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
docker rm -f smoke254
rm -rf .claude/temp/smoke-254-data
```
