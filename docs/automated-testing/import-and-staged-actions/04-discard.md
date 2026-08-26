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

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-04 --port 18604
$base = "http://localhost:18604/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch by previewing the curated file under `review`

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import/preview" `
              --file data/sources/quotinator-curated.json --duplicate-resolution review `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** the response carries a `batchId` — the readings below are scoped to it.

### 3. Record what the batch staged and what the domain holds, before discarding

```powershell
$before = (Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items
$before | Group-Object status | Select-Object Count, Name
$quotesBefore = (Invoke-RestMethod "$base/version").database.quotes
"actions=$(@($before).Count) quotes=$quotesBefore"
```

**Expected:** the action count is non-zero, and the quote count is recorded for comparison.

**On failure:** a zero action count means nothing was staged, and every assertion below is then
satisfied vacuously. Stop — this is a staging problem, not a discard result.

### 4. Discard the batch

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/discard?batchId=$batchId" --expect 204 --status
```

**Expected:** `204`.

### 5. Read both again

```powershell
$after = (Invoke-RestMethod "$base/import/actions?batchId=$batchId&pageSize=0").items
$after | Group-Object status | Select-Object Count, Name
$quotesAfter = (Invoke-RestMethod "$base/version").database.quotes

"actions=$(@($after).Count) sameCount=$(@($after).Count -eq @($before).Count)"
"notDiscarded=$(@($after | Where-Object { $_.status -ne 'Discarded' }).Count)"
"quotes=$quotesAfter unchanged=$($quotesAfter -eq $quotesBefore)"
```

**Expected:** `sameCount=True` against the same non-zero total, `notDiscarded=0` — every action now
reads `Discarded` — and `unchanged=True`: the domain tables were never touched.

## Observed effect

Not yet established as a captured record.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-04
```
