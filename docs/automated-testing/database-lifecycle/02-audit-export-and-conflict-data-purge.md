# Audit export, date-range discovery, and conflict-resolution data auto-purge

**Smoke:** no
**Environment:** Fresh
**Traces to:** #249

## Preconditions

**Beyond the profile.** This test runs **four containers of its own**, because
each needs a different configuration and a configuration is fixed at start — a restart cannot supply
it. Everything else about each run is Fresh: same image, same first-boot seed, same readiness poll. The
audit activity it reads is produced by that seeding, so nothing needs importing first.

**One is a deliberate departure from the profile's settings:** `qt-db-02-noautopurge` sets
`Quotinator__AutoPurgeBundledImportActions=false`, where Fresh pins the application default `true`.
Comparing the two is the point of the test, which is why both appear explicitly rather than one being
left to a default.

`Quotinator.Tools.DbInspector` (read-only) is used for the raw-table checks.

## Determinism

- **Waits for health, not a duration**, at every container start.
- **Copy the `-wal` and `-shm` sidecars** with every `.db` copy, *where they exist*. SQLite does not
  checkpoint recent writes into the main file until the WAL passes its threshold, so the `.db` alone
  can be missing exactly what was just written. After a clean `docker stop` they are usually gone —
  the close checkpointed and removed them — which is why each sidecar copy ends `2>$null` and why
  their absence is not a warning sign. See the index's *Snapshot and restore*.
- **Each configuration gets a fresh container with no prior data.** The auto-purge-off run in
  particular is meaningless against a volume where the on-by-default run already purged.
- **`PurgeTraces` equals the number of bundled seed batches** — one per batch. Derive it from the batch
  count in the same run rather than fixing a number: the bundled file set changes, and this is a
  relationship, not a prediction.
- **The container side is the ordinary `8080`, not the ingress port `8099`.** These containers
  published `8099` while the suite shared one environment, so that this test could run alongside it;
  every test now owns its container and its own host port, so nothing here depends on the ingress port
  any more.

## Steps

### 1. Start the default container, both auto-purge settings on

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-default --port 18302
([regex]::Matches((docker logs qt-db-02-default 2>&1 | Out-String), 'Quotinator ready')).Count
```

**Expected:** `1`. Counted rather than eyeballed — reading the tail of a log and deciding whether it
looks finished is not a condition that can fail.

**On failure:** the audit activity every check below reads is produced by that seeding. A container
that has not finished has nothing to export or date-range, and an empty result would read as an
endpoint defect. Stop.

### 2. Discover the audit date range

```powershell
$range = dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/admin/audit/date-range" --no-key --expect 200 | ConvertFrom-Json
"earliest=$($range.earliestDate) latest=$($range.latestDate)"
```

**Expected:** `200` with non-null `earliestDate`/`latestDate`, from the bundled seed's own
`BulkInserted` entries. Called with `--no-key` deliberately: no `X-Api-Key` is required here, matching
`GET /admin/audit`, and sending one would leave that untested.

### 3. Export the audit trail as a downloaded file

```powershell
$export = Invoke-WebRequest "http://localhost:18302/api/v1/admin/audit/export" -UseBasicParsing
$export.Headers['Content-Disposition']
$body = $export.Content | ConvertFrom-Json
"entries=$(@($body.entries).Count) changes=$(@($body.changes).Count)"
```

**Expected:** the header reads
`attachment; filename="quotinator-audit-export-...json"`, and both `entries` and `changes` are
non-empty after a fresh seed.

### 4. Reject an export above the row-count cap

A separate container, because the cap is configuration:

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-default
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-cap --port 19302 `
  --env Quotinator__AdminAuditExportMaxRows=1
dotnet script scripts/testing/http.csx -- --url "http://localhost:19302/api/v1/admin/audit/export" --expect 422 --status
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-cap
```

**Expected:** `422`, never a silently truncated file. A fresh seed produces far more than one combined
row.

### 5. Capture the auto-purge-on database and count the remaining actions

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-default --port 18302
docker stop -t 15 qt-db-02-default
docker cp qt-db-02-default:/data/quotinatordata.db .claude/temp/smoke249.db
docker cp qt-db-02-default:/data/quotinatordata.db-wal .claude/temp/smoke249.db-wal 2>$null
docker cp qt-db-02-default:/data/quotinatordata.db-shm .claude/temp/smoke249.db-shm 2>$null
docker start qt-db-02-default
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/health" --wait-for 200 --status

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db `
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"
```

**Expected:** `RemainingActions` is `0`: every bundled batch applies cleanly with no pending actions,
so all of them are auto-purged.

`RemainingActions = 0` is the zero-failures assertion in this test: nothing left `Pending`, `Blocked`
or `Stale` after a bundled seed.

### 6. Count the purge traces left behind

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db `
  --sql "SELECT COUNT(*) AS PurgeTraces FROM Audit_Entry WHERE TableName = 'Import_Action' AND Operation = 'Purged'"
