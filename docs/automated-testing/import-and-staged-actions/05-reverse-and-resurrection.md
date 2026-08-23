# Reversing an applied batch undoes it, and re-importing resurrects the rows

**Smoke:** no
**Environment:** Fresh
**Traces to:** #59

## Preconditions

A genuinely `Applied` batch to reverse — the `newest-wins` import below produces one, returning `200`
with nothing left pending.

The out-of-order check at the end needs **at least one other batch applied after** the one being
reversed. That is true of a normal database with seed plus this import history, but it is a
precondition, not a given.

## Determinism

- **`newest-wins` is required for the setup import**, so it applies cleanly and there is an `Applied`
  batch rather than a pending one.
- **Reversal introduces no new action status.** Every action still reads `"status":"Applied"`
  afterwards; the batch's own record being gone is the only signal it was undone. Expecting a
  `Reversed` status would report a false failure.
- There is **no `GET /import-batches` listing endpoint**, so confirming the batch record is gone needs
  `GET /api/v1/admin/audit` or `Quotinator.Tools.DbInspector` against `Import_Batch` showing
  `IsDeleted=1`.
- **Count the search result, do not read it.** `/quotes/search` returns `200` with an empty `items`
  array and a `message` as ordinary behaviour when nothing matches, so an eyeballed response cannot tell
  a successful resurrection from a reversal that deleted the rows for good — which is the failure the
  re-import step exists to catch.

## Steps

Run the **Fresh** profile first.

### 1. Apply a batch cleanly under `newest-wins`

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
```

**Expected:** `200` with nothing left pending — a genuinely `Applied` batch. Note the returned
`batchId`.

### 2. Preview the reversal

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>&preview=true"
```

**Expected:** `200` **without changing anything**.

### 3. Reverse the batch

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```

**Expected:** `200`.

### 4. List the reversed batch's actions

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>"
```

**Expected:** the listing still shows every action `"status":"Applied"` — see Determinism.

### 5. Reverse the same batch again

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<batchId>"
```

**Expected:** `404`: already reversed, treated as absent.

### 6. Re-import after reversal — the resurrection path

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:8080/api/v1/import"
curl -s "http://localhost:8080/api/v1/quotes/search?q=Airplane&field=source&pageSize=0" \
  | grep -o '"totalCount":[0-9]*'
```

**Expected:** the re-import succeeds (`200`/`202`, **never a silent no-op**) and the search's
`totalCount` is **non-zero** — the curated quotes are reachable again. This is the resurrection fix
proven live, rather than only by
`ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`.

**On failure:** a `totalCount` of `0` is the failing case, and it is a `200` response like any other.

### 7. Confirm the soft-delete flag actually flipped back

This is the load-bearing observation here: no action status changes to signal a reversal, so the HTTP
result alone never distinguished the two cases:

```bash
docker stop -t 15 qt-env
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db .claude/temp/smoke-reverse.db
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-wal .claude/temp/smoke-reverse.db-wal
MSYS_NO_PATHCONV=1 docker cp qt-env:/data/quotinatordata.db-shm .claude/temp/smoke-reverse.db-shm
docker start qt-env
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-reverse.db \
  --sql "SELECT IsDeleted, COUNT(*) AS Batches FROM Import_Batch GROUP BY IsDeleted"
```

**Expected:** the reversed batch appears under `IsDeleted = 1`, and at least one batch remains under
`IsDeleted = 0`.

Without this read there is nothing in the run that observes the reversal at all.

### 8. Find the oldest still-live batch

There is no `GET /import-batches` listing endpoint (see Determinism), so the older batch id comes from
the database copy taken above — the *oldest* still-live batch, which is by definition not the most
recently applied one:

```bash
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-reverse.db \
  --sql "SELECT Id, DateCreated FROM Import_Batch WHERE IsDeleted = 0 ORDER BY DateCreated ASC LIMIT 1"
```

**Expected:** an id for a batch older than the one just reversed.

**On failure:** **if that query returns only one row, this step cannot run** — there is no *older* batch
to reverse, so LIFO has nothing to reject and a `422` would prove nothing. That is a precondition
failure, not a result: the profile's own seed must have produced at least one batch before this test's
import.

### 9. Attempt the out-of-order reversal

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/reverse?batchId=<the id from that query>"
```

**Expected:** `422` — the strict LIFO stack rule: only the most recently applied batch still live may be
reversed, regardless of whether it shares any entities with the older one.

## Observed effect

Not yet established as a captured record. The `IsDeleted=1` state on `Import_Batch` is the load-bearing
observation, since no action status changes to signal the reversal.

## Cleanup

```bash
rm -f .claude/temp/smoke-reverse.db .claude/temp/smoke-reverse.db-wal .claude/temp/smoke-reverse.db-shm
```

The applied, reversed and re-imported batches and their actions remain, along with the resurrected
curated rows — restore the Fresh profile before the next test.
