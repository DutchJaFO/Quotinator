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
- **Copy the `-wal` and `-shm` sidecars** with every `.db` copy. SQLite does not checkpoint recent
  writes into the main file until the WAL passes its threshold, so the `.db` alone can be missing
  exactly what was just written.
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

```bash
docker rm -f qt-db-02-default 2>/dev/null; docker volume rm qt-db-02-default-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-db-02-default -p 18302:8080 -v qt-db-02-default-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true quotinator:local
until curl -sf http://localhost:18302/api/v1/health > /dev/null; do sleep 1; done
docker logs qt-db-02-default 2>&1 | grep -c "Quotinator ready"
```

**Expected:** `1`. Counted rather than eyeballed — a `tail` of the log is read by a human deciding
whether it looks finished, which is not a condition that can fail.

**On failure:** the audit activity every check below reads is produced by that seeding. A container
that has not finished has nothing to export or date-range, and an empty result would read as an
endpoint defect. Stop.

### 2. Discover the audit date range

```bash
curl -s "http://localhost:18302/api/v1/admin/audit/date-range"
```

**Expected:** `200` with non-null `earliestDate`/`latestDate`, from the bundled seed's own
`BulkInserted` entries. No `X-Api-Key` required, matching `GET /admin/audit`.

### 3. Export the audit trail as a downloaded file

```bash
curl -s -D - "http://localhost:18302/api/v1/admin/audit/export" -o .claude/temp/audit-export.json | grep -i content-disposition
cat .claude/temp/audit-export.json | head -c 300
```

**Expected:** headers include
`Content-Disposition: attachment; filename="quotinator-audit-export-...json"`. The body has top-level
`entries` and `changes` arrays, both non-empty after a fresh seed.

### 4. Reject an export above the row-count cap

A separate container, because the cap is configuration:

```bash
docker rm -f qt-db-02-default; docker volume rm qt-db-02-default-data
docker rm -f qt-db-02-cap 2>/dev/null; docker volume rm qt-db-02-cap-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-db-02-cap -p 19302:8080 -v qt-db-02-cap-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AdminAuditExportMaxRows=1 quotinator:local
until curl -sf http://localhost:19302/api/v1/health > /dev/null; do sleep 1; done
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:19302/api/v1/admin/audit/export"
docker rm -f qt-db-02-cap; docker volume rm qt-db-02-cap-data
```

**Expected:** `422`, never a silently truncated file. A fresh seed produces far more than one combined
row.

### 5. Capture the auto-purge-on database and count the remaining actions

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qt-db-02-default -p 18302:8080 -v qt-db-02-default-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=true quotinator:local
until curl -sf http://localhost:18302/api/v1/health > /dev/null; do sleep 1; done
docker stop -t 15 qt-db-02-default
MSYS_NO_PATHCONV=1 docker cp qt-db-02-default:/data/quotinatordata.db .claude/temp/smoke249.db
MSYS_NO_PATHCONV=1 docker cp qt-db-02-default:/data/quotinatordata.db-wal .claude/temp/smoke249.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-db-02-default:/data/quotinatordata.db-shm .claude/temp/smoke249.db-shm
docker start qt-db-02-default
until curl -sf http://localhost:18302/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db \
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"
```

**Expected:** `RemainingActions` is `0`: every bundled batch applies cleanly with no pending actions,
so all of them are auto-purged.

`RemainingActions = 0` is the zero-failures assertion in this test: nothing left `Pending`, `Blocked`
or `Stale` after a bundled seed.

### 6. Count the purge traces left behind

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db \
  --sql "SELECT COUNT(*) AS PurgeTraces FROM Audit_Entry WHERE TableName = 'Import_Action' AND Operation = 'Purged'"
```

**Expected:** `PurgeTraces` equals the number of bundled seed batches — one per batch, even though the
`Import_Action` rows themselves are gone.

### 7. Run the same seed with auto-purge disabled

A fresh container, no prior data:

