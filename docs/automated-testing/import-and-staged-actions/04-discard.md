# Discarding a staged batch marks every action discarded and applies nothing

**Smoke:** no
**Environment:** Fresh
**Traces to:** #154

## Preconditions

A staged batch with pending actions — the preview call below produces one under `review` policy.

## Determinism

- **`review` policy is required here**, unlike
  [`03-batch-id-mode-alias.md`](03-batch-id-mode-alias.md): the batch must have pending actions for
  discard to have anything to mark.
- The listing afterwards is scoped to that specific `batchId`, so other batches in the database cannot
  affect the result.
- **The count matters as much as the status**: "every action shows `Discarded`" is satisfied vacuously
  by an empty list, so a discard that hard-deleted the rows, or a `batchId` filter matching nothing,
  would otherwise read as a pass.
- **The quote count is compared before and after rather than asserted as a value.** Creation is
  deferred to apply time, so a discarded batch never touched the domain tables at all — and that claim
  needs a domain read to mean anything. Comparing the count keeps it true whatever the dataset holds.

## Steps

### 1. Create this test's own environment

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-import-04 --port 18604
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch by previewing the curated file under `review`

```bash
batchId=$(curl -s -X POST -H "X-Api-Key: smoketest" \
            -F "file=@data/sources/quotinator-curated.json" \
            -F 'settings={"duplicateResolution":{"default":"review"}}' \
            "http://localhost:18604/api/v1/import/preview" \
          | grep -o '"batchId":"[^"]*"' | cut -d'"' -f4)
echo "batchId=$batchId"
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

### 3. Record what the batch staged and what the domain holds, before discarding

```bash
curl -s "http://localhost:18604/api/v1/import/actions?batchId=$batchId&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
curl -s "http://localhost:18604/api/v1/version" | grep -o '"quotes":[0-9]*'
```

**Expected:** the action count is non-zero, and the quote count is recorded for comparison.

**On failure:** a zero action count means nothing was staged, and every assertion below is then
satisfied vacuously. Stop — this is a staging problem, not a discard result.

### 4. Discard the batch

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: smoketest" \
  "http://localhost:18604/api/v1/import/actions/discard?batchId=$batchId"
```

**Expected:** `204`.

### 5. Read both again

```bash
curl -s "http://localhost:18604/api/v1/import/actions?batchId=$batchId&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
curl -s "http://localhost:18604/api/v1/version" | grep -o '"quotes":[0-9]*'
```

**Expected:** the same non-zero action count as before, every one of them now reading `Discarded`, and
a quote count identical to before.

## Observed effect

Not yet established as a captured record.

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-04
```
