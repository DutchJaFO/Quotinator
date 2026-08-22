# Audit export, date-range discovery, and conflict-resolution data auto-purge

**Smoke:** no
**Traces to:** #249

## Preconditions

A container started fresh and allowed to seed normally — the audit activity this test reads is
produced by seeding itself.

`Quotinator.Tools.DbInspector` (read-only) is used for the raw-table checks.

Four of the runs below need a **different container configuration**, not a restart of the same one.
They are listed as separate starts for that reason, not out of caution.

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

## Steps

**Defaults, both auto-purge settings on:**

```bash
docker run -d --name smoke249 -p 18099:8099 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:18099/api/v1/health > /dev/null; do sleep 1; done
docker logs smoke249 2>&1 | tail -5
```

**Date-range discovery:**

```bash
curl -s "http://localhost:18099/api/v1/admin/audit/date-range"
```

**Bulk export, as a downloaded file:**

```bash
curl -s -D - "http://localhost:18099/api/v1/admin/audit/export" -o .claude/temp/audit-export.json | grep -i content-disposition
cat .claude/temp/audit-export.json | head -c 300
```

**Row-count cap — a separate container, because the cap is configuration:**

```bash
docker rm -f smoke249
docker run -d --name smoke249cap -p 18099:8099 -e Quotinator__AdminApiKey=<your admin key> -e Quotinator__AdminAuditExportMaxRows=1 quotinator:local
until curl -sf http://localhost:18099/api/v1/health > /dev/null; do sleep 1; done
curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:18099/api/v1/admin/audit/export"
docker rm -f smoke249cap
```

**Auto-purge on by default:**

```bash
docker run -d --name smoke249 -p 18099:8099 -e Quotinator__AdminApiKey=<your admin key> quotinator:local
until curl -sf http://localhost:18099/api/v1/health > /dev/null; do sleep 1; done
MSYS_NO_PATHCONV=1 docker cp smoke249:/app/data/quotinatordata.db .claude/temp/smoke249.db
MSYS_NO_PATHCONV=1 docker cp smoke249:/app/data/quotinatordata.db-wal .claude/temp/smoke249.db-wal
MSYS_NO_PATHCONV=1 docker cp smoke249:/app/data/quotinatordata.db-shm .claude/temp/smoke249.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db \
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249.db \
  --sql "SELECT COUNT(*) AS PurgeTraces FROM Audit_Entry WHERE TableName = 'Import_Action' AND Operation = 'Purged'"
```

**Auto-purge disabled — fresh container, no prior data:**

```bash
docker rm -f smoke249
docker run -d --name smoke249noautopurge -p 18099:8099 -e Quotinator__AdminApiKey=<your admin key> -e Quotinator__AutoPurgeBundledImportActions=false quotinator:local
until curl -sf http://localhost:18099/api/v1/health > /dev/null; do sleep 1; done
MSYS_NO_PATHCONV=1 docker cp smoke249noautopurge:/app/data/quotinatordata.db .claude/temp/smoke249b.db
MSYS_NO_PATHCONV=1 docker cp smoke249noautopurge:/app/data/quotinatordata.db-wal .claude/temp/smoke249b.db-wal
MSYS_NO_PATHCONV=1 docker cp smoke249noautopurge:/app/data/quotinatordata.db-shm .claude/temp/smoke249b.db-shm
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke249b.db \
  --sql "SELECT COUNT(*) AS RemainingActions FROM Import_Action"
docker rm -f smoke249noautopurge
```

**`purgeOnSuccess` on a live import** — using `smoke249` from the auto-purge check, still running:

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" -F "file=@data/sources/quotinator-curated.json" \
  "http://localhost:18099/api/v1/import?purgeOnSuccess=true"
```

Note the response's `batchId`, then:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:18099/api/v1/import/actions/reverse?batchId=<batchId-from-above>"
```

**Unscoped `DELETE /admin/audit` clears both tables:**

```bash
curl -s -X DELETE -H "X-Api-Key: <your admin key>" "http://localhost:18099/api/v1/admin/audit"
curl -s "http://localhost:18099/api/v1/admin/audit/date-range"
```

Then confirm a **table-scoped** clear leaves `Audit_Change` untouched — run
`DELETE .../admin/audit?table=Quotinator_Quote` and check `SELECT COUNT(*) FROM Audit_Change` via
DbInspector before and after; the count must be unchanged.

## Expected output

**Date-range** — `200` with non-null `earliestDate`/`latestDate`, from the bundled seed's own
`BulkInserted` entries. No `X-Api-Key` required, matching `GET /admin/audit`.

**Export** — headers include `Content-Disposition: attachment; filename="quotinator-audit-export-...json"`.
The body has top-level `entries` and `changes` arrays, both non-empty after a fresh seed.

**Row-count cap** — `422`, never a silently truncated file. A fresh seed produces far more than one
combined row.

**Auto-purge on** — `RemainingActions` is `0`: every bundled batch applies cleanly with no pending
actions, so all of them are auto-purged. `PurgeTraces` equals the number of bundled seed batches — one
per batch, even though the `Import_Action` rows themselves are gone.

`RemainingActions = 0` is the zero-failures assertion in this test: nothing left `Pending`, `Blocked`
or `Stale` after a bundled seed.

**Auto-purge off** — `RemainingActions` is greater than `0`. With the bundled setting off the seeding
path never purges, matching pre-#249 behaviour.

**`purgeOnSuccess`** — the import returns `200` (the curated file re-imports as all-Modify against
already-seeded data, no pending decisions). The reverse returns `422`: the batch's `Import_Action` rows
were purged immediately, so `ReverseBatchAsync` has nothing to reverse.

**Unscoped clear** — `204`. The date-range call afterwards shows `earliestDate`/`latestDate` matching
*only* the clear's own self-recorded `Purged` trace — a single, just-now timestamp — not any earlier
`Audit_Change` activity, which is now also gone. A table-scoped clear leaves `Audit_Change` untouched.

## Observed effect

Partially established. The raw table counts and the purge traces are observed state and are asserted
above.

**The unscoped-clear behaviour was found live during T1**: date-range and export kept showing data
after a clear, because the endpoint had only ever cleared `Audit_Entry` — even though #249 treats both
tables as one combined concern everywhere else.

## Cleanup

```bash
docker rm -f smoke249
rm -f .claude/temp/smoke249.db .claude/temp/smoke249.db-wal .claude/temp/smoke249.db-shm \
      .claude/temp/smoke249b.db .claude/temp/smoke249b.db-wal .claude/temp/smoke249b.db-shm \
      .claude/temp/audit-export.json
```