```bash
docker stop qt-db-02-default
docker rm -f qt-db-02-noautopurge 2>/dev/null; docker volume rm qt-db-02-noautopurge-data 2>/dev/null
MSYS_NO_PATHCONV=1 docker run -d --name qt-db-02-noautopurge -p 20302:8080 -v qt-db-02-noautopurge-data:/data \
  -e Quotinator__DataDir=/data -e Quotinator__AdminApiKey=<your admin key> \
  -e Quotinator__AutoPurgeBundledImportActions=false quotinator:local
until curl -sf http://localhost:20302/api/v1/health > /dev/null; do sleep 1; done
docker stop -t 15 qt-db-02-noautopurge
MSYS_NO_PATHCONV=1 docker cp qt-db-02-noautopurge:/data/quotinatordata.db .claude/temp/smoke249b.db
MSYS_NO_PATHCONV=1 docker cp qt-db-02-noautopurge:/data/quotinatordata.db-wal .claude/temp/smoke249b.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-db-02-noautopurge:/data/quotinatordata.db-shm .claude/temp/smoke249b.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249b.db \
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"
docker rm -f qt-db-02-noautopurge; docker volume rm qt-db-02-noautopurge-data
docker start qt-db-02-default
until curl -sf http://localhost:18302/api/v1/health > /dev/null; do sleep 1; done
```

**Expected:** `RemainingActions` is greater than `0`. With the bundled setting off the seeding path
never purges, matching pre-#249 behaviour.

**`qt-db-02-default` is stopped rather than removed here**, because the `purgeOnSuccess` step below runs
against it and needs the same database this check just read.

### 8. Import with `purgeOnSuccess=true` on a live import

Using `qt-db-02-default` from the auto-purge check, still running:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@data/sources/quotinator-curated.json" \
  "http://localhost:18302/api/v1/import?purgeOnSuccess=true"
```

**Expected:** the import returns `200` (the curated file re-imports as all-Modify against
already-seeded data, no pending decisions).

### 9. Attempt to reverse that batch

Note the response's `batchId`, then:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18302/api/v1/import/actions/reverse?batchId=<batchId-from-above>"
```

**Expected:** `422`: the batch's `Import_Action` rows were purged immediately, so `ReverseBatchAsync`
has nothing to reverse.

### 10. Clear the audit trail unscoped

```bash
curl -s -X DELETE -H "X-Api-Key: <your admin key>" "http://localhost:18302/api/v1/admin/audit"
```

**Expected:** `204`.

### 11. Re-read the date range after the unscoped clear

```bash
curl -s "http://localhost:18302/api/v1/admin/audit/date-range"
```

**Expected:** `earliestDate`/`latestDate` match *only* the clear's own self-recorded `Purged` trace — a
single, just-now timestamp — not any earlier `Audit_Change` activity, which is now also gone.

### 12. Confirm a table-scoped clear leaves `Audit_Change` untouched

Run `DELETE .../admin/audit?table=Quotinator_Quote` and check `SELECT COUNT(*) FROM Audit_Change` via
DbInspector before and after.

**Expected:** the count is unchanged — a table-scoped clear leaves `Audit_Change` untouched.

## Observed effect

Partially established. The raw table counts and the purge traces are observed state and are asserted
above.

**The unscoped-clear behaviour was found live during T1**: date-range and export kept showing data
after a clear, because the endpoint had only ever cleared `Audit_Entry` — even though #249 treats both
tables as one combined concern everywhere else.

## Cleanup

```bash
docker rm -f qt-db-02-default qt-db-02-cap qt-db-02-noautopurge 2>/dev/null
docker volume rm qt-db-02-default-data qt-db-02-cap-data qt-db-02-noautopurge-data 2>/dev/null
rm -f .claude/temp/smoke249.db .claude/temp/smoke249.db-wal .claude/temp/smoke249.db-shm \
      .claude/temp/smoke249b.db .claude/temp/smoke249b.db-wal .claude/temp/smoke249b.db-shm \
      .claude/temp/audit-export.json
```
