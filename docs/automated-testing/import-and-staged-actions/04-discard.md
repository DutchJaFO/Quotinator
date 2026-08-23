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

Run the **Fresh** profile first.

### 1. Stage a batch by previewing the curated file under `review`

```bash
curl -s -X POST -H "X-Api-Key: <your admin key>" \
  -F "file=@data/sources/quotinator-curated.json" \
  -F 'settings={"duplicateResolution":{"default":"review"}}' \
  "http://localhost:8080/api/v1/import/preview"
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

### 2. Record what the batch staged and what the domain holds, before discarding

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
```

**Expected:** the action count is non-zero, and the quote count is recorded for comparison.

**On failure:** a zero action count means nothing was staged, and every assertion below is then
satisfied vacuously. Stop — this is a staging problem, not a discard result.

### 3. Discard the batch

```bash
curl -s -w "\n%{http_code}\n" -X POST -H "X-Api-Key: <your admin key>" \
  "http://localhost:8080/api/v1/import/actions/discard?batchId=<batchId>"
```

**Expected:** `204`.

### 4. Read both again

```bash
curl -s "http://localhost:8080/api/v1/import/actions?batchId=<batchId>&pageSize=0" \
  | grep -o '"status":"[A-Za-z]*"' | sort | uniq -c
curl -s "http://localhost:8080/api/v1/version" | grep -o '"quotes":[0-9]*'
```

**Expected:** the same non-zero action count as before, every one of them now reading `Discarded`, and
a quote count identical to before.

## Observed effect

Not yet established as a captured record.

## Cleanup

The staged batch and its now-`Discarded` actions remain — restore the Fresh profile before the next
test.
