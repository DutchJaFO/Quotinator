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
  re-import step exists to catch. **Count `totalMatching`**, the field that endpoint actually returns;
  `totalCount` belongs to the paginated list endpoints and matches nothing here.

## Steps

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-05 --port 18605
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Apply a batch cleanly under `newest-wins`

```bash
response=$(curl -s -w "\n%{http_code}" -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  "http://localhost:18605/api/v1/import")
echo "$response" | tail -1
batchId=$(echo "$response" | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "batchId=$batchId"
```

**Expected:** `200` with nothing left pending — a genuinely `Applied` batch — and a non-empty
`batchId`, which every step below is scoped to.

### 3. Preview the reversal

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18605/api/v1/import/actions/reverse?batchId=$batchId&preview=true"
```

**Expected:** `200` **without changing anything**.

### 4. Reverse the batch

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18605/api/v1/import/actions/reverse?batchId=$batchId"
```

**Expected:** `200`.

### 5. List the reversed batch's actions

```bash
curl -s "http://localhost:18605/api/v1/import/actions?batchId=$batchId"
```

**Expected:** the listing still shows every action `"status":"Applied"` — see Determinism.

### 6. Reverse the same batch again

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18605/api/v1/import/actions/reverse?batchId=$batchId"
```

**Expected:** `404`: already reversed, treated as absent.

### 7. Re-import after reversal — the resurrection path

```bash
curl -s -X POST -H "X-Api-Key: smoketest" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"newest-wins"}}' \
  -w "\n%{http_code}\n" \
  "http://localhost:18605/api/v1/import"
curl -s "http://localhost:18605/api/v1/quotes/search?q=Airplane&field=source&pageSize=0" \
  | grep -o '"totalMatching":[0-9]*'
```

**Expected:** the re-import succeeds (`200`/`202`, **never a silent no-op**) and the search's
`totalMatching` is **non-zero** — the curated quotes are reachable again. This is the resurrection fix
proven live, rather than only by
`ApplyResolvedActionAsync_ReAddAfterSoftDelete_ResurrectsSoftDeletedRow`.

**The field is `totalMatching`, not `totalCount`.** `/quotes/search` returns its own shape, and this
step read `totalCount` until #339's full run — a field that endpoint never emits, so the grep was
always empty and "non-zero" could never be satisfied. The resurrection check silently asserted nothing
for as long as it was written that way. See the index's *A count is evidence only if the instrument
counts the right thing*.

**On failure:** a `totalMatching` of `0` is the failing case, and it is a `200` response like any
other. An *empty* reading is different again — that is the grep matching nothing, so check the field
name before concluding anything about the rows.

### 8. Confirm the soft-delete flag actually flipped back

This is the load-bearing observation here: no action status changes to signal a reversal, so the HTTP
result alone never distinguished the two cases:

```bash
docker stop -t 15 qt-import-05
MSYS_NO_PATHCONV=1 docker cp qt-import-05:/data/quotinatordata.db .claude/temp/smoke-reverse.db
MSYS_NO_PATHCONV=1 docker cp qt-import-05:/data/quotinatordata.db-wal .claude/temp/smoke-reverse.db-wal || true
MSYS_NO_PATHCONV=1 docker cp qt-import-05:/data/quotinatordata.db-shm .claude/temp/smoke-reverse.db-shm || true
docker start qt-import-05
until curl -sf http://localhost:18605/api/v1/health > /dev/null; do sleep 1; done
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-reverse.db \
  --sql "SELECT IsDeleted, COUNT(*) AS Batches FROM Import_Batch GROUP BY IsDeleted"
```

**Expected:** the reversed batch appears under `IsDeleted = 1`, and at least one batch remains under
`IsDeleted = 0`.

Without this read there is nothing in the run that observes the reversal at all.

### 9. Find the oldest still-live batch

There is no `GET /import-batches` listing endpoint (see Determinism), so the older batch id comes from
the database copy taken above — the *oldest* still-live batch, which is by definition not the most
recently applied one:

```bash
oldestBatchId=$(dotnet run --project tools/Quotinator.Tools.DbInspector -- --db .claude/temp/smoke-reverse.db \
  --sql "SELECT Id FROM Import_Batch WHERE IsDeleted = 0 ORDER BY DateCreated ASC LIMIT 1" \
  | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' | head -1)
echo "oldestBatchId=$oldestBatchId"
```

**Expected:** a non-empty id, for a batch older than the one just reversed.

**On failure:** **if that query returns only one row, this step cannot run** — there is no *older* batch
to reverse, so LIFO has nothing to reject and a `422` would prove nothing. That is a precondition
failure, not a result: the profile's own seed must have produced at least one batch before this test's
import.

### 10. Attempt the out-of-order reversal

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18605/api/v1/import/actions/reverse?batchId=$oldestBatchId"
```

**Expected:** `422` — the strict LIFO stack rule: only the most recently applied batch still live may be
reversed, regardless of whether it shares any entities with the older one.

## Observed effect

Not yet established as a captured record. The `IsDeleted=1` state on `Import_Batch` is the load-bearing
observation, since no action status changes to signal the reversal.

## Cleanup

```bash
rm -f .claude/temp/smoke-reverse.db .claude/temp/smoke-reverse.db-wal .claude/temp/smoke-reverse.db-shm
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-05
```
