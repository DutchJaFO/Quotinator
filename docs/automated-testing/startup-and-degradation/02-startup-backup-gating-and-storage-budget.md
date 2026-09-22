# Startup backs up only when there is real work, and respects the storage budget

**Smoke:** no
**Environment:** Fresh
**Traces to:** #277

## Preconditions

**Beyond the profile.** The volume is *reused across restarts* rather than being a one-boot
environment — the whole test is about what successive startups against the same data directory do, and
a fresh container each time would never reach the states being checked. It therefore runs its own
container and volume (`qt-startup-02` / `qt-startup-02-data`), and the final step **re-enters** that
same volume with `Quotinator__MaxBackupStorageGb=0` set.

The sequence matters and each step depends on the one before it: fresh baseline → healthy restart →
Reset → restart-after-Reset → budget exceeded.

**A backup protects a specific risky action, not a startup.** One is taken before a migration and
before a reseed, so that a partial failure still leaves a working database and a user can recover —
which is why Reset is what the app offers after a failed migration. A startup with no migration pending
and nothing to seed puts nothing at risk. That is #277's gating, and it is why the counts below read
`0, 0, 1, 2, 2` rather than incrementing on every start.

## Determinism

- **Waits for health, not a duration**, at every start.
- **The backup count is cumulative and asserted at each step** (`0`, then `0`, then `1`, then `2`, then
  still `2`). Checking only the final number would not distinguish which startup took which backup.
- **The budget run re-enters the same volume rather than starting a second container.** The budget is
  configuration and cannot be applied to a running container, but `reenter` replaces the container
  while keeping the data — which is what this step needs, and it leaves exactly one container whose
  log cannot be confused with a sibling's. That is why the `docker logs --since` windows this document
  used are gone: there is no earlier container's output left to scope past.

## Steps

### 1. Install a fresh baseline

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-02 --port 18402
function Count-Thrown { @(docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count }
docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch 'Database - Backup'
```

**Expected:** no `[Database - Backup]` lines at all. Nothing exists to lose.

**On failure:** a backup line here means the volume was not new, so the run is not a baseline at all —
and the cumulative counts every later step asserts (`0`, `0`, `1`, `2`, `2`) are then measuring a
different sequence. Stop and remove the volume before re-running.

### 2. Restart while healthy

```powershell
"before the restart: thrown=$(Count-Thrown)"
docker restart qt-startup-02
dotnet script scripts/testing/http.csx -- --url "http://localhost:18402/api/v1/health" --wait-for 200 --status
$afterRestart = Count-Thrown

docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch 'Database - Backup', 'schema is up to date'
"backups=$(@(docker exec qt-startup-02 ls /data/backups 2>$null).Count)"
```

**Expected:** `thrown=0` before the restart, `schema is up to date`, **no** `[Database - Backup]` line, and `backups=0` — the
`/data/backups` directory does not even exist yet, which is why the listing is allowed to fail.

### 3. Reset the database

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18402/api/v1/admin/database/reset" --expect 200 | Out-Null
"backups=$(@(docker exec qt-startup-02 ls /data/backups 2>$null).Count)"
```

**Expected:** `backups=1`. Reset backs up unconditionally, being the highest-risk operation.

### 4. Restart immediately after the Reset

```powershell
"before the restart: new=$((Count-Thrown) - $afterRestart)"
docker restart qt-startup-02
dotnet script scripts/testing/http.csx -- --url "http://localhost:18402/api/v1/health" --wait-for 200 --status
$afterRestart = Count-Thrown

docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch 'Database - Backup'
"backups=$(@(docker exec qt-startup-02 ls /data/backups 2>$null).Count)"
```

**Expected:** `new=0` before the restart — counted from step 2's restart, whose own shutdown lines stay
in the log — then a `[Database - Backup]` line, and `backups=2`. Content-seed has real work to do again
(Quotes are empty) even though the schema itself needed no migration. **This is the exact case a
`MigrationApplied`-based gate was found to miss**, and the reason the gate is not based on it.

### 5. Reset again with the backup budget already exceeded

```powershell
"before the stop: new=$((Count-Thrown) - $afterRestart)"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-startup-02 --port 18402 `
  --env Quotinator__MaxBackupStorageGb=0

dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18402/api/v1/admin/database/reset" --expect 409
docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch 'reset refused'
"backups=$(@(docker exec qt-startup-02 ls /data/backups 2>$null).Count)"
"quotes still present = $((Invoke-RestMethod 'http://localhost:18402/api/v1/quotes?page=1&pageSize=1').totalCount)"
```

**Expected:** `new=0`, then `409` with `backupObstacle` `BudgetExceeded` and remedies naming the backup
endpoints and the quota setting; a `reset refused — no backup could be taken` warning, not an
exception; `backups=2` — unchanged from step 4; and a non-zero quote count, because the refused reset
changed nothing.

**Reset refuses rather than proceeding without a backup.** This step expected `200` with the backup
skipped until the full-quota work changed Reset to refuse whenever no backup can be taken, unless the
caller passes `allowNoBackup=true` — measured 2026-09-22: `409`, `backups=2`, `795` quotes still
present. [`../backup/05-a-full-quota-is-resolvable-from-inside-the-application.md`](../backup/05-a-full-quota-is-resolvable-from-inside-the-application.md)
asserts the remedies end to end; what this step keeps is that the budget is applied to a volume that
already holds backups.

**On failure:** `backups=3` means the budget was not applied. Check that `reenter` (not `create`) was
used — `create` would have wiped the volume and taken the count back to zero, which reads as a budget
failure while actually being a lost environment.

## Observed effect

Partially established. The backup counts and the presence or absence of the `[Database - Backup]` line
are observed state and are asserted above.

## Cleanup

```powershell
docker logs qt-startup-02 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-02
```

**Expected:** the read finds nothing — `reenter` replaced the container in step 5, so its log starts
there, and the refusal is a warning.
