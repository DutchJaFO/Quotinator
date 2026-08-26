# A batch applied through the staged flow can be reversed

**Smoke:** no
**Environment:** Fresh
**Traces to:** #177

## Preconditions

A batch applied entirely through the staged review → decide → apply flow — that is, via
`POST /import/actions/apply` directly, **not** `POST /import`'s own single-shot path. The distinction is
the whole subject: only the staged path exhibited the defect.

The steps below stage that batch themselves rather than borrowing one, so this document runs alone.

## Determinism

- **Every pending action must be decided before `apply`.** With any left undecided, `apply` returns
  `422` by design and the reversal is never reached — a real failure would be indistinguishable from
  an incomplete setup.
- The batch must have been applied through `/import/actions/apply`, not `/import`. Applying via the
  single-shot path exercises a different code route and would not have shown the original bug.
- **The status codes are the whole assertion, and that is deliberate.** The defect was
  `Import_Batch.Status` never being set to `Applied` by the staged path, and `reverse` rejecting the
  batch as a result. A regression shows up directly as the status code, so no separate read-back is
  needed here.
- **`preview=true` returning `200` is not a claim that it changed nothing** — nothing in this run reads
  state before and after the preview. It is asserted only as "the preview route answers"; if the
  no-side-effect guarantee ever needs proving, that is a read-back this document does not currently have.

## Steps

### 1. Create this test's own environment

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-import-02 --port 18602
$key  = @{'X-Api-Key' = 'smoketest'}
$base = "http://localhost:18602/api/v1"
```

**Expected:** the app reports healthy — the bundled seed has finished.

**On failure:** every step below reads this container. Stop rather than running them against an app that
never became healthy.

### 2. Stage a batch under `review`

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST --url "$base/import" `
              --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202 `
            | ConvertFrom-Json).batchId
$batchId
```

**Expected:** a non-empty `batchId` — every step below is scoped to it.

**On failure:** an empty value means nothing staged, and each step below would then act on no batch at
all while still returning plausible-looking codes. Stop.

### 3. List this batch's pending actions

```powershell
(Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").totalCount
```

**Expected:** a non-zero count — the actions this batch staged, which step 4 must decide in full.

### 4. Decide every one of them, then confirm none is left

```powershell
foreach ($id in (Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "$base/import/actions/$id/decide" `
    -Headers $key -ContentType 'application/json' -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}
(Invoke-RestMethod "$base/import/actions?status=pending&batchId=$batchId&pageSize=0").totalCount
```

**Expected:** `0`.

**On failure:** with any action still pending, `apply` returns `422` by design and the reversal below is
never reached — so a genuine failure would be indistinguishable from an incomplete setup, which is the
trap this confirmation exists to close. Stop and decide the remainder.

### 5. Apply through the staged path

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/apply?batchId=$batchId" --expect 200 --status
```

**Expected:** `200`.

### 6. Preview the reversal

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$batchId&preview=true" --expect 200 --status
```

**Expected:** `200`, never the `422` this issue reported.

### 7. Reverse the batch

```powershell
dotnet script scripts/testing/http.csx -- --method POST `
  --url "$base/import/actions/reverse?batchId=$batchId" --expect 200 --status
```

**Expected:** `200`, never the `422` this issue reported.

## Observed effect

The original defect: a batch applied entirely through the staged flow never had its own
`Import_Batch.Status` set to `Applied`, so `POST /import/actions/reverse` always rejected it with a bare
`422` even though the batch had genuinely applied.

**If this regresses**, `SqliteImportActionService.ApplyBatchAsync`'s `MarkImportBatchAppliedAsync` call
— gated on `TryApplyBatchAsync` returning `null` — is the one place that sets `Status`/`AppliedAt` for
every caller. Check it was not bypassed by a new caller of `ApplyBatchAsync` or `TryApplyBatchAsync`
added elsewhere.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-import-02
```