(Invoke-RestMethod "http://localhost:18302/api/v1/import/batches?type=seed").totalCount
```

**Expected:** `PurgeTraces` equals the seed-batch count printed beneath it — one trace per batch, even
though the `Import_Action` rows themselves are gone. Derived in the same run rather than written here,
per Determinism.

### 7. Run the same seed with auto-purge disabled

A fresh container, no prior data:

```powershell
docker stop qt-db-02-default
dotnet script scripts/testing/test-env.csx -- create --name qt-db-02-noautopurge --port 20302 `
  --env Quotinator__AutoPurgeBundledImportActions=false
docker stop -t 15 qt-db-02-noautopurge
docker cp qt-db-02-noautopurge:/data/quotinatordata.db .claude/temp/smoke249b.db
docker cp qt-db-02-noautopurge:/data/quotinatordata.db-wal .claude/temp/smoke249b.db-wal 2>$null
docker cp qt-db-02-noautopurge:/data/quotinatordata.db-shm .claude/temp/smoke249b.db-shm 2>$null

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249b.db `
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"

dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-noautopurge
docker start qt-db-02-default
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/health" --wait-for 200 --status
```

**Expected:** `RemainingActions` is greater than `0`. With the bundled setting off the seeding path
never purges, matching pre-#249 behaviour.

**`qt-db-02-default` is stopped rather than removed here**, because the `purgeOnSuccess` step below runs
against it and needs the same database this check just read.

### 8. Import with `purgeOnSuccess=true` on a live import

Using `qt-db-02-default` from the auto-purge check, still running:

```powershell
$purgedBatchId = (dotnet script scripts/testing/http.csx -- --method POST `
                    --url "http://localhost:18302/api/v1/import?purgeOnSuccess=true" `
                    --file data/sources/quotinator-curated.json --expect 200 | ConvertFrom-Json).batchId
$purgedBatchId
```

**Expected:** `200` and a non-empty batch id — the curated file re-imports as all-Modify against
already-seeded data, with no pending decisions to stage.

### 9. Attempt to reverse that batch

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "http://localhost:18302/api/v1/import/actions/reverse?batchId=$purgedBatchId" --expect 422 --status
```

**Expected:** `422`: the batch's `Import_Action` rows were purged immediately, so `ReverseBatchAsync`
has nothing to reverse.

### 10. Clear the audit trail unscoped

```powershell
dotnet script scripts/testing/http.csx -- --method DELETE --url "http://localhost:18302/api/v1/admin/audit" --expect 204 --status
```

**Expected:** `204`.

### 11. Re-read the date range after the unscoped clear

```powershell
$after = Invoke-RestMethod "http://localhost:18302/api/v1/admin/audit/date-range"
"earliest=$($after.earliestDate) latest=$($after.latestDate)"
"secondsOld=$([int]((Get-Date).ToUniversalTime() - [datetime]$after.earliestDate).TotalSeconds)"
```

**Expected:** `earliestDate` and `latestDate` are the same just-now timestamp — the clear's own
self-recorded `Purged` trace, seconds old — and not the seed's much earlier activity, which is now gone
along with the `Audit_Change` rows. The age is printed rather than the range being eyeballed, so "still
showing the old data" fails rather than being read past.

### 12. Confirm a table-scoped clear leaves `Audit_Change` untouched

**Step 10's unscoped clear emptied `Audit_Change`, so this step has to put rows back first.** Import
the curated file again to generate them, read the count, run the scoped clear, and read it again:

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18302/api/v1/import" `
  --file data/sources/quotinator-curated.json --expect 200 | Out-Null
docker stop -t 15 qt-db-02-default
docker cp qt-db-02-default:/data/quotinatordata.db .claude/temp/smoke249c.db
docker start qt-db-02-default
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/health" --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249c.db `
  --sql "SELECT COUNT(*) AS ChangesBefore FROM Audit_Change"

dotnet script scripts/testing/http.csx -- --method DELETE `
  --url "http://localhost:18302/api/v1/admin/audit?table=Quotinator_Quote" --expect 204 | Out-Null
docker stop -t 15 qt-db-02-default
docker cp qt-db-02-default:/data/quotinatordata.db .claude/temp/smoke249d.db
docker start qt-db-02-default
dotnet script scripts/testing/http.csx -- --url "http://localhost:18302/api/v1/health" --wait-for 200 --status
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249d.db `
  --sql "SELECT COUNT(*) AS ChangesAfter FROM Audit_Change"
```

**Expected:** `ChangesBefore` is **non-zero**, and `ChangesAfter` equals it. A table-scoped clear
leaves `Audit_Change` untouched.

**The non-zero before-reading is what makes this an assertion at all.** Run straight after step 10 both
readings are `0`, and "the count is unchanged" then holds equally well when a scoped clear wrongly
wipes the table — which is how this step read until #339's full run. Measured with 13 real change rows
present: the scoped clear left all 13, so the behaviour is correct and only the ordering was hiding it.

**On failure:** a zero `ChangesBefore` means the import produced no change rows, so the comparison
proves nothing either way. Stop rather than recording a pass.

## Observed effect

Partially established. The raw table counts and the purge traces are observed state and are asserted
above.

**The unscoped-clear behaviour was found live during T1**: date-range and export kept showing data
after a clear, because the endpoint had only ever cleared `Audit_Entry` — even though #249 treats both
tables as one combined concern everywhere else.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-default
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-cap
dotnet script scripts/testing/test-env.csx -- destroy --name qt-db-02-noautopurge
Remove-Item .claude/temp/smoke249.db, .claude/temp/smoke249.db-wal, .claude/temp/smoke249.db-shm, `
            .claude/temp/smoke249b.db, .claude/temp/smoke249b.db-wal, .claude/temp/smoke249b.db-shm, `
            .claude/temp/smoke249c.db, .claude/temp/smoke249d.db `
            -ErrorAction SilentlyContinue
```
