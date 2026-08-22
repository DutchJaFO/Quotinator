# Startup backs up only when there is real work, and respects the storage budget

**Smoke:** no
**Environment:** Fresh
**Traces to:** #277

## Preconditions

A named volume, reused across restarts — the whole test is about what successive startups against the
*same* data directory do. A fresh container each time would never reach the states being checked.

The sequence matters and each step depends on the one before it: fresh baseline → healthy restart →
Reset → restart-after-Reset → budget exceeded.

## Determinism

- **Waits for health, not a duration**, at every start except where noted.
- **The backup count is cumulative and asserted at each step** (`0`, then `0`, then `1`, then `2`, then
  still `2`). Checking only the final number would not distinguish which startup took which backup.
- **`docker logs --since` windows are generous** relative to the poll, so a fast startup does not push
  the lines being grepped outside the window.
- The budget run needs its **own container with `Quotinator__MaxBackupStorageGb=0`** — the budget is
  configuration, not runtime state, so it cannot be applied to the running container.

## Steps

**Fresh baseline install:**

```bash
docker volume rm smoke277-data 2>/dev/null
docker rm -f smoke277
MSYS_NO_PATHCONV=1 docker run -d --name smoke277 -p 8080:8080 -v smoke277-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke277 2>&1 | grep "Database - Backup"
```

**Healthy restart:**

```bash
docker restart smoke277
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke277 --since 60s 2>&1 | grep "Database - Backup\|schema is up to date"
docker exec smoke277 sh -c "ls /data/backups 2>&1 || echo 'no backups dir — correct'"
```

**Reset:**

```bash
curl -s -X POST -H "X-Api-Key: smoketest" "http://localhost:8080/api/v1/admin/database/reset"
docker exec smoke277 sh -c "ls /data/backups | wc -l"
```

**Restart immediately after the Reset:**

```bash
docker restart smoke277
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke277 --since 60s 2>&1 | grep "Database - Backup"
docker exec smoke277 sh -c "ls /data/backups | wc -l"
```

**Budget already exceeded — a separate container:**

```bash
docker rm -f smoke277
docker run -d --name smoke277 -p 8080:8080 -v smoke277-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=smoketest -e Quotinator__MaxBackupStorageGb=0 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s -X POST -H "X-Api-Key: smoketest" "http://localhost:8080/api/v1/admin/database/reset"
docker logs smoke277 --since 60s 2>&1 | grep "LogBackupSkippedBudgetExceeded"
docker exec smoke277 sh -c "ls /data/backups | wc -l"
```

## Expected output

**Fresh baseline** — no `[Database - Backup]` lines at all. Nothing exists to lose.

**Healthy restart** — `schema is up to date` and **no** `[Database - Backup]` line. `/data/backups`
should not even exist yet.

**Reset** — exactly one backup. Reset backs up unconditionally, being the highest-risk operation.

**Restart after Reset** — takes a backup too, bringing the count to `2`. Content-seed has real work to
do again (Quotes are empty) even though the schema itself needed no migration. **This is the exact case
a `MigrationApplied`-based gate was found to miss**, and the reason the gate is not based on it.

**Budget exceeded** — Reset still succeeds (`200`, database rebuilt). The backup is skipped with a
warning log, not an exception, and the count stays at `2`.

## Observed effect

Partially established. The backup counts and the presence or absence of the `[Database - Backup]` line
are observed state and are asserted above.

## Cleanup

```bash
docker rm -f smoke277
docker volume rm smoke277-data
```
